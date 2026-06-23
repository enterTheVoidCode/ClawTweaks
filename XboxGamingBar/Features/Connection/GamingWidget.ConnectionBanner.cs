using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        /// <summary>
        /// Shows the connection status banner with appropriate color and message based on state.
        /// </summary>
        /// <param name="state">The banner state to display</param>
        private void ShowConnectionBanner(BannerState state)
        {
            Logger.Info($"[BANNER] ShowConnectionBanner called: state={state}");
            if (ConnectionStatusBanner == null || ConnectionStatusText == null)
            {
                Logger.Warn($"[BANNER] ShowConnectionBanner: Banner controls are null! ConnectionStatusBanner={ConnectionStatusBanner != null}, ConnectionStatusText={ConnectionStatusText != null}");
                return;
            }

            // Segoe MDL2 Assets glyphs per state
            string GlyphError = ((char)0xEA39).ToString();   // StatusErrorFull
            string GlyphSync  = ((char)0xE895).ToString();   // Sync
            string GlyphAdmin = ((char)0xE7EF).ToString();   // Admin / shield (UAC)

            switch (state)
            {
                case BannerState.Disconnected:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 51, 51)); // #CC3333
                    ConnectionStatusText.Text = "Not connected to helper";
                    SetBannerIcon(GlyphError);
                    break;
                case BannerState.Syncing:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Syncing...";
                    SetBannerIcon(GlyphSync);
                    break;
                case BannerState.Reconnecting:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 102, 51)); // #CC6633
                    ConnectionStatusText.Text = "Reconnecting...";
                    SetBannerIcon(GlyphSync);
                    break;
                case BannerState.Launching:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Launching helper...";
                    SetBannerIcon(GlyphSync);
                    break;
                case BannerState.Loading:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Loading...";
                    SetBannerIcon(GlyphSync);
                    break;
                case BannerState.InitialSetup:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 51, 153)); // #663399 Purple
                    // This banner shows while the elevated setup runs. In Game Mode / fullscreen the
                    // UAC prompt opens BEHIND the game, so the user must alt-tab to it. Spell that out.
                    ConnectionStatusText.Text = "Setup running — please confirm the Windows admin (UAC) prompt. In fullscreen games it opens in the background, so switch to the desktop (Win/Alt+Tab) to confirm. The widget reloads automatically.";
                    SetBannerIcon(GlyphAdmin);
                    break;
                case BannerState.Upgrading:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 153, 102)); // #339966 Green
                    ConnectionStatusText.Text = "Updating — a UAC prompt may appear in the background. Please confirm it to complete the update.";
                    SetBannerIcon(GlyphAdmin);
                    break;
            }
            ConnectionStatusBanner.Visibility = Visibility.Visible;
            if (BannerDimOverlay != null)
                BannerDimOverlay.Visibility = Visibility.Visible;

            // Also update the small status indicator dot
            switch (state)
            {
                case BannerState.Disconnected:
                    UpdateHelperStatusIndicator(HelperStatus.Disconnected);
                    break;
                case BannerState.Syncing:
                case BannerState.Reconnecting:
                case BannerState.Launching:
                case BannerState.Loading:
                case BannerState.InitialSetup:
                case BannerState.Upgrading:
                    UpdateHelperStatusIndicator(HelperStatus.Connecting);
                    break;
            }
        }

        /// <summary>
        /// Sets the leading glyph on the connection banner (Segoe MDL2 Assets).
        /// </summary>
        private void SetBannerIcon(string glyph)
        {
            if (ConnectionStatusIcon != null)
            {
                ConnectionStatusIcon.Glyph = glyph;
            }
        }

        /// <summary>
        /// Hides the connection status banner.
        /// </summary>
        private void HideConnectionBanner()
        {
            Logger.Info("[BANNER] HideConnectionBanner called");
            if (ConnectionStatusBanner != null)
            {
                ConnectionStatusBanner.Visibility = Visibility.Collapsed;
                Logger.Info("[BANNER] ConnectionStatusBanner visibility set to Collapsed");
            }
            else
            {
                Logger.Warn("[BANNER] HideConnectionBanner: ConnectionStatusBanner is null!");
            }

            if (BannerDimOverlay != null)
                BannerDimOverlay.Visibility = Visibility.Collapsed;

            // When banner is hidden, we're connected - show green status
            UpdateHelperStatusIndicator(HelperStatus.Connected);
        }

        /// <summary>
        /// Helper connection status for the status indicator dot.
        /// </summary>
        private enum HelperStatus
        {
            Disconnected,  // Red - not connected
            Connecting,    // Yellow - connecting/syncing/launching
            Connected      // Green - fully connected
        }

        /// <summary>
        /// Updates the small helper status indicator dot in the Quick tab corner.
        /// </summary>
        private void UpdateHelperStatusIndicator(HelperStatus status)
        {
            if (HelperStatusDot == null) return;

            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                switch (status)
                {
                    case HelperStatus.Disconnected:
                        HelperStatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 85, 85)); // #FF5555 Red
                        if (HelperStatusText != null) HelperStatusText.Text = "Disconnected";
                        break;
                    case HelperStatus.Connecting:
                        HelperStatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 50)); // #FFC832 Yellow/Orange
                        if (HelperStatusText != null) HelperStatusText.Text = "Connecting...";
                        break;
                    case HelperStatus.Connected:
                        HelperStatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 200, 85)); // #55C855 Green
                        if (HelperStatusText != null) HelperStatusText.Text = "Connected";
                        break;
                }
            });
        }

        /// <summary>
        /// Handle tap on helper status indicator to show/hide status text.
        /// </summary>
        private void HelperStatusIndicator_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (HelperStatusText != null)
            {
                HelperStatusText.Visibility = HelperStatusText.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        /// <summary>
        /// Shows a reboot-recommended dialog when the widget detects a version mismatch between
        /// itself and the running helper. The old helper process is still live (MSIX in-place updates
        /// do not kill running elevated processes), so the update is only fully active after a reboot
        /// or after the helper auto-restarts via the scheduled task (requires UAC confirmation).
        ///
        /// "Reboot now" sends a forced system reboot via the helper's power-action pipe.
        /// "Later" dismisses the dialog; the automatic restart flow continues in parallel.
        /// </summary>
        private async Task ShowVersionMismatchRebootDialogAsync(string helperVersion, string widgetVersion)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Reboot recommended",
                    Content = "ClawTweaks was updated. Please reboot to make sure everything is running cleanly.",
                    PrimaryButtonText = "Reboot now",
                    CloseButtonText = "Later",
                    DefaultButton = ContentDialogButton.Close
                };

                Logger.Info($"[VersionMismatch] Showing reboot dialog: {helperVersion} → {widgetVersion}");
                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    Logger.Info($"[VersionMismatch] User chose reboot now — IsConnected={App.IsConnected}");
                    // The pipe may be momentarily disconnected (e.g., failed-restart path disposes it
                    // right after scheduling this dialog). Retry for up to 5 s before giving up.
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        if (App.IsConnected)
                        {
                            SendPowerActionAsync("reboot");
                            return;
                        }
                        Logger.Info($"[VersionMismatch] Pipe not connected, retrying in 1 s (attempt {attempt + 1}/5)");
                        await Task.Delay(1000);
                    }
                    Logger.Warn("[VersionMismatch] Pipe still not connected after 5 s — cannot send reboot command");
                }
                else
                {
                    Logger.Info("[VersionMismatch] User deferred reboot — auto-restart continues");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[VersionMismatch] Reboot dialog failed: {ex.Message}");
            }
        }

        /// <summary>Returns true when versionA is strictly newer than versionB (Major.Minor.Build.Revision).</summary>
        private static bool IsVersionNewer(string versionA, string versionB)
        {
            if (Version.TryParse(versionA, out var a) && Version.TryParse(versionB, out var b))
                return a > b;
            return false;
        }

    }
}
