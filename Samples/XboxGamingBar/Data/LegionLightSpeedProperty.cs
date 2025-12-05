using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion light animation speed (0-100)
    /// </summary>
    internal class LegionLightSpeedProperty : WidgetSliderProperty
    {
        public LegionLightSpeedProperty(Slider inControl, Page inOwner) : base(50, Function.LegionLightSpeed, inControl, inOwner)
        {
        }
    }
}
