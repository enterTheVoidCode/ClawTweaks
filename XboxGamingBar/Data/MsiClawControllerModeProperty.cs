using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side MSI Claw Controller/Mouse mode toggle (Quick Settings tile).
    ///
    /// true  = Controller mode (ClawButtonMonitor + ViGEm virtual Xbox 360 controller)
    /// false = Mouse mode (MSIClawDesktopModeForwarder: RS→cursor, LS→scroll, LB/RB→clicks)
    ///
    /// Default is true (Controller mode) so the device behaves as a gamepad immediately.
    /// Writing toggles the mode in the helper via MsiClawControllerModeManager.ApplyMode().
    /// </summary>
    internal class MsiClawControllerModeProperty : WidgetProperty<bool>
    {
        public MsiClawControllerModeProperty() : base(true, null, Function.MsiClawControllerMode) { }
    }
}
