using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDImageSharpeningSharpnessProperty : WidgetSliderProperty
    {
        public AMDImageSharpeningSharpnessProperty(Slider inControl, Page inOwner) : base(50, Function.AMDImageSharpeningSharpness, inControl, inOwner)
        {
        }
    }
}
