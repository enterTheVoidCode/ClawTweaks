using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LegionDesktopControlsProperty : WidgetToggleProperty
    {
        public LegionDesktopControlsProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionDesktopControls, inUI, inOwner)
        {
        }
    }
}
