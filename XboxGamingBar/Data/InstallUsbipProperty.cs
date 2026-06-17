using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property to trigger installation of the bundled usbip-win2 MSI (VIIPER backend prerequisite).
    /// Write "install" to trigger the installation on the helper side.
    /// </summary>
    internal class InstallUsbipProperty : WidgetProperty<string>
    {
        private readonly Page owner;

        public InstallUsbipProperty(Page inOwner) : base("", null, Function.InstallUsbip)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Triggers usbip-win2 installation via the helper.
        /// </summary>
        public void TriggerInstall()
        {
            Logger.Info("Triggering usbip-win2 installation...");
            SetValue("install");
        }
    }
}
