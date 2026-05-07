using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// When the active VIIPER virtual device exposes a lightbar (DualShock 4 / DualSense
    /// Edge), mirror the game-asserted color onto the Legion Go stick lights. Off keeps
    /// the user's saved stick color even while VIIPER is running.
    /// </summary>
    internal class ViiperMirrorLightbarToStickProperty : WidgetToggleProperty
    {
        public ViiperMirrorLightbarToStickProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.Viiper_MirrorLightbarToStick, inUI, inOwner)
        {
        }
    }
}
