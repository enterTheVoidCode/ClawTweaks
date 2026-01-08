using System;
using Shared.Enums;
using Windows.Storage;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class ProfileMatchByExeProperty : HelperProperty<bool, SettingsManager>
    {
        private const string SettingsKey = "ProfileMatchByExe";

        public ProfileMatchByExeProperty(SettingsManager inManager) : base(LoadFromSettings(), null, Function.ProfileMatchByExe, inManager)
        {
            Logger.Info($"ProfileMatchByExe loaded from LocalSettings: {Value}");
        }

        private static bool LoadFromSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(SettingsKey, out var value))
                {
                    return value is bool b && b;
                }
            }
            catch { }
            return false;
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
                Logger.Error($"Failed to save ProfileMatchByExe: {ex.Message}");
            }
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            SaveToSettings();
            Logger.Info($"Profile Match By Exe changed to {Value}");
        }
    }
}
