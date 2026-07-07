using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// RTSS is required infrastructure — it's the FPS-limiter/OSD fallback and onboarding only
    /// finalizes once it's installed — so this is no longer a real user preference (the UI toggle
    /// is hidden/disabled, see GamingWidgetSettings.xaml). Force it to true on every construction,
    /// including for existing users whose settings.json carries a "false" migrated from a pre-fork
    /// GoTweaks install (same LocalSettingsHelper key), so they don't silently stay stuck without
    /// RTSS auto-starting.
    /// </summary>
    internal class AutoStartRTSSProperty : HelperProperty<bool, SettingsManager>
    {
        private const string SettingsKey = "AutoStartRTSS";

        public AutoStartRTSSProperty(SettingsManager inManager) : base(true, null, Function.Settings_AutoStartRTSS, inManager)
        {
            if (LocalSettingsHelper.TryGetValue<bool>(SettingsKey, out var stored) && !stored)
            {
                Logger.Warn("AutoStartRTSS was persisted as false (legacy setting) — forcing to true, RTSS is required infrastructure");
            }
            LocalSettingsHelper.SetValue(SettingsKey, true);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Logger.Info($"Auto Start RTSS changed to {Value}");
        }
    }
}
