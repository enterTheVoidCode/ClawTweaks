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
            // VIIPER is now the default backend. When the key is unset (fresh install or
            // post-factory-reset), default to VIIPER (true). An explicit stored value is honoured
            // either way, so a user who deliberately picked Legacy keeps Legacy.
            // Note: the start path auto-falls back to ViGEm when usbip-win2 is missing, so a
            // VIIPER default never leaves a dead controller (see Program.MSIClaw.cs).
            if (LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var stored))
                return stored == (int)EmulationBackend.Viiper;
            return true; // default: VIIPER
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
