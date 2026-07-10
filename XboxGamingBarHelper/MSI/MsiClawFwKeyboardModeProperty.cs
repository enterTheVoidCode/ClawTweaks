using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Helper-side MSI Claw firmware keyboard-remap backend toggle.
    ///
    /// Persisted across helper restarts via LocalSettingsHelper (like ProfileMatchByExe) — a plain
    /// HelperProperty defaults to false on every start, so without this the toggle silently reverted
    /// to OFF after each reboot. When the widget writes a new value, NotifyPropertyChanged persists it
    /// and calls MsiClawFwKeyboardModeManager.ApplyMode() → Program.MSIClaw.cs →
    /// ClawButtonMonitor.SetFirmwareKeyboardMode. Default false = software injector.
    /// </summary>
    internal class MsiClawFwKeyboardModeProperty : HelperProperty<bool, MsiClawFwKeyboardModeManager>
    {
        private const string SettingsKey = "MsiClawFwKeyboardMode";

        public MsiClawFwKeyboardModeProperty(MsiClawFwKeyboardModeManager manager)
            : base(LoadFromSettings(), null, Function.MsiClawFwKeyboardMode, manager)
        {
            Logger.Info($"MsiClawFwKeyboardMode loaded: {Value}");
        }

        private static bool LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<bool>(SettingsKey, out var value))
            {
                return value;
            }
            return false;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            LocalSettingsHelper.SetValue(SettingsKey, Value);
            Manager.ApplyMode(Value);
        }
    }
}
