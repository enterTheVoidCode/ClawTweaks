using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Property to control which TDP method to use.
    /// Persists to LocalSettings so user choice is remembered across restarts.
    /// </summary>
    internal class TdpMethodProperty : HelperProperty<int, SettingsManager>
    {
        private const string SettingsKey = "TdpMethod";

        public TdpMethodProperty(SettingsManager inManager) : base(LoadFromSettings(), null, Function.Settings_TdpMethod, inManager)
        {
            Logger.Info($"TdpMethod loaded: {(TdpMethod)Value}");
        }

        private static int LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var value))
            {
                return value;
            }
            // Default to ManufacturerWMI for Legion Go devices
            // Non-Legion devices will have WMI option hidden and fall back to PawnIO in the UI
            return (int)TdpMethod.ManufacturerWMI;
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value);
            Logger.Debug($"TdpMethod saved: {(TdpMethod)Value}");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"TDP Method changed to {(TdpMethod)Value}");
            SaveToSettings();
        }

        /// <summary>
        /// Gets the current TDP method as the enum type
        /// </summary>
        public TdpMethod Method => (TdpMethod)Value;
    }
}
