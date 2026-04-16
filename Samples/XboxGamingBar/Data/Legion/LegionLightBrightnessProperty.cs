using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion light brightness (0-100)
    /// </summary>
    internal class LegionLightBrightnessProperty : WidgetSliderProperty
    {
        public LegionLightBrightnessProperty(Slider inControl, Page inOwner) : base(100, Function.LegionLightBrightness, inControl, inOwner)
        {
        }
    }
}
