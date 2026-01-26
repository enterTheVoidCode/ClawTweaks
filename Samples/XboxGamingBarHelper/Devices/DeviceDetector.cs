using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.IO;
using System.Management;
using System.Text.Json;

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
        /// Returns true if debug mode is active (device info was overridden via debug.json)
        /// </summary>
        public static bool IsDebugModeActive => _isDebugModeActive;

        /// <summary>
        /// Detects device information from WMI and determines device type.
        /// Results are cached after first call.
        /// </summary>
        public static DeviceInfo DetectDevice()
        {
            if (_cachedDeviceInfo != null)
            {
                return _cachedDeviceInfo;
            }

            var deviceInfo = new DeviceInfo();
            var timer = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Step 1: Query WMI for device information
                QueryComputerSystemProduct(deviceInfo);
                QueryComputerSystem(deviceInfo);

                // Step 2: Check for debug override file
                ApplyDebugOverrides(deviceInfo);

                // Step 3: Match against registered device configurations
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

                timer.Stop();
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

        /// <summary>
        /// Clears the cached device info, forcing re-detection on next call.
        /// </summary>
        public static void ClearCache()
        {
            _cachedDeviceInfo = null;
            Logger.Debug("Device detection cache cleared");
        }

        /// <summary>
        /// Queries Win32_ComputerSystemProduct for Vendor, Name, and Version
        /// </summary>
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
                if (string.IsNullOrEmpty(debugFilePath) || !File.Exists(debugFilePath))
                {
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
            Logger.Info($"  Hardware - Touchpad: {deviceInfo.HasTouchpad}, ScrollWheel: {deviceInfo.HasScrollWheel}");
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
    }
}
