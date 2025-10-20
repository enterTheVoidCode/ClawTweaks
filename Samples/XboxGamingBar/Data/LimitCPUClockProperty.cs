using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LimitCPUClockProperty : WidgetToggleProperty
    {
        public LimitCPUClockProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LimitCPUClock, inUI, inOwner)
        {
        }
    }
}
