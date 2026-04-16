using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if right controller is connected (attached).
    /// </summary>
    internal class ControllerConnectedRightProperty : WidgetProperty<bool>
    {
        public ControllerConnectedRightProperty() : base(false, null, Function.ControllerConnectedRight)
        {
        }
    }
}
