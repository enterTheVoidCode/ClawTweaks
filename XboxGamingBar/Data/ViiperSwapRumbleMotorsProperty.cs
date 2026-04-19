using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Swap the large/small motor values before the VIIPER forwarder sends rumble to the
    /// physical XInput controller. Useful when the user's pad reports its heavy motor on
    /// the opposite side from the emulated pad.
    /// </summary>
    internal class ViiperSwapRumbleMotorsProperty : WidgetToggleProperty
    {
        public ViiperSwapRumbleMotorsProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.Viiper_SwapRumbleMotors, inUI, inOwner)
        {
        }
    }
}
