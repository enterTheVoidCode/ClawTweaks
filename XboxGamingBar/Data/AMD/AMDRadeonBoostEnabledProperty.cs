using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonBoostEnabledProperty : WidgetToggleProperty
    {
        public AMDRadeonBoostEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDRadeonBoostEnabled, inUI, inOwner)
        {
        }
    }
}
