using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class ProfileMatchByExeProperty : WidgetToggleProperty
    {
        public ProfileMatchByExeProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.ProfileMatchByExe, inUI, inOwner)
        {
        }
    }
}
