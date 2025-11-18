using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingFrameGenEnabledProperty : WidgetToggleProperty
    {
        public LosslessScalingFrameGenEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LosslessScalingFrameGenEnabled, inUI, inOwner)
        {
        }
    }
}
