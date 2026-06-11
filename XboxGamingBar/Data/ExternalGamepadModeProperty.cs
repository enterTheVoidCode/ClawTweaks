using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side "External Gamepad Mode" toggle (Quick Settings tile).
    ///
    /// true  = hide ALL handheld controllers (native MSI + virtual ViGEm) via HidHide, so only an
    ///         externally connected gamepad remains visible.
    /// false = restore the normal handheld controller state.
    ///
    /// Default false and NOT persisted — the tile always starts OFF after a helper start (the
    /// handheld needs its own HW/virtual controller after every reboot).
    /// </summary>
    internal class ExternalGamepadModeProperty : WidgetProperty<bool>
    {
        public ExternalGamepadModeProperty() : base(false, null, Function.ExternalGamepadMode) { }
    }
}
