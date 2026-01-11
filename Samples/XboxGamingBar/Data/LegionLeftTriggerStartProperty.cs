using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Left Trigger Start (0-50%)
    /// </summary>
    internal class LegionLeftTriggerStartProperty : WidgetSliderProperty
    {
        public LegionLeftTriggerStartProperty(Slider inUI, Page inOwner) : base(0, Function.LegionLeftTriggerStart, inUI, inOwner)
        {
        }
    }
}
