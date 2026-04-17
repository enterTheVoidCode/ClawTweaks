using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Property to select controller emulation backend.
    /// Currently a binary toggle: false = Legacy ViGEm (default), true = VIIPER.
    /// Global setting, persisted to LocalSettings.
    /// </summary>
    internal class EmulationBackendProperty : HelperProperty<bool, SettingsManager>
    {
        private const string SettingsKey = "EmulationBackend";

        public EmulationBackendProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Settings_EmulationBackend, inManager)
        {
            Logger.Info($"EmulationBackend loaded: {Backend}");
        }

        private static bool LoadFromSettings()
        {
            // Stored as int so future enum expansion stays compatible.
            if (LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var value))
            {
                return value == (int)EmulationBackend.Viiper;
            }
            return false; // Default to Legacy.
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value ? (int)EmulationBackend.Viiper : (int)EmulationBackend.Legacy);
            Logger.Debug($"EmulationBackend saved: {Backend}");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"Emulation backend changed to {Backend}");
            SaveToSettings();
        }

        public EmulationBackend Backend => Value ? EmulationBackend.Viiper : EmulationBackend.Legacy;
    }
}
