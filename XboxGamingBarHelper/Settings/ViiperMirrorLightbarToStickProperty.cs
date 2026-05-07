using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// When true, the VIIPER input forwarder mirrors the emulated controller's lightbar
    /// color (DualShock 4 / DualSense Edge) onto the Legion Go stick lights. When false,
    /// the user's saved stick color is left intact regardless of what the emulated game
    /// asserts. Default false — the prior "always-on" behavior surprised users by
    /// overriding picker colors with the DS Edge's idle blue (#000040) the moment VIIPER
    /// initialized. Opt-in for users who actually want game-driven LED effects.
    /// </summary>
    internal class ViiperMirrorLightbarToStickProperty : HelperProperty<bool, SettingsManager>
    {
        public const bool Default = false;
        private const string SettingsKey = "ViiperMirrorLightbarToStick";

        public ViiperMirrorLightbarToStickProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_MirrorLightbarToStick, inManager)
        {
            Logger.Info($"ViiperMirrorLightbarToStick loaded: {Value}");
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
            Logger.Info($"ViiperMirrorLightbarToStick changed: {Value}");
            SaveToSettings();
        }
    }
}
