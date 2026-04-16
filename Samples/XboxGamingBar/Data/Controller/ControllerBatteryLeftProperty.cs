using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property for left controller battery percentage (1-100, or -1 if unavailable).
    /// </summary>
    internal class ControllerBatteryLeftProperty : WidgetProperty<int>
    {
        public ControllerBatteryLeftProperty() : base(-1, null, Function.ControllerBatteryLeft)
        {
        }
    }
}
