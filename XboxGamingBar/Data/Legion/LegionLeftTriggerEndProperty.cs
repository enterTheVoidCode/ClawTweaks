using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Left Trigger End (0-50%)
    /// </summary>
    internal class LegionLeftTriggerEndProperty : WidgetSliderProperty
    {
        public LegionLeftTriggerEndProperty(Slider inUI, Page inOwner) : base(0, Function.LegionLeftTriggerEnd, inUI, inOwner)
        {
        }
    }
}
