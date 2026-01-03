using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if right controller is charging.
    /// </summary>
    internal class ControllerChargingRightProperty : WidgetProperty<bool>
    {
        public ControllerChargingRightProperty() : base(false, null, Function.ControllerChargingRight)
        {
        }
    }
}
