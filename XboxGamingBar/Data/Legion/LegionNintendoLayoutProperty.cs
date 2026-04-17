using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Nintendo-style face button swap (A↔B, X↔Y)
    /// </summary>
    internal class LegionNintendoLayoutProperty : WidgetToggleProperty
    {
        public LegionNintendoLayoutProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionNintendoLayout, inUI, inOwner)
        {
        }
    }
}
