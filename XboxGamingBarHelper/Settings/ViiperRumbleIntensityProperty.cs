using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Percentage multiplier (0-200) applied to rumble motor values before forwarding
    /// them to the physical controller. 100 = unity, 0 = mute rumble, 200 = double strength.
    /// </summary>
    internal class ViiperRumbleIntensityProperty : HelperProperty<int, SettingsManager>
    {
        public const int Default = 100;
        public const int Minimum = 0;
        public const int Maximum = 200;
        private const string SettingsKey = "ViiperRumbleIntensity";

        public ViiperRumbleIntensityProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_RumbleIntensity, inManager)
        {
            Logger.Info($"ViiperRumbleIntensity loaded: {Value}");
        }

        private static int LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var value))
            {
                if (value < Minimum) value = Minimum;
                if (value > Maximum) value = Maximum;
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
            Logger.Info($"ViiperRumbleIntensity changed: {Value}");
            SaveToSettings();
        }
    }
}
