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

        // Reset buttons STAGE the MSI factory values into the sliders (committed via Apply).
        private void TcmResetSticks_Click(object sender, RoutedEventArgs e)
        {
            if (TcmLSDZSlider != null)  TcmLSDZSlider.Value  = 5;
            if (TcmLSEDZSlider != null) TcmLSEDZSlider.Value = 100;
            if (TcmRSDZSlider != null)  TcmRSDZSlider.Value  = 5;
            if (TcmRSEDZSlider != null) TcmRSEDZSlider.Value = 100;
        }

        private void TcmResetTriggers_Click(object sender, RoutedEventArgs e)
        {
            if (TcmLTDZSlider != null)  TcmLTDZSlider.Value  = 0;
            if (TcmLTEDZSlider != null) TcmLTEDZSlider.Value = 100;
            if (TcmRTDZSlider != null)  TcmRTDZSlider.Value  = 0;
            if (TcmRTEDZSlider != null) TcmRTEDZSlider.Value = 100;
        }
    }
}
