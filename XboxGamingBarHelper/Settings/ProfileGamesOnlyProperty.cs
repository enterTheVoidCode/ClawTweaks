using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class ProfileGamesOnlyProperty : HelperProperty<bool, SettingsManager>
    {
        private const string SettingsKey = "ProfileGamesOnly";

        public ProfileGamesOnlyProperty(SettingsManager inManager) : base(LoadFromSettings(), null, Function.ProfileGamesOnly, inManager)
        {
            Logger.Info($"ProfileGamesOnly loaded: {Value}");
        }

        private static bool LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<bool>(SettingsKey, out var value))
            {
                return value;
            }
            return true; // Default to true (games only)
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            SaveToSettings();
            Logger.Info($"Profile Games Only changed to {Value}");
        }
    }
}
