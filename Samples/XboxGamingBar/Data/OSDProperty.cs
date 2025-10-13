using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class OSDProperty : SliderProperty
    {
        public OSDProperty(int inValue, Slider inControl, Page inOwner) : base(inValue, Function.OSD, inControl, inOwner)
        {
        }
    }
}
