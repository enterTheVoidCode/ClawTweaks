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
            // VIIPER is not yet released — always use Legacy ViGEm regardless of stored value.
            // Remove this override and restore the LocalSettings read when VIIPER ships.
            return false;
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
