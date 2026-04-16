using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using NLog;
using Windows.Storage;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Helper for accessing LocalSettings that works both inside and outside package context.
    /// Falls back to a JSON file when ApplicationData.Current is not available.
    /// </summary>
    internal static class LocalSettingsHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static Dictionary<string, object> _fallbackSettings;
        private static string _fallbackSettingsPath;
        private static bool _useLocalSettings = true;

        static LocalSettingsHelper()
        {
            // Check if we can use LocalSettings
            try
            {
                _ = ApplicationData.Current.LocalSettings;
                Logger.Info("LocalSettingsHelper: Using UWP ApplicationData.Current.LocalSettings");
            }
            catch
            {
                _useLocalSettings = false;
                Logger.Info("LocalSettingsHelper: LocalSettings not available, using file-based fallback");
            }

            // Always initialize fallback file storage so settings persist across context changes.
            // The helper may start in MSIX package context (UWP LocalSettings available) or as a
            // deployed scheduled task (no package identity). Both must read/write the same JSON file
            // to ensure settings like TDP method survive restarts.
            InitializeFallback();
        }

        private static void InitializeFallback()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                // Prefer LocalState when running without package identity.
                var settingsFolder = GetLocalStateFromExePath()
                    ?? Path.Combine(localAppData, "GoTweaks");

                Directory.CreateDirectory(settingsFolder);
                _fallbackSettingsPath = Path.Combine(settingsFolder, "settings.json");
                Logger.Info($"Fallback settings path: {_fallbackSettingsPath}");

                // Migrate from old LocalCache location (if present) to LocalState/GoTweaks.
                TryMigrateLegacySettings(localAppData, settingsFolder);

                if (File.Exists(_fallbackSettingsPath))
                {
                    var json = File.ReadAllText(_fallbackSettingsPath);
                    _fallbackSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                }
                else
                {
                    _fallbackSettings = new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to initialize fallback settings: {ex.Message}");
                _fallbackSettings = new Dictionary<string, object>();
            }
        }

        private static string GetLocalStateFromExePath()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(exePath))
                    return null;

                var packagesIdx = exePath.IndexOf("Packages", StringComparison.OrdinalIgnoreCase);
                if (packagesIdx < 0)
                    return null;

                var localCacheIdx = exePath.IndexOf("LocalCache", StringComparison.OrdinalIgnoreCase);
                var localStateIdx = exePath.IndexOf("LocalState", StringComparison.OrdinalIgnoreCase);

                int baseIdx = localCacheIdx >= 0 ? localCacheIdx : localStateIdx;
                if (baseIdx <= packagesIdx)
                    return null;

                var packageBase = exePath.Substring(0, baseIdx).TrimEnd('\\');
                return Path.Combine(packageBase, "LocalState");
            }
            catch
            {
                return null;
            }
        }

        private static string GetLocalCacheFromExePath()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(exePath))
                    return null;

                var packagesIdx = exePath.IndexOf("Packages", StringComparison.OrdinalIgnoreCase);
                if (packagesIdx < 0)
                    return null;

                var localCacheIdx = exePath.IndexOf("LocalCache", StringComparison.OrdinalIgnoreCase);
                if (localCacheIdx <= packagesIdx)
                    return null;

                var packageBase = exePath.Substring(0, localCacheIdx).TrimEnd('\\');
                return Path.Combine(packageBase, "LocalCache");
            }
            catch
            {
                return null;
            }
        }

        private static void TryMigrateLegacySettings(string localAppData, string newSettingsFolder)
        {
            try
            {
                var legacyFolder = GetLocalCacheFromExePath()
                    ?? Path.Combine(
                        localAppData,
                        "Packages",
                        "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg",
                        "LocalCache"
                    );

                var legacyPath = Path.Combine(legacyFolder, "settings.json");
                if (!File.Exists(legacyPath))
                    return;

                var newPath = Path.Combine(newSettingsFolder, "settings.json");
                if (File.Exists(newPath))
                    return;

                Directory.CreateDirectory(newSettingsFolder);
                File.Copy(legacyPath, newPath, overwrite: false);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Legacy settings migration failed: {ex.Message}");
            }
        }

        private static void SaveFallback()
        {
            if (_fallbackSettingsPath == null || _fallbackSettings == null)
                return;

            try
            {
                var json = JsonSerializer.Serialize(_fallbackSettings);
                File.WriteAllText(_fallbackSettingsPath, json);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to save fallback settings: {ex.Message}");
            }
        }

        public static bool TryGetValue<T>(string key, out T value)
        {
            value = default;

            if (_useLocalSettings)
            {
                try
                {
                    var settings = ApplicationData.Current.LocalSettings;
                    if (settings.Values.TryGetValue(key, out var obj))
                    {
                        if (obj is T typedValue)
                        {
                            value = typedValue;
                            return true;
                        }
                        // Try to convert
                        value = (T)Convert.ChangeType(obj, typeof(T));
                        return true;
                    }
                }
                catch { }
            }
            else if (_fallbackSettings != null)
            {
                if (_fallbackSettings.TryGetValue(key, out var obj))
                {
                    try
                    {
                        if (obj is JsonElement jsonElement)
                        {
                            value = JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                            return true;
                        }
                        if (obj is T typedValue)
                        {
                            value = typedValue;
                            return true;
                        }
                        value = (T)Convert.ChangeType(obj, typeof(T));
                        return true;
                    }
                    catch { }
                }
            }

            return false;
        }

        public static void SetValue(string key, object value)
        {
            if (_useLocalSettings)
            {
                try
                {
                    var settings = ApplicationData.Current.LocalSettings;
                    settings.Values[key] = value;
                }
                catch
                {
                    // Fall through to fallback only
                }
            }

            // Always save to fallback file for cross-context persistence.
            // The helper may run in package context (UWP) or deployed context (scheduled task),
            // and settings must be readable from either.
            if (_fallbackSettings != null)
            {
                _fallbackSettings[key] = value;
                SaveFallback();
            }
        }
    }
}
