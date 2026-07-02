using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Helper-side MSI Claw HW-mouse killswitch property.
    ///
    /// When the widget writes a new value (Quick Settings tile / action), NotifyPropertyChanged
    /// calls MsiClawHwMouseManager.Apply() which dispatches to Program.MSIClaw.cs via OnChanged
    /// (Enter/ExitHwMouseKillswitch).
    ///
    /// true  = firmware Desktop mouse mode forced on (stick→cursor, A→click; works on the UAC
    ///         secure desktop). The virtual controller is preserved, only suspended.
    /// false = controller mode (default).
    ///
    /// Default false; NOT loaded from settings — the killswitch always starts OFF after a helper
    /// start, so the helper always boots in controller mode.
    /// </summary>
    internal class MsiClawHwMouseProperty : HelperProperty<bool, MsiClawHwMouseManager>
    {
        public MsiClawHwMouseProperty(MsiClawHwMouseManager manager)
            : base(false, null, Function.MsiClawHwMouse, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.Apply(Value);
        }
    }
}
