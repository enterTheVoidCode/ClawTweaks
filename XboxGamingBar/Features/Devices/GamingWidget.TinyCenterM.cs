using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// "Tiny Center M" tab — MSI Claw hardware controller config (stick + trigger deadzones/limits).
    /// Changes are STAGED: dragging a slider only marks a pending change and lights the Apply button.
    /// Pressing Apply writes every pending field to the controller firmware + MSI's profile.rec, then
    /// bounces MSI Center M's ControlMode server so its own state reloads to match (full coexistence).
    /// Helper IPC: TinyCenterMGet (read), TinyCenterMApply "FIELD:VALUE;..." (commit + Center M sync).
    /// </summary>
    public sealed partial class GamingWidget
    {
        private bool _tcmLoading;                                     // suppress slider events while loading
        private readonly Dictionary<string, int> _tcmPending = new Dictionary<string, int>();

        /// <summary>Show the tab's nav item only on the MSI Claw. Called from the device-name hook.</summary>
        private void InitializeTinyCenterMTab()
        {
            if (TinyCenterMNavItem != null)
                TinyCenterMNavItem.Visibility = IsMsiClawDevice() ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadTinyCenterMAsync()
        {
            try
            {
                var resp = await App.SendMessageAsync(new ValueSet { { "TinyCenterMGet", true } }, 8000);
                string status = resp != null && resp.TryGetValue("TinyCenterMStatus", out var s) ? s as string : null;
                var parts = status?.Split(':');
                if (parts == null || parts.Length < 11 || !bool.TryParse(parts[0], out bool valid) || !valid)
                {
                    if (TcmStatusText != null)
                    {
                        TcmStatusText.Text = "Couldn't read the controller right now — try again in a moment.";
                        TcmStatusText.Visibility = Visibility.Visible;
                    }
                    SetGyroUnknown();
                    // The banner is all the user gets, and it says nothing about WHY. The cause is on
                    // the helper side (ReadFwSlotRaw could not open the mi_01 command interface), so
                    // the two logs have to be correlatable by time - hence a line here too.
                    Logger.Warn($"LoadTinyCenterMAsync: controller read not usable (status='{status ?? "null"}')");
                    return;
                }

                _tcmLoading = true;
                SetSlider(TcmLSDZSlider,  TcmLSDZValue,  parts[1]);
                SetSlider(TcmLSEDZSlider, TcmLSEDZValue, parts[2]);
                SetSlider(TcmRSDZSlider,  TcmRSDZValue,  parts[3]);
                SetSlider(TcmRSEDZSlider, TcmRSEDZValue, parts[4]);
                SetSlider(TcmLTDZSlider,  TcmLTDZValue,  parts[5]);
                SetSlider(TcmLTEDZSlider, TcmLTEDZValue, parts[6]);
                SetSlider(TcmRTDZSlider,  TcmRTDZValue,  parts[7]);
                SetSlider(TcmRTEDZSlider, TcmRTEDZValue, parts[8]);
                _tcmLoading = false;

                // parts[9] = MSI stick swap, parts[10] = firmware gyro enable (EEPROM 0x0029 bit0).
                // parts[11..15] = gyro detail: outputMouse, always, button, sensPct, dzPct.
                PopulateGyro(parts.Length > 10 && bool.TryParse(parts[10], out bool gyroOn) && gyroOn, parts);

                _tcmPending.Clear();
                UpdateTcmApplyButton();
                if (TcmStatusText != null) TcmStatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                // Was a bare swallow. A load that dies here leaves the tab showing stale sliders with
                // no banner and no trace at all, which is indistinguishable from a successful read.
                _tcmLoading = false;
                Logger.Warn($"LoadTinyCenterMAsync failed: {ex.Message}");
            }
        }

        private static void SetSlider(Slider slider, TextBlock value, string raw)
        {
            if (slider == null) return;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            {
                // Read back through the Slider: a value outside Minimum..Maximum (e.g. a trigger limit
                // of 0 written before the 1 % floor existed) gets coerced, and the label has to follow
                // it — otherwise the text says 0 % while the thumb sits at 1 %.
                slider.Value = v;
                if (value != null) value.Text = (int)slider.Value + " %";
            }
        }

        // Slider drag → STAGE only (no firmware write until Apply).
        private void TcmSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_tcmLoading) return;
            if (!(sender is Slider slider) || !(slider.Tag is string field)) return;
            int v = (int)Math.Round(e.NewValue);

            var label = FindTcmValueLabel(field);
            if (label != null) label.Text = v + " %";

            _tcmPending[field] = v;
            UpdateTcmApplyButton();
        }

        private void UpdateTcmApplyButton()
        {
            if (TcmApplyButton == null) return;
            bool dirty = _tcmPending.Count > 0;
            TcmApplyButton.IsEnabled = dirty;
            if (TcmApplyText != null)
                TcmApplyText.Text = dirty ? "Apply & refresh MSI Center M" : "No unsaved changes";
            // Glow when there are unsaved changes.
            TcmApplyButton.Background = dirty
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xB0, 0x4A))   // green highlight
                : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));  // subtle idle
            TcmApplyButton.Foreground = new SolidColorBrush(dirty ? Colors.White : Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));

            // A disabled Button can't take focus: with no pending changes, Reset's XYFocusUp would
            // dead-end on the greyed-out Apply and the D-pad couldn't leave the section upwards.
            // Route it straight to the expand button whenever Apply isn't focusable.
            if (TcmResetButton != null && TcmSticksExpandButton != null)
                TcmResetButton.XYFocusUp = dirty ? (DependencyObject)TcmApplyButton : TcmSticksExpandButton;
        }

        private async void TcmApply_Click(object sender, RoutedEventArgs e)
        {
            if (_tcmPending.Count == 0) return;
            string payload = string.Join(";", _tcmPending.Select(kv => $"{kv.Key}:{kv.Value}"));

            if (TcmApplyText != null) TcmApplyText.Text = "Applying…";
            if (TcmApplyButton != null) TcmApplyButton.IsEnabled = false;
            try
            {
                // The commit writes FW + profile.rec and bounces Center M's ControlMode — allow time.
                await App.SendMessageAsync(new ValueSet { { "TinyCenterMApply", payload } }, 15000);
                _tcmPending.Clear();
                await Task.Delay(600);          // let ControlMode respawn + the controller settle
                await LoadTinyCenterMAsync();   // reflect the committed values
            }
            catch (Exception) { }
            finally { UpdateTcmApplyButton(); }
        }

        private TextBlock FindTcmValueLabel(string field)
        {
            switch (field)
            {
                case "LSDZ":  return TcmLSDZValue;
                case "LSEDZ": return TcmLSEDZValue;
                case "RSDZ":  return TcmRSDZValue;
                case "RSEDZ": return TcmRSEDZValue;
                case "LTDZ":  return TcmLTDZValue;
                case "LTEDZ": return TcmLTEDZValue;
                case "RTDZ":  return TcmRTDZValue;
                case "RTEDZ": return TcmRTEDZValue;
                default: return null;
            }
        }

        /// <summary>
        /// Stages the MSI factory values for sticks AND triggers into the sliders (sticks: inner 5 /
        /// outer 100, triggers: deadzone 0 / limit 100). Like any other edit this only stages — the
        /// firmware write still goes through Apply.
        /// </summary>
        private void TcmResetAll_Click(object sender, RoutedEventArgs e)
        {
            if (TcmLSDZSlider != null)  TcmLSDZSlider.Value  = 5;
            if (TcmLSEDZSlider != null) TcmLSEDZSlider.Value = 100;
            if (TcmRSDZSlider != null)  TcmRSDZSlider.Value  = 5;
            if (TcmRSEDZSlider != null) TcmRSEDZSlider.Value = 100;
            if (TcmLTDZSlider != null)  TcmLTDZSlider.Value  = 0;
            if (TcmLTEDZSlider != null) TcmLTEDZSlider.Value = 100;
            if (TcmRTDZSlider != null)  TcmRTDZSlider.Value  = 0;
            if (TcmRTEDZSlider != null) TcmRTEDZSlider.Value = 100;
        }

        // ── Collapsible sections (same pattern as the LED / charge-limit cards) ──────────
        private bool _tcmSticksExpanded;
        private bool _tcmGyroExpanded;

        internal void TcmSticksExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _tcmSticksExpanded = !_tcmSticksExpanded;
            if (TcmSticksContent != null)
                TcmSticksContent.Visibility = _tcmSticksExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (TcmSticksExpandIcon != null)
                TcmSticksExpandIcon.Glyph = _tcmSticksExpanded ? "\uE70E" : "\uE70D";
        }

        internal void TcmGyroExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _tcmGyroExpanded = !_tcmGyroExpanded;
            if (TcmGyroContent != null)
                TcmGyroContent.Visibility = _tcmGyroExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (TcmGyroExpandIcon != null)
                TcmGyroExpandIcon.Glyph = _tcmGyroExpanded ? "\uE70E" : "\uE70D";
        }

        // ── Hardware gyro (MTP block 0x0029–0x0032) — full read-out + staged write ──────────
        private bool _gyroLoading;    // suppress control events while populating from a read
        private bool _gyroPending;    // a gyro control changed since the last load/apply

        /// <summary>Populate the whole gyro section from a read. parts[10]=enable, [11]=mouse, [12]=always,
        /// [13]=button, [14]=sens%, [15]=dz%. Missing fields fall back to sensible defaults.</summary>
        private void PopulateGyro(bool active, string[] parts)
        {
            _gyroLoading = true;

            if (TcmGyroStateText != null)
            {
                TcmGyroStateText.Text = active ? "Active" : "Off";
                TcmGyroStateText.Foreground = new SolidColorBrush(active
                    ? Color.FromArgb(0xFF, 0xFF, 0xC1, 0x07)    // amber — it can fight our own gyro
                    : Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));  // green
            }

            bool mouse  = parts.Length > 11 && bool.TryParse(parts[11], out bool m) && m;
            string activation = parts.Length > 12 ? (parts[12] ?? "").Trim().ToLowerInvariant() : "always";
            string button = parts.Length > 13 ? parts[13] : "";
            int sens = parts.Length > 14 && int.TryParse(parts[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sv) ? sv : 20;
            int dz   = parts.Length > 15 && int.TryParse(parts[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dv) ? dv : 10;

            if (TcmGyroEnableToggle   != null) TcmGyroEnableToggle.IsOn = active;
            if (TcmGyroOutputCombo    != null) TcmGyroOutputCombo.SelectedIndex = mouse ? 1 : 0;
            if (TcmGyroActivationCombo != null) TcmGyroActivationCombo.SelectedIndex = activation == "hold" ? 1 : activation == "toggle" ? 2 : 0;
            if (TcmGyroButtonCombo    != null) TcmGyroButtonCombo.SelectedIndex = GyroButtonIndex(button);
            if (TcmGyroSensSlider != null) { TcmGyroSensSlider.Value = sens; if (TcmGyroSensValue != null) TcmGyroSensValue.Text = sens + " %"; }
            if (TcmGyroDzSlider   != null) { TcmGyroDzSlider.Value   = dz;   if (TcmGyroDzValue   != null) TcmGyroDzValue.Text   = dz   + " %"; }

            _gyroLoading = false;
            _gyroPending = false;
            UpdateGyroInteractivity();
            UpdateGyroApplyButton();
        }

        /// <summary>Read failed (controller busy / not present) — never guess a state.</summary>
        private void SetGyroUnknown()
        {
            if (TcmGyroStateText != null)
            {
                TcmGyroStateText.Text = "unknown";
                TcmGyroStateText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
            }
        }

        private static int GyroButtonIndex(string name)
        {
            switch ((name ?? "").Trim().ToUpperInvariant())
            {
                case "R2": return 1;
                case "M1": return 2;
                case "M2": return 3;
                default:   return 0;   // L2
            }
        }

        private static string GyroComboTag(ComboBox combo)
        {
            return (combo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        }

        // Enable gates the config controls; activation=button additionally gates the button picker.
        // (A StackPanel isn't a Control, so we disable the individual inputs rather than the panel.)
        private void UpdateGyroInteractivity()
        {
            bool on = TcmGyroEnableToggle != null && TcmGyroEnableToggle.IsOn;
            if (TcmGyroOutputCombo     != null) TcmGyroOutputCombo.IsEnabled     = on;
            if (TcmGyroActivationCombo != null) TcmGyroActivationCombo.IsEnabled = on;
            if (TcmGyroSensSlider != null) TcmGyroSensSlider.IsEnabled = on;
            if (TcmGyroDzSlider   != null) TcmGyroDzSlider.IsEnabled   = on;
            string act = GyroComboTag(TcmGyroActivationCombo);
            bool buttonMode = on && (act == "hold" || act == "toggle");
            if (TcmGyroButtonCombo != null) TcmGyroButtonCombo.IsEnabled = buttonMode;
            if (TcmGyroConfigPanel != null) TcmGyroConfigPanel.Opacity = on ? 1.0 : 0.55;

            // D-pad can't land on a disabled control: when the button picker is off (always-on or gyro
            // disabled), route the vertical chain around it so down/up navigation doesn't dead-end on it.
            if (TcmGyroActivationCombo != null)
                TcmGyroActivationCombo.XYFocusDown = buttonMode ? (DependencyObject)TcmGyroButtonCombo : TcmGyroSensSlider;
            if (TcmGyroSensSlider != null)
                TcmGyroSensSlider.XYFocusUp = buttonMode ? (DependencyObject)TcmGyroButtonCombo : TcmGyroActivationCombo;
        }

        private void TcmGyro_Changed(object sender, RoutedEventArgs e)
        {
            if (_gyroLoading) return;
            _gyroPending = true;
            UpdateGyroInteractivity();
            UpdateGyroApplyButton();
        }

        private void TcmGyroCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_gyroLoading) return;
            _gyroPending = true;
            UpdateGyroInteractivity();
            UpdateGyroApplyButton();
        }

        private void TcmGyroSlider_Changed(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!(sender is Slider s)) return;
            int v = (int)Math.Round(e.NewValue);
            if (s == TcmGyroSensSlider && TcmGyroSensValue != null) TcmGyroSensValue.Text = v + " %";
            else if (s == TcmGyroDzSlider && TcmGyroDzValue != null) TcmGyroDzValue.Text = v + " %";
            if (_gyroLoading) return;
            _gyroPending = true;
            UpdateGyroApplyButton();
        }

        private void UpdateGyroApplyButton()
        {
            if (TcmGyroApplyButton == null) return;
            TcmGyroApplyButton.IsEnabled = _gyroPending;
            if (TcmGyroApplyText != null)
                TcmGyroApplyText.Text = _gyroPending ? "Apply gyro & refresh MSI Center M" : "No unsaved changes";
            TcmGyroApplyButton.Background = _gyroPending
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xB0, 0x4A))
                : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            TcmGyroApplyButton.Foreground = new SolidColorBrush(_gyroPending ? Colors.White : Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
        }

        /// <summary>
        /// Commits the staged gyro state via the helper: enable + output (mouse/right-stick) + activation
        /// (always vs hold-button) + sensitivity/deadzone. Writes EEPROM + profile.rec and bounces Center M.
        /// </summary>
        private async void TcmGyroApply_Click(object sender, RoutedEventArgs e)
        {
            if (!_gyroPending) return;
            bool enable = TcmGyroEnableToggle != null && TcmGyroEnableToggle.IsOn;
            bool mouse  = GyroComboTag(TcmGyroOutputCombo) == "mouse";
            string activation = GyroComboTag(TcmGyroActivationCombo);   // always | hold | toggle
            if (string.IsNullOrEmpty(activation)) activation = "always";
            string button = GyroComboTag(TcmGyroButtonCombo);
            int sens = TcmGyroSensSlider != null ? (int)Math.Round(TcmGyroSensSlider.Value) : 20;
            int dz   = TcmGyroDzSlider   != null ? (int)Math.Round(TcmGyroDzSlider.Value)   : 10;
            string payload = $"{(enable ? 1 : 0)}:{(mouse ? 1 : 0)}:{activation}:{button}:{sens}:{dz}";

            if (TcmGyroApplyText != null) TcmGyroApplyText.Text = "Applying…";
            if (TcmGyroApplyButton != null) TcmGyroApplyButton.IsEnabled = false;
            try
            {
                await App.SendMessageAsync(new ValueSet { { "TinyCenterMGyro", payload } }, 15000);
                _gyroPending = false;
                await Task.Delay(600);          // let ControlMode respawn + the controller settle
                await LoadTinyCenterMAsync();   // reflect the committed values
            }
            catch (Exception) { }
            finally { UpdateGyroApplyButton(); }
        }
    }
}
