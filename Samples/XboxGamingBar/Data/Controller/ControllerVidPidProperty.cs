using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property for controller VID:PID (e.g., "17EF:6182").
    /// </summary>
    internal class ControllerVidPidProperty : WidgetProperty<string>
    {
        public ControllerVidPidProperty() : base("", null, Function.ControllerVidPid)
        {
        }
    }
}
