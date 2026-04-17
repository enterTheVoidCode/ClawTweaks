using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingFlowScaleProperty : WidgetSliderProperty
    {
        public LosslessScalingFlowScaleProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingFlowScale, inUI, inOwner)
        {
        }
    }
}
