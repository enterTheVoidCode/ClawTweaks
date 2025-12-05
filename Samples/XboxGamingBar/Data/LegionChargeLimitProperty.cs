using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion battery charge limit (80%)
    /// </summary>
    internal class LegionChargeLimitProperty : WidgetToggleProperty
    {
        public LegionChargeLimitProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionChargeLimit, inUI, inOwner)
        {
        }
    }
}
