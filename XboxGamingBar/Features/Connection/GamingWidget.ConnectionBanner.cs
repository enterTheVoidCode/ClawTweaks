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

            switch (state)
            {
                case BannerState.Disconnected:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 51, 51)); // #CC3333
                    ConnectionStatusText.Text = "Not connected to helper";
                    break;
                case BannerState.Syncing:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Syncing...";
                    break;
                case BannerState.Reconnecting:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 102, 51)); // #CC6633
                    ConnectionStatusText.Text = "Reconnecting...";
                    break;
                case BannerState.Launching:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Launching helper...";
                    break;
                case BannerState.Loading:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Loading...";
                    break;
                case BannerState.InitialSetup:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 51, 153)); // #663399 Purple
                    ConnectionStatusText.Text = "Initial setup in progress - please wait...";
                    break;
                case BannerState.Upgrading:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 153, 102)); // #339966 Green
                    ConnectionStatusText.Text = "Upgrading helper...";
                    break;
            }
            ConnectionStatusBanner.Visibility = Visibility.Visible;

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

    }
}
