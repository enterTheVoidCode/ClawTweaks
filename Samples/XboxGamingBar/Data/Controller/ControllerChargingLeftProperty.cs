using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if left controller is charging.
    /// </summary>
    internal class ControllerChargingLeftProperty : WidgetProperty<bool>
    {
        public ControllerChargingLeftProperty() : base(false, null, Function.ControllerChargingLeft)
        {
        }
    }
}
