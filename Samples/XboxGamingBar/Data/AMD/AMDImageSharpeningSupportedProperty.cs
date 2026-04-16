using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDImageSharpeningSupportedProperty : WidgetControlEnabledProperty<ToggleSwitch>
    {
        public AMDImageSharpeningSupportedProperty(ToggleSwitch inUI, Page inOwner) : base(Function.AMDImageSharpeningSupported, inUI, inOwner)
        {
        }
    }
}
