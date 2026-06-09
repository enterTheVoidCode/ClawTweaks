using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Generic write-only trigger property (widget → helper) for Setup/Dependencies tool actions.
    /// Write "install" or "uninstall" to trigger; the helper performs the action and pushes back
    /// the matching *Installed status property. Used for RTSS install and all four uninstalls.
    /// </summary>
    internal class ToolTriggerProperty : WidgetProperty<string>
    {
        public ToolTriggerProperty(Page inOwner, Function function) : base("", null, function)
        {
        }

        public void Trigger(string action)
        {
            Logger.Info($"Tool action triggered: {action}");
            SetValue(action);
        }
    }
}
