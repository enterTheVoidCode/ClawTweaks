using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if left controller is connected (attached).
    /// </summary>
    internal class ControllerConnectedLeftProperty : WidgetProperty<bool>
    {
        public ControllerConnectedLeftProperty() : base(false, null, Function.ControllerConnectedLeft)
        {
        }
    }
}
