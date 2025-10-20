using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class CPUEPPProperty : WidgetSliderProperty
    {
        public CPUEPPProperty(int inValue, Slider inControl, Page inOwner) : base(inValue, Function.CPUEPP, inControl, inOwner)
        {
        }
    }
}
