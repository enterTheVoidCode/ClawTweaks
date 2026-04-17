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

        /// <summary>
        /// Shows the VIIPER device configuration section when the toggle is on,
        /// and further shows the Steam sub-device picker only when a Steam device type
        /// is selected.
        /// </summary>
        private async void UpdateViiperConfigVisibility()
        {
            if (ViiperConfigSection == null || emulationBackend == null)
            {
                return;
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                bool backendOn = emulationBackend.Value;
                ViiperConfigSection.Visibility = backendOn ? Visibility.Visible : Visibility.Collapsed;

                if (ViiperSteamSubDevicePanel != null && viiperDeviceType != null)
                {
                    string t = viiperDeviceType.Value ?? string.Empty;
                    bool isSteam = t == "steam-generic" || t == "steam-controller" || t == "steamdeck-generic";
                    ViiperSteamSubDevicePanel.Visibility = (backendOn && isSteam) ? Visibility.Visible : Visibility.Collapsed;
                }
            });
        }
    }
}
