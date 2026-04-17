using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class AutoHibernateIdleMinutesProperty : HelperProperty<int, SettingsManager>
    {
        private const string SettingsKey = "AutoHibernateIdleMinutes";

        public AutoHibernateIdleMinutesProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.AutoHibernateIdleMinutes, inManager)
        {
            Logger.Info($"AutoHibernateIdleMinutes loaded: {Value}");
        }

        private static int LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var value))
            {
                return value;
            }
            return 15; // Default to 15 minutes
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            SaveToSettings();
            Logger.Info($"AutoHibernateIdleMinutes changed to {Value}");
        }
    }
}
