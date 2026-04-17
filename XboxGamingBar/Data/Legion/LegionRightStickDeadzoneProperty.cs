using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Right Stick Deadzone (0-50%)
    /// </summary>
    internal class LegionRightStickDeadzoneProperty : WidgetSliderProperty
    {
        public LegionRightStickDeadzoneProperty(Slider inUI, Page inOwner) : base(4, Function.LegionRightStickDeadzone, inUI, inOwner)
        {
        }
    }
}
