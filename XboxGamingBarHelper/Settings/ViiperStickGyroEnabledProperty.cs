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
    /// Default true to preserve the always-on behavior the feature shipped with in
    /// 0.3.2152, before the master toggle existed. Users who want raw stick
    /// passthrough on Xbox 360 VIIPER targets can flip this from the panel UI.
    /// </summary>
    internal class ViiperStickGyroEnabledProperty : HelperProperty<bool, SettingsManager>
    {
        public const bool Default = true;
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
