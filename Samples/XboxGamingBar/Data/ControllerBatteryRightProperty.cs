using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property for right controller battery percentage (1-100, or -1 if unavailable).
    /// </summary>
    internal class ControllerBatteryRightProperty : WidgetProperty<int>
    {
        public ControllerBatteryRightProperty() : base(-1, null, Function.ControllerBatteryRight)
        {
        }
    }
}
