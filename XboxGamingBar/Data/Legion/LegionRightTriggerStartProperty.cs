using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Right Trigger Start (0-50%)
    /// </summary>
    internal class LegionRightTriggerStartProperty : WidgetSliderProperty
    {
        public LegionRightTriggerStartProperty(Slider inUI, Page inOwner) : base(0, Function.LegionRightTriggerStart, inUI, inOwner)
        {
        }
    }
}
