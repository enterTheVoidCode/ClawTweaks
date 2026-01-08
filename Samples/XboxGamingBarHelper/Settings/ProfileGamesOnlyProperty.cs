using System;
using Shared.Enums;
using Windows.Storage;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class ProfileGamesOnlyProperty : HelperProperty<bool, SettingsManager>
    {
        private const string SettingsKey = "ProfileGamesOnly";

        public ProfileGamesOnlyProperty(SettingsManager inManager) : base(LoadFromSettings(), null, Function.ProfileGamesOnly, inManager)
        {
            Logger.Info($"ProfileGamesOnly loaded from LocalSettings: {Value}");
        }

        private static bool LoadFromSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(SettingsKey, out var value))
                {
                    return !(value is bool b) || b; // Default true if not bool
                }
            }
            catch { }
            return true; // Default to true (games only)
        }

        private void SaveToSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[SettingsKey] = Value;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save ProfileGamesOnly: {ex.Message}");
            }
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            SaveToSettings();
            Logger.Info($"Profile Games Only changed to {Value}");
        }
    }
}
