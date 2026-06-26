using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// "LED color based on battery State of Charge" toggle (MSI Claw). When on, the helper tints the
    /// controller LED by battery % (blue → green → yellow → orange → red → purple), only while the
    /// LED is on and only on 10% band crossings. Helper persists the setting and applies/restores.
    /// </summary>
    internal class LedColorBySocProperty : WidgetToggleProperty
    {
        public LedColorBySocProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LedColorBySoc, inUI, inOwner)
        {
        }
    }
}
