using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side MSI Claw HW-mouse killswitch (Quick Settings tile / action).
    ///
    /// true  = force the CLAW FIRMWARE into its native Desktop mouse mode (stick→cursor, A→click) —
    ///         a real hardware HID mouse that works on the UAC secure desktop, where software
    ///         SendInput cannot. Breaks controller input while active, so the tile is highlighted.
    /// false = controller mode (default).
    ///
    /// Default false and NOT persisted — the killswitch always starts OFF after a helper start.
    /// The helper is authoritative and pushes state back here (e.g. after auto-recovery), so the
    /// tile reflects reality even when the firmware mode changed without a tile tap.
    /// </summary>
    internal class MsiClawHwMouseProperty : WidgetProperty<bool>
    {
        public MsiClawHwMouseProperty() : base(false, null, Function.MsiClawHwMouse) { }
    }
}
