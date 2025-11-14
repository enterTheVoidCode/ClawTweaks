using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AutoStartRTSSProperty : WidgetToggleProperty
    {
        public AutoStartRTSSProperty(ToggleSwitch inUI, Page inOwner) : base(true, Function.Settings_AutoStartRTSS, inUI, inOwner)
        {
        }
    }
}
