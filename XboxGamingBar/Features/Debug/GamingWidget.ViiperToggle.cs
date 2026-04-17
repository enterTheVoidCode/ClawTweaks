using System;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        /// <summary>
        /// Shows or hides the "usbip-win2 required" card based on the current
        /// emulation backend toggle and the helper-reported USBIP install status.
        /// </summary>
        private async void UpdateUsbipCardVisibility()
        {
            if (UsbipInstallCard == null || emulationBackend == null || usbipInstalled == null)
            {
                return;
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                bool backendOn = emulationBackend.Value;
                bool driverMissing = !usbipInstalled.Value;
                UsbipInstallCard.Visibility = (backendOn && driverMissing)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            });
        }
    }
}
