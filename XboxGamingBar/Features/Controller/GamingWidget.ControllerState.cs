using System;
using Shared.Enums;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// Controller-State card (bottom of the Controller tab, MSI Claw only).
    ///
    /// Read-only diagnostic: the widget asks the elevated helper, which inspects ViGEm,
    /// HidHide and MSI Claw PnP live (see Program.ControllerState.cs, mirroring
    /// Diagnostics\Get-ControllerState.ps1) and returns a compact status string. The widget
    /// just renders it. Nothing here changes emulation or hide state.
    /// </summary>
    public sealed partial class GamingWidget
    {
        /// <summary>
        /// Request a fresh controller state. Called when the Controller (Legion) tab becomes active.
        /// The card itself is always visible in the Controller tab (set in XAML).
        /// </summary>
        private void RefreshControllerStateCard()
        {
            RequestControllerState();
        }

        /// <summary>Ask the helper for the live controller state.</summary>
        private async void RequestControllerState()
        {
            if (!App.IsConnected)
                return;

            try
            {
                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("Command", (int)Command.Get);
                request.Add("Function", (int)Function.RequestControllerState);
                var response = await App.SendMessageAsync(request);

                if (response != null && response.TryGetValue("Content", out object contentObj) && contentObj is string content)
                {
                    OnControllerStateReceived(content);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"RequestControllerState failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse "state|vigem|pid1901|pid1902|blocked|xinput" and update the card UI.
        /// </summary>
        private void OnControllerStateReceived(string content)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (ControllerStateText == null)
                    return;

                int state = 0, vigem = 0, pid1901 = 0, pid1902 = 0, blocked = -1, xinput = 0;
                try
                {
                    var parts = (content ?? "").Split('|');
                    if (parts.Length >= 6)
                    {
                        int.TryParse(parts[0], out state);
                        int.TryParse(parts[1], out vigem);
                        int.TryParse(parts[2], out pid1901);
                        int.TryParse(parts[3], out pid1902);
                        int.TryParse(parts[4], out blocked);
                        int.TryParse(parts[5], out xinput);
                    }
                }
                catch { /* fall through to "unknown" defaults */ }

                Color color;
                string title;
                string caption;
                switch (state)
                {
                    case 1: // Virtual controller mode
                        color = Color.FromArgb(255, 0, 200, 83);   // green
                        title = "Virtual controller mode";
                        caption = "Virtual ViGEm controller active · physical MSI controllers hidden via HidHide.";
                        break;
                    case 2: // Hardware controller mode
                        color = Color.FromArgb(255, 46, 155, 255);  // blue
                        title = "HW controller mode";
                        caption = "Controller emulation off · physical MSI controllers active (no virtual controller).";
                        break;
                    case 3: // External Gamepad Mode
                        color = Color.FromArgb(255, 186, 104, 255); // purple
                        title = "External Gamepad Mode";
                        caption = "Handheld controllers hidden · only an external gamepad is visible to games.";
                        break;
                    default: // Undetermined / transitional
                        color = Color.FromArgb(255, 136, 136, 136); // grey
                        title = "Transitional / unknown";
                        caption = "Intermediate state — please refresh again.";
                        break;
                }

                ControllerStateText.Text = title;
                if (ControllerStateCaption != null)
                    ControllerStateCaption.Text = caption;
                if (ControllerStateDot != null)
                    ControllerStateDot.Fill = new SolidColorBrush(color);

                if (ControllerStateViGEmValue != null)
                    ControllerStateViGEmValue.Text = vigem > 0 ? $"{vigem} active" : "none";
                if (ControllerStatePid1901Value != null)
                    ControllerStatePid1901Value.Text = pid1901 > 0 ? "present" : "not present";
                if (ControllerStatePid1902Value != null)
                    ControllerStatePid1902Value.Text = pid1902 > 0 ? "present" : "not present";
                if (ControllerStateBlockedValue != null)
                    ControllerStateBlockedValue.Text = blocked < 0 ? "unknown" : blocked.ToString();
                if (ControllerStateXInputValue != null)
                    ControllerStateXInputValue.Text = xinput.ToString();
            });
        }

        private void ControllerStateRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RequestControllerState();
        }

        private void ControllerStateDetailsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ControllerStateDetailsPanel == null)
                return;

            bool expanded = ControllerStateDetailsToggle?.IsChecked == true;
            ControllerStateDetailsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (ControllerStateDetailsIcon != null)
                ControllerStateDetailsIcon.Glyph = expanded ? "" : ""; // up / down chevron
        }
    }
}
