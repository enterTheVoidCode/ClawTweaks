using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class AutoHibernateEnabledProperty : HelperProperty<bool, SettingsManager>
    {
        private const string SettingsKey = "AutoHibernateEnabled";

        public AutoHibernateEnabledProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.AutoHibernateEnabled, inManager)
        {
            Logger.Info($"AutoHibernateEnabled loaded: {Value}");
        }

        private static bool LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<bool>(SettingsKey, out var value))
            {
                return value;
            }
            return false; // Default to off
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            SaveToSettings();
            Logger.Info($"AutoHibernateEnabled changed to {Value}");
        }
    }
}
