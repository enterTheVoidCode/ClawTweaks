using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
            }
            catch
            {
                _useLocalSettings = false;
                Logger.Info("LocalSettings not available, using file-based fallback");
                InitializeFallback();
            }
        }

        private static void InitializeFallback()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var settingsFolder = Path.Combine(localAppData, "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg", "LocalCache");
                Directory.CreateDirectory(settingsFolder);
                _fallbackSettingsPath = Path.Combine(settingsFolder, "settings.json");

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
                    return;
                }
                catch
                {
                    // Fall through to fallback
                }
            }

            if (_fallbackSettings != null)
            {
                _fallbackSettings[key] = value;
                SaveFallback();
            }
        }
    }
}
