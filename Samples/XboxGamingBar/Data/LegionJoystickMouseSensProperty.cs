using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Joystick Mouse Sensitivity (10-100)
    /// </summary>
    internal class LegionJoystickMouseSensProperty : WidgetSliderProperty
    {
        public LegionJoystickMouseSensProperty(Slider inUI, Page inOwner) : base(50, Function.LegionJoystickMouseSens, inUI, inOwner)
        {
        }
    }
}
