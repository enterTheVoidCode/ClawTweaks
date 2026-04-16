using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonChillSupportedProperty : WidgetControlEnabledProperty<ToggleSwitch>
    {
        public AMDRadeonChillSupportedProperty(ToggleSwitch inUI, Page inOwner) : base(Function.AMDRadeonChillSupported, inUI, inOwner)
        {
        }
    }
}
