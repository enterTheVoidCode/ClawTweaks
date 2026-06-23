using System;
using System.Text;
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
        /// Parse "state|vigem|pid1901|pid1902|blocked|xinput|viiper|viiperType|pid1901Hid" and update the card UI.
        /// </summary>
        private void OnControllerStateReceived(string content)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (ControllerStateText == null)
                    return;

                int state = 0, vigem = 0, pid1901 = 0, pid1902 = 0, blocked = -1, xinput = 0, viiper = 0, pid1901Hid = -1, joyCplCount = -1;
                string viiperType = "";
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
                    if (parts.Length >= 7) int.TryParse(parts[6], out viiper);
                    if (parts.Length >= 8) viiperType = parts[7] ?? "";
                    if (parts.Length >= 9) int.TryParse(parts[8], out pid1901Hid);
                    if (parts.Length >= 10) int.TryParse(parts[9], out joyCplCount);
                }
                catch { /* fall through to "unknown" defaults */ }

                string viiperName = ViiperDeviceTypeDisplayName(viiperType);

                Color color;
                string title;
                string caption;
                switch (state)
                {
                    case 1: // Virtual controller mode
                        color = Color.FromArgb(255, 0, 200, 83);   // green
                        title = "Virtual controller mode";
                        caption = viiper > 0
                            ? $"Virtual VIIPER controller active ({viiperName}) · physical MSI controllers hidden via HidHide."
                            : "Virtual ViGEm controller active · physical MSI controllers hidden via HidHide.";
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
                if (ControllerStateViiperValue != null)
                    ControllerStateViiperValue.Text = viiper > 0
                        ? (string.IsNullOrEmpty(viiperName) ? $"{viiper} active" : $"{viiperName} (active)")
                        : "none";
                // "Game controllers" counter = xinput (occupied XInput slots) — the same number
                // joy.cpl shows regardless of mode. pid1901Hid tracks physical MSI nodes for the
                // console list and health-check coloring in HW mode.
                if (ControllerStatePid1901Value != null)
                {
                    ControllerStatePid1901Value.Text = xinput.ToString();

                    // Color: in HW mode (2) we expect 3 MSI XInput nodes; in virtual mode (1)
                    // we expect at least 1 virtual controller; otherwise white if anything present.
                    bool countOk = state == 2 ? xinput >= 3
                                 : state == 1 ? xinput >= 1
                                 : xinput > 0;
                    ControllerStatePid1901Value.Foreground = new SolidColorBrush(
                        countOk
                            ? Windows.UI.Colors.White
                            : xinput > 0
                                ? Windows.UI.Color.FromArgb(255, 255, 180, 0)   // orange — partial
                                : Windows.UI.Color.FromArgb(255, 255, 80, 80)); // red — none
                }

                if (ControllerListConsole != null)
                {
                    var sb = new StringBuilder();
                    int slot = 0;

                    // MSI Claw XInput gamepad nodes (each = one joy.cpl "Xbox 360 Controller" entry)
                    int igCount = pid1901Hid >= 0 ? pid1901Hid : (pid1901 > 0 ? 1 : 0);
                    for (int i = 0; i < igCount; i++)
                        sb.AppendLine($"[{slot++}]  Xbox 360 Controller for Windows  (MSI Claw)");

                    // ViGEm virtual controllers
                    for (int i = 0; i < vigem; i++)
                        sb.AppendLine($"[{slot++}]  Xbox 360 Controller  (Virtual / ViGEm)");

                    // VIIPER virtual controllers
                    if (viiper > 0)
                    {
                        string vname = string.IsNullOrEmpty(viiperName) ? "VIIPER" : viiperName;
                        for (int i = 0; i < viiper; i++)
                            sb.AppendLine($"[{slot++}]  {vname}  (Virtual / VIIPER)");
                    }

                    // PID_1902 DInput (if visible / not hidden)
                    if (pid1902 > 0)
                        sb.AppendLine($"[{slot++}]  MSI Claw DInput Controller  (PID_1902)");

                    ControllerListConsole.Text = sb.Length > 0
                        ? sb.ToString().TrimEnd()
                        : "(no controllers detected)";
                }
                if (ControllerStatePid1902Value != null)
                    ControllerStatePid1902Value.Text = pid1902 > 0 ? "present" : "not present";
                if (ControllerStateBlockedValue != null)
                    ControllerStateBlockedValue.Text = blocked < 0 ? "unknown" : blocked.ToString();
                if (ControllerStateXInputValue != null)
                    ControllerStateXInputValue.Text = xinput.ToString();
            });
        }

        /// <summary>Maps a libviiper device-type tag to the friendly name shown in the status card.</summary>
        private static string ViiperDeviceTypeDisplayName(string tag)
        {
            switch (tag)
            {
                case "xbox360":          return "Xbox 360";
                case "dualshock4":       return "DualShock 4";
                case "dualsenseedge":    return "DualSense Edge";
                case "xboxelite2":       return "Xbox Elite 2";
                case "steam-generic":
                case "steam-controller": return "Steam Controller";
                case "steamdeck-generic": return "Steam Deck";
                case "switchpro":        return "Switch Pro";
                default:                 return tag ?? "";
            }
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
