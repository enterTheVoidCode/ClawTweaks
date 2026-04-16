using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Left Stick Deadzone (0-50%)
    /// </summary>
    internal class LegionLeftStickDeadzoneProperty : WidgetSliderProperty
    {
        public LegionLeftStickDeadzoneProperty(Slider inUI, Page inOwner) : base(4, Function.LegionLeftStickDeadzone, inUI, inOwner)
        {
        }
    }
}
