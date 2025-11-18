using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingAutoScaleProperty : WidgetToggleProperty
    {
        public LosslessScalingAutoScaleProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingAutoScale, inUI, inOwner)
        {
        }
    }
}
