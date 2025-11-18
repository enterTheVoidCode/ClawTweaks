using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingSharpnessProperty : WidgetSliderProperty
    {
        public LosslessScalingSharpnessProperty(int inValue, Slider inUI, Page inOwner)
            : base(inValue, Function.LosslessScalingSharpness, inUI, inOwner)
        {
        }
    }
}
