using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingLSFG3TargetProperty : WidgetSliderProperty
    {
        public LosslessScalingLSFG3TargetProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingLSFG3Target, inUI, inOwner)
        {
        }
    }
}
