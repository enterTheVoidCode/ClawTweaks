using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonChillEnabledProperty : WidgetToggleProperty
    {
        public AMDRadeonChillEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDRadeonChillEnabled, inUI, inOwner)
        {
        }
    }
}
