using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Hair Triggers preset (0%/1% for instant response)
    /// </summary>
    internal class LegionHairTriggersProperty : WidgetToggleProperty
    {
        public LegionHairTriggersProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionHairTriggers, inUI, inOwner)
        {
        }
    }
}
