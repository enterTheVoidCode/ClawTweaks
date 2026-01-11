using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Right Trigger End (0-50%)
    /// </summary>
    internal class LegionRightTriggerEndProperty : WidgetSliderProperty
    {
        public LegionRightTriggerEndProperty(Slider inUI, Page inOwner) : base(0, Function.LegionRightTriggerEnd, inUI, inOwner)
        {
        }
    }
}
