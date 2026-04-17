using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class ProfileMatchByExeProperty : HelperProperty<bool, SettingsManager>
    {
        private const string SettingsKey = "ProfileMatchByExe";

        public ProfileMatchByExeProperty(SettingsManager inManager) : base(LoadFromSettings(), null, Function.ProfileMatchByExe, inManager)
        {
            Logger.Info($"ProfileMatchByExe loaded: {Value}");
        }

        private static bool LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<bool>(SettingsKey, out var value))
            {
                return value;
            }
            return false;
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            SaveToSettings();
            Logger.Info($"Profile Match By Exe changed to {Value}");
        }
    }
}
