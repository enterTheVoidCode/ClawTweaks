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
        private const string MsiChargeLimitPercentKey = "MsiClaw_ChargeLimitPct"; // int 20..100
        // Set true the first time the user enables the charge limiter in the System tab. The
        // Quick Settings ChargeLimiter tile only works after this initial setup.
        private const string MsiChargeLimitInitKey    = "MsiClaw_ChargeLimitInit"; // bool
        private const int    MsiChargeLimitTileDefault = 90;  // tile default % when the user has no stored value

        // ── State ───────────────────────────────────────────────────────────────────
        private bool   _msiLedExpanded       = false;
        private bool   _msiLedLoading        = false;
        // Start TRUE so the Slider's XAML-default ValueChanged (Value="80") and the ToggleSwitch's
        // default Toggled — both raised during page construction, BEFORE RestoreMsiChargeLimitFromSettings
        // runs — are ignored. Otherwise that spurious 80 clobbered the stored percent and got pushed to
        // the helper on every startup, resetting a user's 95% back to 80%. Restore clears it when done.
        private bool   _msiChargeLimitLoading = true;

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

            // Show the MSI-Claw-only cards
            if (MsiLedColorCard    != null) MsiLedColorCard.Visibility    = Visibility.Visible;
            if (MsiChargeLimitCard != null) MsiChargeLimitCard.Visibility = Visibility.Visible;
            // Vibration & Deadzone card (Controller tab). Gated on the device name like the LED /
            // charge-limit cards — NOT on controllerEmulationAvailable, which can arrive after the
            // gyro callback (its only other visibility owner) and used to leave the card hidden.
            if (ControllerFeedbackCard != null) ControllerFeedbackCard.Visibility = Visibility.Visible;

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

        // Vibration & Deadzone card expander (matches Button Remapping; collapsed by default).
        private bool _controllerFeedbackExpanded;
        internal void ControllerFeedbackExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            _controllerFeedbackExpanded = !_controllerFeedbackExpanded;
            if (ControllerFeedbackContent != null)
                ControllerFeedbackContent.Visibility = _controllerFeedbackExpanded ? Visibility.Visible : Visibility.Collapsed;
            // E70D = ChevronDown (collapsed), E70E = ChevronUp (expanded)
            if (ControllerFeedbackExpandIcon != null)
                ControllerFeedbackExpandIcon.Glyph = _controllerFeedbackExpanded ? "" : "";
        }

        // Fires a short test rumble pulse at the current intensity (no game needed).
        // Send a UNIQUE value each press — the trigger property dedupes equal values, so a constant
        // "test" would only fire once. The helper ignores the content (it just checks for a Set).
        private int _testVibrationSeq;
        private void TestVibrationButton_Click(object sender, RoutedEventArgs e)
        {
            testControllerVibration?.Trigger("test" + (++_testVibrationSeq));
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

        // Debounce so dragging the slider doesn't flood the helper with WMI writes.
        private Windows.UI.Xaml.DispatcherTimer _msiChargeDebounceTimer;
        private int _pendingChargePercent = MsiChargeLimitTileDefault;

        private void RestoreMsiChargeLimitFromSettings()
        {
            try
            {
                _msiChargeLimitLoading = true;
                var s = ApplicationData.Current.LocalSettings.Values;

                bool enabled = s.TryGetValue(MsiChargeLimitEnabledKey, out var en) && en is bool b && b;
                int  pct     = s.TryGetValue(MsiChargeLimitPercentKey,  out var pc) && pc is int  i ? i : MsiChargeLimitTileDefault;
                pct = Math.Max(20, Math.Min(100, pct));

                if (MsiChargeLimitToggle != null)
                    MsiChargeLimitToggle.IsOn = enabled;
                if (MsiChargeLimitPercentPanel != null)
                    MsiChargeLimitPercentPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                if (MsiChargeLimitSlider != null)
                    MsiChargeLimitSlider.Value = pct;
                if (MsiChargeLimitValue != null)
                    MsiChargeLimitValue.Text = $"{pct}%";
            }
            catch (Exception ex) { Logger.Warn($"[BattMgr] Restore charge limit failed: {ex.Message}"); }
            finally { _msiChargeLimitLoading = false; }

            // Read the live EC value so the status line reflects reality on open.
            _ = QueryMsiChargeLimitStatusAsync();
        }

        internal void MsiChargeLimitToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiChargeLimitLoading) return;
            bool on = MsiChargeLimitToggle?.IsOn ?? false;

            ApplicationData.Current.LocalSettings.Values[MsiChargeLimitEnabledKey] = on;
            // First-time enable in Settings unlocks the Quick Settings tile.
            if (on) ApplicationData.Current.LocalSettings.Values[MsiChargeLimitInitKey] = true;
            if (MsiChargeLimitPercentPanel != null)
                MsiChargeLimitPercentPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

            int pct = (int)(MsiChargeLimitSlider?.Value ?? MsiChargeLimitTileDefault);
            // Persist the percent alongside the enabled flag so the stored value is always
            // complete (the slider may never have been moved), and the helper can be re-synced.
            ApplicationData.Current.LocalSettings.Values[MsiChargeLimitPercentKey] = pct;
            _ = SendMsiChargeLimitAsync(on, pct);
            Logger.Info($"[BattMgr] Toggle → enabled={on} pct={pct}");
        }

        internal void MsiChargeLimitSlider_ValueChanged(object sender,
            Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            int pct = (int)Math.Round(e.NewValue);
            if (MsiChargeLimitValue != null) MsiChargeLimitValue.Text = $"{pct}%";
            if (_msiChargeLimitLoading) return;

            ApplicationData.Current.LocalSettings.Values[MsiChargeLimitPercentKey] = pct;
            _pendingChargePercent = pct;

            // Debounce the helper write (500 ms after the last change).
            if (_msiChargeDebounceTimer == null)
            {
                _msiChargeDebounceTimer = new Windows.UI.Xaml.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _msiChargeDebounceTimer.Tick += (s, ev) =>
                {
                    _msiChargeDebounceTimer.Stop();
                    bool on = MsiChargeLimitToggle?.IsOn ?? false;
                    _ = SendMsiChargeLimitAsync(on, _pendingChargePercent);
                    Logger.Info($"[BattMgr] Percent (debounced) → {_pendingChargePercent}%");
                };
            }
            _msiChargeDebounceTimer.Stop();
            _msiChargeDebounceTimer.Start();
        }

        /// <summary>Re-reads the live EC charge-limit value (for resume-from-sleep verification).</summary>
        internal void MsiChargeLimitVerifyButton_Click(object sender, RoutedEventArgs e)
        {
            _ = QueryMsiChargeLimitStatusAsync();
        }

        // ── Charge Limiter Quick Settings tile ───────────────────────────────────────
        /// <summary>True once the user has enabled the charge limiter in the System tab at least once.</summary>
        internal bool IsChargeLimiterInitialized()
        {
            var s = ApplicationData.Current.LocalSettings.Values;
            return s.TryGetValue(MsiChargeLimitInitKey, out var v) && v is bool b && b;
        }

        internal bool IsChargeLimiterEnabled()
        {
            var s = ApplicationData.Current.LocalSettings.Values;
            return s.TryGetValue(MsiChargeLimitEnabledKey, out var v) && v is bool b && b;
        }

        internal int ChargeLimiterPercent()
        {
            var s = ApplicationData.Current.LocalSettings.Values;
            return s.TryGetValue(MsiChargeLimitPercentKey, out var v) && v is int i ? i : MsiChargeLimitTileDefault;
        }

        /// <summary>
        /// Quick Settings tile: simple on/off. Only works once the limiter has been set up in the
        /// System tab (IsChargeLimiterInitialized). Toggling on restores the user's stored percent
        /// (or 90% if none). Keeps the System-tab UI in sync.
        /// </summary>
        private void ToggleChargeLimiterTile()
        {
            if (!IsChargeLimiterInitialized())
            {
                // Not set up yet — tell the user to enable it once in Settings.
                _ = SendActionNotificationAsync("Charge Limit\nEnable it once in the System tab first");
                Logger.Info("[BattMgr] ChargeLimiter tile tapped but not initialized — prompting Settings setup");
                return;
            }

            bool newOn = !IsChargeLimiterEnabled();
            int pct = ChargeLimiterPercent();
            if (pct < 20 || pct > 100) pct = MsiChargeLimitTileDefault;

            ApplicationData.Current.LocalSettings.Values[MsiChargeLimitEnabledKey] = newOn;
            ApplicationData.Current.LocalSettings.Values[MsiChargeLimitPercentKey] = pct;

            // Keep the System-tab controls in sync (without re-triggering their handlers).
            _msiChargeLimitLoading = true;
            try
            {
                if (MsiChargeLimitToggle != null) MsiChargeLimitToggle.IsOn = newOn;
                if (MsiChargeLimitPercentPanel != null)
                    MsiChargeLimitPercentPanel.Visibility = newOn ? Visibility.Visible : Visibility.Collapsed;
                if (MsiChargeLimitSlider != null) MsiChargeLimitSlider.Value = pct;
                if (MsiChargeLimitValue != null)  MsiChargeLimitValue.Text = $"{pct}%";
            }
            finally { _msiChargeLimitLoading = false; }

            _ = SendMsiChargeLimitAsync(newOn, pct);
            Logger.Info($"[BattMgr] ChargeLimiter tile → {(newOn ? "On" : "Off")} {pct}%");
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

            // Confirm what actually landed in the EC.
            await QueryMsiChargeLimitStatusAsync();
        }

        /// <summary>
        /// Re-pushes the widget's stored (authoritative) charge-limit to the helper. Called on
        /// pipe (re)connect. Fixes the case where a debounced slider write never reached the
        /// helper (app/helper killed within the debounce window, or a lost helper-side write):
        /// the helper would otherwise keep a stale/older value (e.g. the 80% it got from the
        /// initial toggle-on) and re-apply it on every reboot. The widget LocalSettings value is
        /// written synchronously on every change, so it is the reliable source of truth.
        /// </summary>
        internal void ResendChargeLimitToHelper()
        {
            try
            {
                if (!IsMsiClawDevice()) return;
                if (!IsChargeLimiterInitialized()) return;   // never set up → nothing to enforce
                if (!IsChargeLimiterEnabled()) return;        // disabled → don't fight the helper
                int pct = ChargeLimiterPercent();
                if (pct < 20 || pct > 100) pct = MsiChargeLimitTileDefault;
                _ = SendMsiChargeLimitAsync(true, pct);
                Logger.Info($"[BattMgr] Re-pushed stored charge limit on connect: On {pct}%");
            }
            catch (Exception ex) { Logger.Warn($"[BattMgr] ResendChargeLimitToHelper: {ex.Message}"); }
        }

        /// <summary>
        /// Asks the helper for the live EC charge-limit value and shows it in the status line.
        /// Response format: "enabled:percent:readok".
        /// </summary>
        private async Task QueryMsiChargeLimitStatusAsync()
        {
            try
            {
                if (!App.IsConnected)
                {
                    SetChargeStatus("Helper not connected — cannot read live value.", warn: true);
                    return;
                }

                var msg = new Windows.Foundation.Collections.ValueSet { { "MsiChargeLimitGet", true } };
                var resp = await App.SendMessageAsync(msg);
                if (resp == null || !resp.TryGetValue("MsiChargeLimitStatus", out var stObj) || !(stObj is string st))
                {
                    SetChargeStatus("Could not read the live value.", warn: true);
                    return;
                }

                var parts = st.Split(':');
                bool enabled = parts.Length > 0 && bool.TryParse(parts[0], out var en) && en;
                int  pct     = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 0;
                bool readOk  = parts.Length < 3 || (bool.TryParse(parts[2], out var ro) && ro);

                if (!readOk)
                    SetChargeStatus("Live value: unknown (device not ready / powered off).", warn: true);
                else if (enabled)
                    SetChargeStatus($"Live value: active, limit {pct}% ✓ verified.", warn: false);
                else
                    SetChargeStatus($"Live value: disabled (charges to 100%). Stored limit {pct}%.", warn: false);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BattMgr] Query status failed: {ex.Message}");
                SetChargeStatus("Could not read the live value.", warn: true);
            }
        }

        private void SetChargeStatus(string text, bool warn)
        {
            if (MsiChargeLimitStatusText == null) return;
            MsiChargeLimitStatusText.Text = text;
            MsiChargeLimitStatusText.Foreground = new SolidColorBrush(
                warn ? Color.FromArgb(255, 0xFF, 0x8C, 0x00)   // orange
                     : Color.FromArgb(255, 0x4C, 0xAF, 0x50)); // green
        }
    }
}
