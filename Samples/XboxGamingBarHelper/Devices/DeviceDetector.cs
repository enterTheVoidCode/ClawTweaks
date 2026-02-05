using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Threading;

namespace XboxGamingBarHelper.Devices
{
    /// <summary>
    /// Detects device information using WMI queries and matches against registered device configurations.
    /// This provides an agnostic way to detect devices and enable device-specific features.
    /// </summary>
    public static class DeviceDetector
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Cached device info to avoid repeated WMI queries
        /// </summary>
        private static DeviceInfo _cachedDeviceInfo = null;

        /// <summary>
        /// Lock object for thread-safe cache access
        /// </summary>
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Whether debug mode is active (debug.json override applied)
        /// </summary>
        private static bool _isDebugModeActive = false;

        /// <summary>
        /// Package family name for constructing LocalState path
        /// </summary>
        private const string PackageFamilyName = "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg";

        /// <summary>
        /// Debug override file name
        /// </summary>
        private const string DebugFileName = "debug.json";

        /// <summary>
        /// Device cache file name for fast startup
        /// </summary>
        private const string CacheFileName = "device_cache.json";

        /// <summary>
        /// Returns true if debug mode is active (device info was overridden via debug.json)
        /// </summary>
        public static bool IsDebugModeActive => _isDebugModeActive;

        /// <summary>
        /// Detects device information from WMI and determines device type.
        /// Results are cached in memory and on disk for fast startup.
        /// Thread-safe - only one WMI query will be made even if called from multiple threads.
        /// </summary>
        public static DeviceInfo DetectDevice()
        {
            // Fast path: return cached result if available
            if (_cachedDeviceInfo != null)
            {
                return _cachedDeviceInfo;
            }

            // Thread-safe initialization - only one thread does the work
            lock (_cacheLock)
            {
                // Double-check after acquiring lock
                if (_cachedDeviceInfo != null)
                {
                    return _cachedDeviceInfo;
                }

                var deviceInfo = new DeviceInfo();
                var timer = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    // Check if debug.json exists - if so, skip cache to ensure fresh detection
                    var debugFilePath = GetDebugFilePath();
                    bool debugFileExists = !string.IsNullOrEmpty(debugFilePath) && File.Exists(debugFilePath);

                    // Step 1: Try to load from disk cache (instant startup) - skip if debug.json exists
                    if (!debugFileExists && TryLoadFromDiskCache(out var cachedInfo))
                    {
                        deviceInfo = cachedInfo;
                        timer.Stop();
                        Logger.Info($"[TIMING] Device detection from disk cache: {timer.ElapsedMilliseconds}ms");
                    }
                    else
                    {
                        if (debugFileExists)
                        {
                            Logger.Info("Debug override file exists - skipping device cache");
                        }

                        // Step 2: Query WMI for device information (single combined query)
                        QueryDeviceInfoCombined(deviceInfo);

                        timer.Stop();
                        Logger.Info($"[TIMING] Device detection from WMI: {timer.ElapsedMilliseconds}ms");

                        // Save to disk cache for next startup (only if not in debug mode)
                        if (!debugFileExists)
                        {
                            SaveToDiskCache(deviceInfo);
                        }
                    }

                    // Step 3: Check for debug override file (always check, even with cache)
                    ApplyDebugOverrides(deviceInfo);

                    // Step 4: Match against registered device configurations
                    var matchedConfig = DeviceRegistry.FindMatchingDevice(deviceInfo);

                    if (matchedConfig != null)
                    {
                        // Apply the matched device's features
                        matchedConfig.ApplyFeatures(deviceInfo);
                        Logger.Info($"Device detected: {matchedConfig.DisplayName}");
                    }
                    else
                    {
                        // No match - use generic device type
                        deviceInfo.DeviceType = DeviceType.Generic;
                        Logger.Info("Device type: Generic (no specific device matched)");
                    }

                    LogDeviceInfo(deviceInfo, timer.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    Logger.Error($"Device detection failed after {timer.ElapsedMilliseconds}ms: {ex.Message}");
                    deviceInfo.DeviceType = DeviceType.Generic;
                }

                _cachedDeviceInfo = deviceInfo;
                return deviceInfo;
            }
        }

