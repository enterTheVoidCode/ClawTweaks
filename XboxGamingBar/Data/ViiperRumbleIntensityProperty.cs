using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Percent multiplier (0..200) applied to rumble motor values before the VIIPER
    /// forwarder sends them to the physical XInput controller. 100 is unity.
    /// </summary>
    internal class ViiperRumbleIntensityProperty : WidgetSliderProperty
    {
        public ViiperRumbleIntensityProperty(int inValue, Slider inControl, Page inOwner)
            : base(inValue, Function.Viiper_RumbleIntensity, inControl, inOwner)
        {
        }
    }
}
