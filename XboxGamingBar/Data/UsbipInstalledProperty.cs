using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only widget property mirroring whether usbip-win2 is installed on the host.
    /// Synced from the helper; widget uses this to show/hide the install prompt.
    /// </summary>
    internal class UsbipInstalledProperty : WidgetProperty<bool>
    {
        public UsbipInstalledProperty() : base(false, null, Function.Viiper_UsbipInstalled)
        {
        }
    }
}