        /// <summary>
        /// Clears the cached device info, forcing re-detection on next call.
        /// </summary>
        public static void ClearCache()
        {
            _cachedDeviceInfo = null;
            Logger.Debug("Device detection cache cleared");
        }

        /// <summary>
        /// Queries both Win32_ComputerSystemProduct and Win32_ComputerSystem in a single WMI connection.
        /// This is faster than two separate queries (~500ms vs ~1000ms).
        /// </summary>
        private static void QueryDeviceInfoCombined(DeviceInfo deviceInfo)
        {
            try
            {
                // Use a single ManagementScope for both queries (reuses connection)
                var scope = new ManagementScope(@"\\.\root\cimv2");
                scope.Connect();

                // Query 1: Win32_ComputerSystemProduct for Vendor, Name, Version
                using (var searcher1 = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Vendor, Name, Version FROM Win32_ComputerSystemProduct")))
                {
                    searcher1.Options.Timeout = TimeSpan.FromSeconds(3);
                    foreach (var obj in searcher1.Get())
                    {
                        deviceInfo.Manufacturer = obj["Vendor"]?.ToString()?.Trim() ?? "Unknown";
                        deviceInfo.Model = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                        deviceInfo.Version = obj["Version"]?.ToString()?.Trim() ?? "Unknown";
                        break;
                    }
                }

                // Query 2: Win32_ComputerSystem for SystemFamily (same connection)
                using (var searcher2 = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT SystemFamily FROM Win32_ComputerSystem")))
                {
                    searcher2.Options.Timeout = TimeSpan.FromSeconds(3);
                    foreach (var obj in searcher2.Get())
                    {
                        deviceInfo.SystemFamily = obj["SystemFamily"]?.ToString()?.Trim() ?? "Unknown";
                        break;
                    }
                }

                Logger.Debug($"WMI Combined Query: Vendor={deviceInfo.Manufacturer}, Name={deviceInfo.Model}, Version={deviceInfo.Version}, SystemFamily={deviceInfo.SystemFamily}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to query WMI: {ex.Message}");
            }
        }

        /// <summary>
        /// Queries Win32_ComputerSystemProduct for Vendor, Name, and Version
        /// </summary>
        [Obsolete("Use QueryDeviceInfoCombined instead for better performance")]
        private static void QueryComputerSystemProduct(DeviceInfo deviceInfo)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Vendor, Name, Version FROM Win32_ComputerSystemProduct"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(5);

                    foreach (var obj in searcher.Get())
                    {
                        deviceInfo.Manufacturer = obj["Vendor"]?.ToString()?.Trim() ?? "Unknown";
                        deviceInfo.Model = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                        deviceInfo.Version = obj["Version"]?.ToString()?.Trim() ?? "Unknown";
                        break; // Only one result expected
                    }
                }

                Logger.Debug($"Win32_ComputerSystemProduct: Vendor={deviceInfo.Manufacturer}, Name={deviceInfo.Model}, Version={deviceInfo.Version}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to query Win32_ComputerSystemProduct: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies debug overrides from debug.json if present in LocalState folder.
        /// This allows developers to test device-specific features without the actual hardware.
        /// </summary>
        private static void ApplyDebugOverrides(DeviceInfo deviceInfo)
        {
            try
            {
                var debugFilePath = GetDebugFilePath();
                Logger.Info($"Debug override check - path: {debugFilePath ?? "(null)"}");

                if (string.IsNullOrEmpty(debugFilePath))
                {
                    Logger.Info("Debug file path is null or empty");
                    return;
                }

                if (!File.Exists(debugFilePath))
                {
                    Logger.Info($"Debug file does not exist at: {debugFilePath}");
                    return;
                }

                Logger.Warn($"DEBUG MODE: Found debug override file at {debugFilePath}");

                var json = File.ReadAllText(debugFilePath);
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    // Store original values for logging
                    var originalManufacturer = deviceInfo.Manufacturer;
                    var originalModel = deviceInfo.Model;
                    var originalVersion = deviceInfo.Version;

                    // Apply overrides
                    if (root.TryGetProperty("manufacturer", out var manufacturer) && manufacturer.ValueKind == JsonValueKind.String)
                    {
                        deviceInfo.Manufacturer = manufacturer.GetString();
                    }

                    if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
                    {
                        deviceInfo.Model = model.GetString();
                    }

                    if (root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
                    {
                        deviceInfo.Version = version.GetString();
                    }

                    if (root.TryGetProperty("systemFamily", out var systemFamily) && systemFamily.ValueKind == JsonValueKind.String)
                    {
                        deviceInfo.SystemFamily = systemFamily.GetString();
                    }

                    _isDebugModeActive = true;

                    Logger.Warn($"DEBUG MODE: Overriding device info:");
                    Logger.Warn($"  Manufacturer: {originalManufacturer} -> {deviceInfo.Manufacturer}");
                    Logger.Warn($"  Model: {originalModel} -> {deviceInfo.Model}");
                    Logger.Warn($"  Version: {originalVersion} -> {deviceInfo.Version}");
                    Logger.Warn($"  SystemFamily: {deviceInfo.SystemFamily}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply debug overrides: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the path to the debug.json file in LocalState folder.
        /// </summary>
        private static string GetDebugFilePath()
        {
            try
            {
                // Try to get LocalState from package API
                try
                {
                    var localState = global::Windows.Storage.ApplicationData.Current.LocalFolder;
                    return Path.Combine(localState.Path, DebugFileName);
                }
                catch
                {
                    // API may not be available when running outside package context
                }

                // Fallback: derive from exe path
                var exePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (exePath.Contains("LocalCache"))
                {
                    // Helper runs from: .../LocalCache/GoTweaks/Helper/
                    // LocalState is at: .../LocalState/
                    int idx = exePath.IndexOf("LocalCache", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        var packageBase = exePath.Substring(0, idx);
                        return Path.Combine(packageBase, "LocalState", DebugFileName);
                    }
                }

                // Last resort: construct from environment
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "Packages", PackageFamilyName, "LocalState", DebugFileName);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to get debug file path: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Queries Win32_ComputerSystem for SystemFamily
        /// </summary>
        [Obsolete("Use QueryDeviceInfoCombined instead for better performance")]
        private static void QueryComputerSystem(DeviceInfo deviceInfo)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SystemFamily FROM Win32_ComputerSystem"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(5);

                    foreach (var obj in searcher.Get())
                    {
                        deviceInfo.SystemFamily = obj["SystemFamily"]?.ToString()?.Trim() ?? "Unknown";
                        break; // Only one result expected
                    }
                }

                Logger.Debug($"Win32_ComputerSystem: SystemFamily={deviceInfo.SystemFamily}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to query Win32_ComputerSystem: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs detailed device information
        /// </summary>
        private static void LogDeviceInfo(DeviceInfo deviceInfo, long elapsedMs)
        {
            Logger.Info($"[TIMING] Device detection completed in {elapsedMs}ms");
            Logger.Info($"Detected device: {deviceInfo}");
            Logger.Info($"  Manufacturer: {deviceInfo.Manufacturer}");
            Logger.Info($"  Model: {deviceInfo.Model}");
            Logger.Info($"  Version: {deviceInfo.Version}");
            Logger.Info($"  SystemFamily: {deviceInfo.SystemFamily}");
            Logger.Info($"  DeviceType: {deviceInfo.DeviceType}");
            Logger.Info($"  Features - WMI TDP: {deviceInfo.SupportsWmiTdp}, Controller: {deviceInfo.SupportsControllerRemap}, RGB: {deviceInfo.SupportsRgbLighting}, Gyro: {deviceInfo.SupportsGyro}, FanControl: {deviceInfo.SupportsFanControl}");
            Logger.Info($"  Hardware - Touchpad: {deviceInfo.HasTouchpad}, ScrollWheel: {deviceInfo.HasScrollWheel}, DetachableControllers: {deviceInfo.HasDetachableControllers}");
        }

        /// <summary>
        /// Checks if the detected device is any Legion device
        /// </summary>
        public static bool IsLegionDevice()
        {
            var device = DetectDevice();
            return device.IsLegionDevice;
        }

        /// <summary>
        /// Gets the display name for the detected device
        /// </summary>
        public static string GetDeviceDisplayName()
        {
            var device = DetectDevice();
            var config = DeviceRegistry.GetByType(device.DeviceType);
            return config?.DisplayName ?? "Generic Device";
        }

        /// <summary>
        /// Tries to load device info from disk cache for fast startup.
        /// Returns false if cache doesn't exist or is invalid.
        /// </summary>
        private static bool TryLoadFromDiskCache(out DeviceInfo deviceInfo)
        {
            deviceInfo = null;

            try
            {
                var cachePath = GetCacheFilePath();
                if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
                {
                    return false;
                }

                var json = File.ReadAllText(cachePath);
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    // Validate cache version
                    if (!root.TryGetProperty("cacheVersion", out var versionProp) || versionProp.GetInt32() != 1)
                    {
                        Logger.Debug("Device cache version mismatch, will refresh from WMI");
                        return false;
                    }

                    deviceInfo = new DeviceInfo
                    {
                        Manufacturer = root.GetProperty("manufacturer").GetString() ?? "Unknown",
                        Model = root.GetProperty("model").GetString() ?? "Unknown",
                        Version = root.GetProperty("version").GetString() ?? "Unknown",
                        SystemFamily = root.GetProperty("systemFamily").GetString() ?? "Unknown"
                    };

                    Logger.Debug($"Loaded device info from disk cache: {deviceInfo.Manufacturer} {deviceInfo.Model}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to load device cache: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves device info to disk cache for fast startup next time.
        /// </summary>
        private static void SaveToDiskCache(DeviceInfo deviceInfo)
        {
            try
            {
                var cachePath = GetCacheFilePath();
                if (string.IsNullOrEmpty(cachePath))
                {
                    return;
                }

                var cacheData = new
                {
                    cacheVersion = 1,
                    manufacturer = deviceInfo.Manufacturer,
                    model = deviceInfo.Model,
                    version = deviceInfo.Version,
                    systemFamily = deviceInfo.SystemFamily,
                    savedAt = DateTime.UtcNow.ToString("o")
                };

                var json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(cachePath, json);

                Logger.Debug($"Saved device info to disk cache: {cachePath}");
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to save device cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the path to the device cache file in LocalCache folder.
        /// Uses LocalCache (not LocalState) because this is transient data.
        /// </summary>
        private static string GetCacheFilePath()
        {
            try
            {
                // Try to get LocalCache from package API
                try
                {
                    var localCache = global::Windows.Storage.ApplicationData.Current.LocalCacheFolder;
                    return Path.Combine(localCache.Path, CacheFileName);
                }
                catch
                {
                    // API may not be available when running outside package context
                }

                // Fallback: derive from exe path (helper runs from LocalCache/GoTweaks/Helper/)
                var exePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (exePath.Contains("LocalCache"))
                {
                    int idx = exePath.IndexOf("LocalCache", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        var packageBase = exePath.Substring(0, idx);
                        return Path.Combine(packageBase, "LocalCache", CacheFileName);
                    }
                }

                // Last resort: construct from environment
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "Packages", PackageFamilyName, "LocalCache", CacheFileName);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to get cache file path: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Invalidates the disk cache, forcing a WMI refresh on next detection.
        /// Call this when hardware configuration might have changed.
        /// </summary>
        public static void InvalidateDiskCache()
        {
            try
            {
                var cachePath = GetCacheFilePath();
                if (!string.IsNullOrEmpty(cachePath) && File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                    Logger.Info("Device disk cache invalidated");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to invalidate disk cache: {ex.Message}");
            }
        }
    }
}
