using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Master enable for the VIIPER Gyro → Right Stick processor on no-native-motion
    /// targets (xbox360, xboxelite2, switchpro family, etc.). When false, the
    /// processor short-circuits and the physical right stick passes through
    /// untouched — same behavior as if the section had never shipped.
    ///
    /// Default false (issue #79 round 5): vvalente30's last good build (2137)
    /// predated this feature entirely. Even with bias correction disabled, the
    /// processor still runs the One-Euro filter, sensitivity scaling, deadzone,
    /// and the new stickConversion=2 default — feel changes from "no gyro on
    /// xbox360 VIIPER" (2137 baseline) to "smoothed-mapped gyro→stick".
    /// Defaulting off restores 2137 behavior; users can opt in from the panel UI.
    /// </summary>
    internal class ViiperStickGyroEnabledProperty : HelperProperty<bool, SettingsManager>
    {
        public const bool Default = false;
        private const string SettingsKey = "ViiperStickGyroEnabled";

        public ViiperStickGyroEnabledProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_StickGyroEnabled, inManager)
        {
            Logger.Info($"ViiperStickGyroEnabled loaded: {Value}");
        }

        private static bool LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<bool>(SettingsKey, out var value))
            {
                return value;
            }
            return Default;
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ViiperStickGyroEnabled changed: {Value}");
            SaveToSettings();
        }
    }
}
