using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonAntiLagSupportedProperty : WidgetControlEnabledProperty<ToggleSwitch>
    {
        public AMDRadeonAntiLagSupportedProperty(ToggleSwitch inUI, Page inOwner) : base(Function.AMDRadeonAntiLagSupported, inUI, inOwner)
        {
        }
    }
}
