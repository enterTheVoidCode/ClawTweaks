using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class ProfileGamesOnlyProperty : WidgetToggleProperty
    {
        public ProfileGamesOnlyProperty(ToggleSwitch inUI, Page inOwner) : base(true, Function.ProfileGamesOnly, inUI, inOwner)
        {
        }
    }
}
