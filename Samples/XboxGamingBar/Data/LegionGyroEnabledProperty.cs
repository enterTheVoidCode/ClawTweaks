using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LegionGyroEnabledProperty : WidgetToggleProperty
    {
        public LegionGyroEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionGyroEnabled, inUI, inOwner)
        {
        }
    }
}
