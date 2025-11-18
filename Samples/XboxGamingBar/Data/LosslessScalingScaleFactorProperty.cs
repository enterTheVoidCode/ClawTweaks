using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingScaleFactorProperty : WidgetSliderProperty
    {
        public LosslessScalingScaleFactorProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingScaleFactor, inUI, inOwner)
        {
        }
    }
}
