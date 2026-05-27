using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Helper-side MSI Claw Controller/Mouse mode property.
    ///
    /// When the widget writes a new value (Quick Settings tile tap), NotifyPropertyChanged
    /// calls MsiClawControllerModeManager.ApplyMode() which dispatches to
    /// Program.MSIClaw.cs via the static OnModeChanged callback.
    ///
    /// true  = Controller mode (default): ClawButtonMonitor + ViGEm virtual Xbox 360
    /// false = Mouse mode: MSIClawDesktopModeForwarder (RS→cursor, LS→scroll, LB/RB→clicks)
    ///
    /// Default is true so the device starts as a gamepad if no saved state exists.
    /// </summary>
    internal class MsiClawControllerModeProperty : HelperProperty<bool, MsiClawControllerModeManager>
    {
        public MsiClawControllerModeProperty(MsiClawControllerModeManager manager)
            : base(true, null, Function.MsiClawControllerMode, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.ApplyMode(Value);
        }
    }
}
