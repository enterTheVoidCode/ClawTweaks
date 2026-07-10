using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Minimal manager for the MSI Claw firmware keyboard-remap backend toggle.
    ///
    /// Bridges property-change notifications to Program.MSIClaw.cs via a static callback
    /// (OnFwKeyboardModeChanged), registered when the Claw emulation starts. Mirrors
    /// <see cref="MsiClawControllerModeManager"/>.
    ///
    /// true  = firmware keyboard remap (ClawButtonMonitor writes button-bound keyboard shortcuts to
    ///         the controller firmware so they emit a real HID key inside games)
    /// false = software injector (default; existing behaviour, unchanged)
    /// </summary>
    internal class MsiClawFwKeyboardModeManager : IManager
    {
        /// <summary>Registered by Program.MSIClaw.cs to apply the backend change to ClawButtonMonitor.</summary>
        internal static Action<bool> OnFwKeyboardModeChanged;

        /// <summary>Helper-side property exposed for registration in Program.cs property list.</summary>
        public MsiClawFwKeyboardModeProperty MsiClawFwKeyboardMode { get; }

        public MsiClawFwKeyboardModeManager()
        {
            MsiClawFwKeyboardMode = new MsiClawFwKeyboardModeProperty(this);
        }

        internal void ApplyMode(bool firmwareOn)
        {
            OnFwKeyboardModeChanged?.Invoke(firmwareOn);
        }

        public void Update() { }
        public void Dispose() { }
    }
}
