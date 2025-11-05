using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonSuperResolutionSupportedProperty : WidgetControlEnabledProperty<ToggleSwitch>
    {
        public AMDRadeonSuperResolutionSupportedProperty(ToggleSwitch inUI, Page inOwner) : base(Function.AMDRadeonSuperResolutionSupported, inUI, inOwner)
        {
        }
    }
}
