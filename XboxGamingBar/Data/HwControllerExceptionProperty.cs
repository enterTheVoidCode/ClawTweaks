using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Per-game "HW Controller Exception" toggle. When on for the running game (and controller
    /// emulation is active), the helper swaps the virtual Viiper/ViGEm pad for the native HW
    /// controller at the next game start. Helper is authoritative and pushes the current game's
    /// state here; this property forwards user toggles to the helper.
    /// </summary>
    internal class HwControllerExceptionProperty : WidgetToggleProperty
    {
        public HwControllerExceptionProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.HwControllerException, inUI, inOwner)
        {
        }

        protected override void SetControlEnabled(bool isEnabled)
        {
            // Enable/disable is driven by code-behind visibility gating (emulation on + game running),
            // not by the helper sync — mirror PerGameProfileProperty and skip the base behavior.
        }
    }
}
