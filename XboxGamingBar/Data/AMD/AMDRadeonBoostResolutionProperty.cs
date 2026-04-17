using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonBoostResolutionProperty : WidgetSliderProperty
    {
        public AMDRadeonBoostResolutionProperty(Slider inControl, Page inOwner) : base(0, Function.AMDRadeonBoostResolution, inControl, inOwner)
        {
        }
    }
}
