using Shared.Constants;
using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class CPUClockMaxProperty : WidgetSliderProperty
    {
        public CPUClockMaxProperty(Slider inControl, Page inOwner) : base(CPUConstants.DEFAULT_CPU_CLOCK, Function.CPUClockMax, inControl, inOwner)
        {
        }
    }
}
