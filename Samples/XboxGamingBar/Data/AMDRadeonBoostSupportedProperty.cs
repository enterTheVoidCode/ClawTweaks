using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonBoostSupportedProperty : WidgetControlEnabledProperty<ToggleSwitch>
    {
        public AMDRadeonBoostSupportedProperty(ToggleSwitch inUI, Page inOwner) : base(Function.AMDRadeonBoostSupported, inUI, inOwner)
        {
        }
    }
}
