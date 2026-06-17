using System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        /// <summary>
        /// Shows or hides the "usbip-win2 required" warning card based on the
        /// emulation backend toggle and the helper-reported USBIP install status.
        /// Also refreshes the always-visible status line under the VIIPER toggle —
        /// added per issue #79 vvalente30 so users see the prereq state proactively
        /// (not just when they flip the toggle and discover input doesn't work).
        /// </summary>
        private async void UpdateUsbipCardVisibility()
        {
            if (emulationBackend == null || usbipInstalled == null)
            {
                return;
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                bool backendOn = emulationBackend.Value;
                bool installed = usbipInstalled.Value;

                if (UsbipInstallCard != null)
                {
                    UsbipInstallCard.Visibility = (backendOn && !installed)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                if (UsbipPrereqStatusLine != null)
                {
                    UsbipPrereqStatusLine.Text = installed
                        ? "usbip-win2 driver: detected ✓"
                        : "usbip-win2 driver: not detected — install + reboot required";
                    UsbipPrereqStatusLine.Foreground = installed
                        ? new SolidColorBrush(Color.FromArgb(0xFF, 0x66, 0xCC, 0x66))
                        : new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xC1, 0x07));
                }
            });
        }

        /// <summary>
        /// Swaps the Controller Emulation card body between the legacy ControllerEmulationContent
        /// and the new ViiperEmulationContent based on the backend toggle state. When expanded,
        /// only one of the two is visible at a time — they are not run concurrently.
        /// Also manages the Steam sub-device sub-panel visibility inside the VIIPER panel.
        /// </summary>
        private async void UpdateViiperConfigVisibility()
        {
            if (emulationBackend == null)
            {
                return;
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // The picker must reflect the *running* VIIPER pad, not just the backend selection:
                // show it only when VIIPER is the active backend AND controller emulation is actually
                // enabled. In HW-controller mode (emulation off) there is no virtual pad to configure.
                bool backendOn = emulationBackend.Value && (controllerEmulationEnabled?.Value == true);

                // The Controller Emulation card body (gyro / remaps / mouse mode) is backend-INDEPENDENT
                // on the Claw — those settings are processed in ClawButtonMonitor before the submit point,
                // so they apply equally to ViGEm and VIIPER. It therefore follows ONLY the card's expand
                // state in both backends (it is no longer swapped out for the VIIPER body). The VIIPER body
                // (forwarder-only, GoTweaks/Legion path) is never shown on the Claw; the VIIPER device
                // picker now lives in the Controller Status card (ViiperDeviceConfigPanel).
                if (ViiperEmulationContent != null) ViiperEmulationContent.Visibility = Visibility.Collapsed;
                if (ControllerEmulationContent != null)
                {
                    ControllerEmulationContent.Visibility = LastCardExpandedBeforeHide
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                // VIIPER device picker: visible only while the VIIPER backend is active.
                if (ViiperDeviceConfigPanel != null)
                    ViiperDeviceConfigPanel.Visibility = backendOn ? Visibility.Visible : Visibility.Collapsed;

                if (ViiperSteamSubDevicePanel != null && viiperDeviceType != null)
                {
                    string t = viiperDeviceType.Value ?? string.Empty;
                    bool isSteam = t == "steam-generic" || t == "steam-controller" || t == "steamdeck-generic";
                    ViiperSteamSubDevicePanel.Visibility = (backendOn && isSteam) ? Visibility.Visible : Visibility.Collapsed;
                }
            });
        }

        // Track whether the user had the Controller Emulation card expanded so that we can
        // restore the expanded body after switching backends.
        private bool LastCardExpandedBeforeHide;
    }
}
