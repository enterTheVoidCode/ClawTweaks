using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side MSI Claw firmware keyboard-remap backend toggle (Controller Status card, A2VM only).
    ///
    /// true  = firmware keyboard remap: button-bound keyboard shortcuts from the controller profile are
    ///         written to the controller firmware, which emits a real HID key seen inside games.
    /// false = software injector (default): the existing, robust injector path.
    ///
    /// Only shown when the helper reports DeviceSupportsFirmwareKeyboardRemap. Persisted + synced to the
    /// helper (→ ClawButtonMonitor.SetFirmwareKeyboardMode) like any WidgetToggleProperty.
    /// </summary>
    internal class MsiClawFwKeyboardModeProperty : WidgetToggleProperty
    {
        public MsiClawFwKeyboardModeProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.MsiClawFwKeyboardMode, inUI, inOwner)
        {
        }
    }
}
