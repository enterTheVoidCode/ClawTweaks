using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Enums;
using Windows.Storage;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class ProfileCustomGamePathProperty : HelperProperty<string, SettingsManager>
    {
        private const char Separator = '|';
        private const string SettingsKey = "ProfileCustomGamePath";

        public ProfileCustomGamePathProperty(SettingsManager inManager) : base(LoadFromSettings(), null, Function.ProfileCustomGamePath, inManager)
        {
            Logger.Info($"ProfileCustomGamePath loaded from LocalSettings: {GetPaths().Count} paths");
        }

        private static string LoadFromSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(SettingsKey, out var value))
                {
                    return value as string ?? "";
                }
            }
            catch { }
            return "";
        }

        private void SaveToSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[SettingsKey] = Value ?? "";
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save ProfileCustomGamePath: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the list of custom game paths from the pipe-separated value
        /// </summary>
        public List<string> GetPaths()
        {
            if (string.IsNullOrEmpty(Value))
            {
                return new List<string>();
            }
            return Value.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        /// <summary>
        /// Checks if a path matches any custom game path
        /// </summary>
        public bool ContainsPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            var paths = GetPaths();
            return paths.Any(customPath => customPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Persist to LocalSettings
            SaveToSettings();

            var paths = GetPaths();
            Logger.Info($"Profile Custom Game Paths changed: {paths.Count} paths configured");
        }
    }
}
