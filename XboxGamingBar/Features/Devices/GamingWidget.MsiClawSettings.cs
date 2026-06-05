using NLog;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// Widget-side handlers for MSI Claw-specific hardware features:
    ///   • LED Controller Color (HID)
    ///   • Battery Charge Limit (WMI/ACPI)
    ///
    /// Both cards are hidden by default and revealed only when the helper
    /// confirms an MSI Claw device (deviceDisplayName contains "Claw").
    ///
    /// Settings are persisted in LocalSettings and pushed to the helper
    /// via named pipe on change and on widget startup.
    /// </summary>
    public sealed partial class GamingWidget
    {
        // ── LocalSettings keys ──────────────────────────────────────────────────────
        private const string MsiLedColorKey         = "MsiClaw_LedColor";       // "R,G,B"
        private const string MsiChargeLimitEnabledKey = "MsiClaw_ChargeLimitOn";  // bool
        private const string MsiChargeLimitPercentKey = "MsiClaw_ChargeLimitPct"; // int 60/80/100

        // ── State ───────────────────────────────────────────────────────────────────
        private bool   _msiLedExpanded       = false;
        private bool   _msiLedLoading        = false;
        private bool   _msiChargeLimitLoading = false;

        // Debounce timer so dragging the color wheel doesn't flood the helper
        private Windows.UI.Xaml.DispatcherTimer _msiLedDebounceTimer;
        private Color  _pendingLedColor;

        // ── Init ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called from the main widget initialization (after device name is known).
        /// Shows MSI Claw cards and restores saved settings.
        /// </summary>
        internal void InitializeMsiClawSettings()
        {
            if (!IsMsiClawDevice()) return;

            // Show the two MSI-Claw-only cards
            if (MsiLedColorCard    != null) MsiLedColorCard.Visibility    = Visibility.Visible;
            if (MsiChargeLimitCard != null) MsiChargeLimitCard.Visibility = Visibility.Visible;

            RestoreMsiLedColorFromSettings();
            RestoreMsiChargeLimitFromSettings();

            Logger.Debug("[MsiClawSettings] Cards visible, settings restored");
        }

        // ── LED Color ────────────────────────────────────────────────────────────────

        private void RestoreMsiLedColorFromSettings()
        {
            try
            {
                _msiLedLoading = true;
                var s = ApplicationData.Current.LocalSettings.Values;
                if (s.TryGetValue(MsiLedColorKey, out var colorObj) && colorObj is string colorStr)
                {
                    var parts = colorStr.Split(',');
                    if (parts.Length == 3
                        && byte.TryParse(parts[0], out byte r)
                        && byte.TryParse(parts[1], out byte g)
                        && byte.TryParse(parts[2], out byte b))
                    {
                        if (MsiLedColorPicker != null)
                            MsiLedColorPicker.Color = Color.FromArgb(255, r, g, b);
                    }
                }
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] Restore color failed: {ex.Message}"); }
            finally { _msiLedLoading = false; }
        }

        internal void MsiLedExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _msiLedExpanded = !_msiLedExpanded;
            if (MsiLedContent    != null) MsiLedContent.Visibility    = _msiLedExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (MsiLedExpandIcon != null) MsiLedExpandIcon.Glyph      = _msiLedExpanded ? "" : "";
        }

        internal void MsiLedColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender,
                                                      Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (_msiLedLoading) return;
            _pendingLedColor = args.NewColor;

            // Debounce: apply 600 ms after the user stops dragging
            if (_msiLedDebounceTimer == null)
            {
                _msiLedDebounceTimer = new Windows.UI.Xaml.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(600)
                };
                _msiLedDebounceTimer.Tick += (s, ev) =>
                {
                    _msiLedDebounceTimer.Stop();
                    ApplyAndSaveLedColor(_pendingLedColor);
                };
            }
            _msiLedDebounceTimer.Stop();
            _msiLedDebounceTimer.Start();
        }

        internal void MsiLedApplyButton_Click(object sender, RoutedEventArgs e)
        {
            _msiLedDebounceTimer?.Stop();
            if (MsiLedColorPicker != null)
                ApplyAndSaveLedColor(MsiLedColorPicker.Color);
        }

        private void ApplyAndSaveLedColor(Color c)
        {
            // Persist
            ApplicationData.Current.LocalSettings.Values[MsiLedColorKey]
                = $"{c.R},{c.G},{c.B}";

            // Send to helper
            _ = SendMsiLedColorAsync(c.R, c.G, c.B);
        }

        private async Task SendMsiLedColorAsync(byte r, byte g, byte b)
        {
            try
            {
                if (!App.IsConnected) return;
                var msg = new Windows.Foundation.Collections.ValueSet
                {
                    { "MsiLedColor", $"{r},{g},{b}" }
                };
                await App.SendMessageAsync(msg);
                Logger.Info($"[MsiLed] Sent color R={r} G={g} B={b}");
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] Send failed: {ex.Message}"); }
        }

        // ── Charge Limit ─────────────────────────────────────────────────────────────

        private void RestoreMsiChargeLimitFromSettings()
        {
            try
            {
                _msiChargeLimitLoading = true;
                var s = ApplicationData.Current.LocalSettings.Values;

                bool enabled = s.TryGetValue(MsiChargeLimitEnabledKey, out var en) && en is bool b && b;
                int  pct     = s.TryGetValue(MsiChargeLimitPercentKey,  out var pc) && pc is int  i ? i : 80;

                if (MsiChargeLimitToggle != null)
                    MsiChargeLimitToggle.IsOn = enabled;
                if (MsiChargeLimitPercentPanel != null)
                    MsiChargeLimitPercentPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

                SetChargeLimitRadio(pct);
            }
            catch (Exception ex) { Logger.Warn($"[BattMgr] Restore charge limit failed: {ex.Message}"); }
            finally { _msiChargeLimitLoading = false; }
        }

        private void SetChargeLimitRadio(int pct)
        {
            _msiChargeLimitLoading = true;
            try
            {
                if (MsiChargeLimit60  != null) MsiChargeLimit60.IsChecked  = (pct == 60);
                if (MsiChargeLimit80  != null) MsiChargeLimit80.IsChecked  = (pct == 80);
                if (MsiChargeLimit100 != null) MsiChargeLimit100.IsChecked = (pct == 100);
            }
            finally { _msiChargeLimitLoading = false; }
        }

        internal void MsiChargeLimitToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiChargeLimitLoading) return;
            bool on = MsiChargeLimitToggle?.IsOn ?? false;

            ApplicationData.Current.LocalSettings.Values[MsiChargeLimitEnabledKey] = on;
            if (MsiChargeLimitPercentPanel != null)
                MsiChargeLimitPercentPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

            int pct = GetSelectedChargePercent();
            _ = SendMsiChargeLimitAsync(on, pct);
            Logger.Info($"[BattMgr] Toggle → enabled={on} pct={pct}");
        }

        internal void MsiChargeLimitPercent_Checked(object sender, RoutedEventArgs e)
        {
            if (_msiChargeLimitLoading) return;
            int pct = GetSelectedChargePercent();
            ApplicationData.Current.LocalSettings.Values[MsiChargeLimitPercentKey] = pct;
            bool on = MsiChargeLimitToggle?.IsOn ?? false;
            _ = SendMsiChargeLimitAsync(on, pct);
            Logger.Info($"[BattMgr] Percent → {pct}%");
        }

        private int GetSelectedChargePercent()
        {
            if (MsiChargeLimit60?.IsChecked  == true) return 60;
            if (MsiChargeLimit100?.IsChecked == true) return 100;
            return 80;
        }

        private async Task SendMsiChargeLimitAsync(bool enabled, int percent)
        {
            try
            {
                if (!App.IsConnected) return;
                var msg = new Windows.Foundation.Collections.ValueSet
                {
                    { "MsiChargeLimit", $"{enabled}:{percent}" }
                };
                await App.SendMessageAsync(msg);
                Logger.Info($"[BattMgr] Sent limit enabled={enabled} percent={percent}");
            }
            catch (Exception ex) { Logger.Warn($"[BattMgr] Send failed: {ex.Message}"); }
        }
    }
}
