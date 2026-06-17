using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Experimental, opt-in: when a NON-Xbox VIIPER device is mounted, hot-swap to xbox360 while the
    /// Xbox Game Bar is open (and back to the chosen type on close) so the overlay can be navigated
    /// cleanly. The helper enforces the guards (emulation active, External Gamepad Mode off,
    /// non-xbox360 device mounted). Off by default.
    /// </summary>
    internal class ViiperGameBarAutoXboxSwapProperty : WidgetToggleProperty
    {
        public ViiperGameBarAutoXboxSwapProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.Viiper_GameBarAutoXboxSwap, inUI, inOwner)
        {
        }
    }
}
