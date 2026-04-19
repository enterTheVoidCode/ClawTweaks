using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// When true, the VIIPER rumble-forwarding path swaps the large/small motor values
    /// before sending them to the physical XInput controller. Useful when the user's
    /// physical pad reports its heavy motor on the opposite side from the emulated pad.
    /// </summary>
    internal class ViiperSwapRumbleMotorsProperty : HelperProperty<bool, SettingsManager>
    {
        public const bool Default = false;
        private const string SettingsKey = "ViiperSwapRumbleMotors";

        public ViiperSwapRumbleMotorsProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_SwapRumbleMotors, inManager)
        {
            Logger.Info($"ViiperSwapRumbleMotors loaded: {Value}");
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
            Logger.Info($"ViiperSwapRumbleMotors changed: {Value}");
            SaveToSettings();
        }
    }
}
