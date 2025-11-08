using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonAntiLagEnabledProperty : WidgetToggleProperty
    {
        public AMDRadeonAntiLagEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDRadeonAntiLagEnabled, inUI, inOwner)
        {
        }
    }
}
