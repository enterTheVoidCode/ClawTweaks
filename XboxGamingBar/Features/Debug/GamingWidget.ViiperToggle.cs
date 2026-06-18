using System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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

        private static string ViiperDeviceFriendlyName(string tag)
        {
            switch (tag)
            {
                case "dualshock4":    return "DualShock 4";
                case "dualsenseedge": return "DualSense Edge";
                case "xboxelite2":    return "Xbox Elite 2";
                case "steam-generic": return "Steam Controller";
                case "switchpro":     return "Switch Pro";
                default:              return tag;
            }
        }

        /// <summary>
        /// Confirmation gate for the VIIPER virtual device-type picker (wired via
        /// ViiperStringComboProperty.ConfirmChangeAsync). Switching to any non-Xbox-360 type can upset
        /// the Xbox Game Bar, so warn first. Uses the native <see cref="ContentDialog"/> so it is fully
        /// controller-navigable (D-pad between buttons, A confirms, B = cancel), with focus defaulting
        /// to "stay on Xbox 360". Three outcomes:
        ///   • Close (default focus) — stay on Xbox 360 (returns false → combo reverts, no switch).
        ///   • Primary — switch anyway.
        ///   • Secondary — switch AND enable "auto-switch to Xbox in Game Bar" (experimental).
        /// Switching back TO xbox360 never warns.
        /// </summary>
        private async System.Threading.Tasks.Task<bool> ConfirmViiperDeviceSwitchAsync(string oldValue, string newValue)
        {
            if (newValue == "xbox360") return true;

            string name = ViiperDeviceFriendlyName(newValue);
            var dialog = new ContentDialog
            {
                Title = "Non-Xbox controller type",
                Content = $"Switching the virtual controller to {name} can cause problems in the Xbox Game Bar "
                        + "(e.g. right-trigger spamming or overlay navigation issues). In games it usually works fine.\n\n"
                        + "Xbox 360 is the most compatible type for the Game Bar.",
                CloseButtonText = "Stay on Xbox 360",
                PrimaryButtonText = "Switch anyway",
                SecondaryButtonText = "Switch + Auto-Xbox",
                DefaultButton = ContentDialogButton.Close
            };

            try
            {
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    return true; // switch, leave the auto-swap toggle untouched
                if (result == ContentDialogResult.Secondary)
                {
                    // Experimental: also turn on "auto-switch to Xbox while the Game Bar is open" so the
                    // overlay stays usable. Setting IsOn fires the toggle's handler → pushes to helper.
                    if (ViiperGameBarAutoXboxSwapToggle != null && !ViiperGameBarAutoXboxSwapToggle.IsOn)
                        ViiperGameBarAutoXboxSwapToggle.IsOn = true;
                    return true;
                }
                return false; // Close / default → stay on Xbox 360
            }
            catch (Exception ex)
            {
                Logger.Warn($"ConfirmViiperDeviceSwitchAsync: dialog failed ({ex.Message}) — proceeding with switch");
                return true; // never block the user on a dialog error
            }
        }
    }
}
