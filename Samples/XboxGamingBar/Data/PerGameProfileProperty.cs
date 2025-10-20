using Shared.Enums;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class PerGameProfileProperty : WidgetToggleProperty
    {
        public PerGameProfileProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.PerGameProfile, inUI, inOwner)
        {
        }

        public override async Task Sync()
        {
            await base.Sync();

            // TODO Special case for per-game profile.
        }
    }
}
