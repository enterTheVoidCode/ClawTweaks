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

        protected override void SetControlEnabled(bool isEnabled)
        {
            // Per-game profile should be enabled/disabled differently.
            // base.SetControlEnabled(isEnabled);
        }
    }
}
