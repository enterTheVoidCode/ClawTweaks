using Shared.Enums;
using System;
using Windows.Storage;
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
            Logger.Info($"TdpMethod loaded from LocalSettings: {(TdpMethod)Value}");
        }

        private static int LoadFromSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(SettingsKey))
                {
                    return (int)settings.Values[SettingsKey];
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load TdpMethod from LocalSettings: {ex.Message}");
            }
            // Default to ManufacturerWMI for Legion Go devices
            // Non-Legion devices will have WMI option hidden and fall back to PawnIO in the UI
            return (int)TdpMethod.ManufacturerWMI;
        }

        private void SaveToSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[SettingsKey] = Value;
                Logger.Info($"TdpMethod saved to LocalSettings: {(TdpMethod)Value}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save TdpMethod to LocalSettings: {ex.Message}");
            }
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
