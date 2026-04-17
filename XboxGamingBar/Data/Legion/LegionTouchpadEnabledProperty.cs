using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LegionTouchpadEnabledProperty : WidgetToggleProperty
    {
        public LegionTouchpadEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(true, Function.LegionTouchpadEnabled, inUI, inOwner)
        {
        }
    }
}
