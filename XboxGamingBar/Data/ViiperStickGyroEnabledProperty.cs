using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Master enable for the VIIPER Gyro → Right Stick processor. Off makes the
    /// emulated stick passthrough raw (no gyro contribution). Default true to
    /// preserve the always-on behavior the feature shipped with in 0.3.2152.
    /// </summary>
    internal class ViiperStickGyroEnabledProperty : WidgetToggleProperty
    {
        public ViiperStickGyroEnabledProperty(ToggleSwitch inUI, Page inOwner)
            : base(true, Function.Viiper_StickGyroEnabled, inUI, inOwner)
        {
        }
    }
}
