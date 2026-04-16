using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDRadeonChillMinFPSProperty : WidgetSliderProperty
    {
        public AMDRadeonChillMinFPSProperty(Slider inControl, Page inOwner) : base(30, Function.AMDRadeonChillMinFPS, inControl, inOwner)
        {
        }
    }
}
