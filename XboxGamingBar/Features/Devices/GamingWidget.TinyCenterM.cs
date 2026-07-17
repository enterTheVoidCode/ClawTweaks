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

                // parts[9] = MSI stick swap, parts[10] = firmware gyro (EEPROM 0x0029 bit0).
                SetGyroState(parts.Length > 10 && bool.TryParse(parts[10], out bool gyroOn) && gyroOn);

                _tcmPending.Clear();
                UpdateTcmApplyButton();
                if (TcmStatusText != null) TcmStatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception)
            {
                _tcmLoading = false;
            }
        }

        private static void SetSlider(Slider slider, TextBlock value, string raw)
        {
            if (slider == null) return;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            {
                slider.Value = v;
                if (value != null) value.Text = v + " %";
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

        // ── Hardware gyro (EEPROM 0x0029 bit0) — read-out + one-way off switch ──────────
        /// <summary>Reflects the firmware gyro bit. Disabling is only offered while it is actually on.</summary>
        private void SetGyroState(bool active)
        {
            if (TcmGyroStateText != null)
            {
                TcmGyroStateText.Text = active ? "Active" : "Off";
                TcmGyroStateText.Foreground = new SolidColorBrush(active
                    ? Color.FromArgb(0xFF, 0xFF, 0xC1, 0x07)    // amber — it can fight our own gyro
                    : Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));  // green — nothing to do
            }
            if (TcmGyroDisableButton != null) TcmGyroDisableButton.IsEnabled = active;
        }

        /// <summary>Read failed (controller busy / not present) — never guess, and never offer the write.</summary>
        private void SetGyroUnknown()
        {
            if (TcmGyroStateText != null)
            {
                TcmGyroStateText.Text = "unknown";
                TcmGyroStateText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
            }
            if (TcmGyroDisableButton != null) TcmGyroDisableButton.IsEnabled = false;
        }

        /// <summary>
        /// Clears the firmware gyro enable bit via the helper (read-modify-write + SyncToROM, and a
        /// profile.rec mirror so Center M agrees). Deliberately one-way: re-enabling stays Center M's job.
        /// </summary>
        private async void TcmGyroDisable_Click(object sender, RoutedEventArgs e)
        {
            if (TcmGyroDisableButton != null) TcmGyroDisableButton.IsEnabled = false;
            try
            {
                await App.SendMessageAsync(new ValueSet { { "TinyCenterMReset", "gyrooff" } }, 10000);
                await Task.Delay(400);          // let the controller settle after SyncToROM
                await LoadTinyCenterMAsync();   // re-read so the state text reflects the real bit
            }
            catch (Exception)
            {
                SetGyroUnknown();
            }
        }
    }
}
