using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Experimental, opt-in: when the VIIPER virtual device is a NON-Xbox type (e.g. DualShock 4)
    /// and the Xbox Game Bar opens, hot-swap the virtual controller to xbox360 so the overlay can
    /// be navigated cleanly (some non-Xbox pads spam the right trigger to the Game Bar). On Game Bar
    /// close, the virtual device is swapped back to the user's selected type.
    ///
    /// Guards (enforced where the swap is performed, not here): controller emulation must be active,
    /// External Gamepad Mode must be OFF, and a non-xbox360 device must actually be mounted. Off by
    /// default — this is an experimental convenience that briefly disconnects/reconnects the virtual
    /// USB device, which a running game can notice.
    /// </summary>
    internal class ViiperGameBarAutoXboxSwapProperty : HelperProperty<bool, SettingsManager>
    {
        public const bool Default = false;
        private const string SettingsKey = "ViiperGameBarAutoXboxSwap";

        public ViiperGameBarAutoXboxSwapProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_GameBarAutoXboxSwap, inManager)
        {
            Logger.Info($"ViiperGameBarAutoXboxSwap loaded: {Value}");
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
            Logger.Info($"ViiperGameBarAutoXboxSwap changed: {Value}");
            SaveToSettings();
        }
    }
}
