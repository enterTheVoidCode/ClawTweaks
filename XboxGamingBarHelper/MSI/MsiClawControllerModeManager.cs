using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Minimal manager for the MSI Claw Controller/Mouse mode Quick Settings tile.
    ///
    /// Satisfies the IManager constraint of HelperProperty and bridges property
    /// change notifications to Program.MSIClaw.cs lifecycle logic via a static
    /// callback delegate (OnModeChanged), registered during StartMSIClawControllerEmulation().
    ///
    /// true  = Controller mode: ClawButtonMonitor + ViGEm virtual Xbox 360 controller
    /// false = Mouse mode: MSIClawDesktopModeForwarder (RS→cursor, LS→scroll, LB/RB→clicks)
    /// </summary>
    internal class MsiClawControllerModeManager : IManager
    {
        /// <summary>
        /// Callback registered by Program.MSIClaw.cs to handle mode-change notifications.
        /// true = switch to Controller mode; false = switch to Mouse mode.
        /// </summary>
        internal static Action<bool> OnModeChanged;

        /// <summary>The helper-side property exposed for registration in Program.cs property list.</summary>
        public MsiClawControllerModeProperty MsiClawControllerMode { get; }

        public MsiClawControllerModeManager()
        {
            MsiClawControllerMode = new MsiClawControllerModeProperty(this);
        }

        /// <summary>
        /// Called by MsiClawControllerModeProperty.NotifyPropertyChanged() when the widget writes a new value.
        /// </summary>
        internal void ApplyMode(bool controllerOn)
        {
            OnModeChanged?.Invoke(controllerOn);
        }

        // IManager: no polling or disposable resources in this manager.
        public void Update() { }
        public void Dispose() { }
    }
}
