using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDImageSharpeningEnabledProperty : WidgetToggleProperty
    {
        public AMDImageSharpeningEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDImageSharpeningEnabled, inUI, inOwner)
        {
        }
    }
}
