using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingDrawFpsProperty : WidgetToggleProperty
    {
        public LosslessScalingDrawFpsProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingDrawFps, inUI, inOwner)
        {
        }
    }
}
