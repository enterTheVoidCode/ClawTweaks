using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class TDPProperty : SliderProperty
    {
        public TDPProperty(int inValue, Slider inControl, Page inOwner) : base(inValue, Function.TDP, inControl, inOwner)
        {
        }
    }
}
