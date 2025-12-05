using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion power button LED
    /// </summary>
    internal class LegionPowerLightProperty : WidgetToggleProperty
    {
        public LegionPowerLightProperty(ToggleSwitch inUI, Page inOwner) : base(true, Function.LegionPowerLight, inUI, inOwner)
        {
        }
    }
}
