using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonChillMaxFPSProperty : WidgetSliderProperty
    {
        public AMDRadeonChillMaxFPSProperty(Slider inControl, Page inOwner) : base(60, Function.AMDRadeonChillMaxFPS, inControl, inOwner)
        {
        }
    }
}
