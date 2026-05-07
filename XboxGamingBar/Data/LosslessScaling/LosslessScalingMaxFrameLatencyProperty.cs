using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingMaxFrameLatencyProperty : WidgetSliderProperty
    {
        public LosslessScalingMaxFrameLatencyProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingMaxFrameLatency, inUI, inOwner)
        {
        }
    }
}
