using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonSuperResolutionSharpnessProperty : WidgetSliderProperty
    {
        public AMDRadeonSuperResolutionSharpnessProperty(Slider inControl, Page inOwner) : base(75, Function.AMDRadeonSuperResolutionSharpness, inControl, inOwner)
        {
        }
    }
}
