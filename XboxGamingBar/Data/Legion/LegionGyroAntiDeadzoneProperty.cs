using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for the gyro output anti-deadzone: the radius, in percent of full stick deflection,
    /// that a gyro-only movement is lifted to while the physical stick is at rest. Its job is to clear
    /// the GAME's own stick deadzone, which is why it belongs in the per-game profile — every game
    /// brings a different one. Range 0-50, default 20 (Motion Assistant's value).
    /// </summary>
    internal class LegionGyroAntiDeadzoneProperty : WidgetSliderProperty
    {
        public LegionGyroAntiDeadzoneProperty(Slider inUI, Page inOwner) : base(20, Function.LegionGyroAntiDeadzone, inUI, inOwner)
        {
        }
    }
}
