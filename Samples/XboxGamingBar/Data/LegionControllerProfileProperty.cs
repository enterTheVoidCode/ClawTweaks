using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for per-game controller profile toggle.
    /// This enables/disables per-game controller button remapping profiles.
    /// </summary>
    internal class LegionControllerProfileProperty : WidgetToggleProperty
    {
        public LegionControllerProfileProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionControllerProfileEnabled, inUI, inOwner)
        {
        }

        protected override void SetControlEnabled(bool isEnabled)
        {
            // Controller profile toggle enabled/disabled is managed by game detection
            // Don't let the base class override it
        }
    }
}
