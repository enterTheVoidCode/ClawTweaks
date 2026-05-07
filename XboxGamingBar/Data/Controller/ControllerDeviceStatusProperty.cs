using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property carrying a JSON snapshot of the controller's device status
    /// (firmware, RGB, brightness, mode, speed, vibration, touchpad). Pushed by the
    /// helper from the b0:01 status report; rendered into the Legion Info card.
    /// </summary>
    internal class ControllerDeviceStatusProperty : WidgetProperty<string>
    {
        public ControllerDeviceStatusProperty() : base("", null, Function.ControllerDeviceStatus)
        {
        }
    }
}
