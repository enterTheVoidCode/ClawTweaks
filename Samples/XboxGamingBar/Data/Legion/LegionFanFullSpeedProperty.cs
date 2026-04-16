using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LegionFanFullSpeedProperty : WidgetToggleProperty
    {
        public LegionFanFullSpeedProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionFanFullSpeed, inUI, inOwner)
        {
        }
    }
}
