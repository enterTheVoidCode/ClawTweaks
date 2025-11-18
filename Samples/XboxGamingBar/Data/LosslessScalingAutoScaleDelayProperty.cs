using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingAutoScaleDelayProperty : WidgetSliderProperty
    {
        public LosslessScalingAutoScaleDelayProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingAutoScaleDelay, inUI, inOwner)
        {
        }
    }
}
