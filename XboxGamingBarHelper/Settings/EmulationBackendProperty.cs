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
            // Experimental: read the stored backend. Default Legacy (ViGEm) when unset.
            // The widget exposes this only behind the debug menu.
            return LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var stored)
                   && stored == (int)EmulationBackend.Viiper;
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
