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
        private const string MsiLedBrightnessKey    = "MsiClaw_LedBrightness";  // int 0..100 (0 = LED off)
        private const string MsiLedBootCycleKey     = "MsiClaw_LedBootCycle";   // bool (startup red→green→colour)
        private const string MsiChargeLimitEnabledKey = "MsiClaw_ChargeLimitOn";  // bool
        private const string MsiChargeLimitPercentKey = "MsiClaw_ChargeLimitPct"; // int 20..100
        // Set true the first time the user enables the charge limiter in the System tab. The
        // Quick Settings ChargeLimiter tile only works after this initial setup.
        private const string MsiChargeLimitInitKey    = "MsiClaw_ChargeLimitInit"; // bool
        private const int    MsiChargeLimitTileDefault = 90;  // tile default % when the user has no stored value

        // ── State ───────────────────────────────────────────────────────────────────
        private bool   _msiLedExpanded       = false;
        private bool   _msiLedLoading        = false;
        // Start TRUE so the ToggleSwitch's construction-time Toggled (XAML IsOn="True") is ignored
        // until RestoreMsiLedBootCycleFromSettings loads the real value (same pattern as charge limit).
        private bool   _msiLedBootCycleLoading = true;
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

            // Controller-Status card is always visible in the Controller tab (set in XAML).
            // Just fetch a fresh state on init.
            RequestControllerState();

            RestoreMsiLedEffectFromSettings();
            RestoreMsiLedBootCycleFromSettings();
            RestoreMsiChargeLimitFromSettings();
            RestoreGameBarWidgetPositionFromSettings();

            Logger.Debug("[MsiClawSettings] Cards visible, settings restored");
        }

        // ── Right MSI Button: ClawTweaks Game Bar widget position (RB auto-jump) ──────────
        // Start TRUE (same rationale as _msiChargeLimitLoading): the Slider's XAML-default
        // ValueChanged (Value="1") fires during page construction, BEFORE
        // RestoreGameBarWidgetPositionFromSettings runs. With this false, that spurious 1 was saved
        // over the user's stored value and pushed to the helper — so the position reset to 1 on every
        // reboot. Restore clears this flag in its finally once the real value is loaded.
        private bool _loadingGameBarWidgetPosition = true;

        private void RestoreGameBarWidgetPositionFromSettings()
        {
            try
            {
                _loadingGameBarWidgetPosition = true;
                int pos = 1; // default 1 = auto-jump off; also the value after a factory reset (LocalSettings cleared → key missing)
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(GameBarWidgetPositionKey, out var v) && v is int stored)
                    pos = stored;
                if (pos < 1) pos = 1;
                if (pos > 10) pos = 10;

                if (ClawTweaksWidgetPositionSlider != null) ClawTweaksWidgetPositionSlider.Value = pos;
                if (ClawTweaksWidgetPositionValue != null) ClawTweaksWidgetPositionValue.Text = pos.ToString();

                // Push to helper so the RB hop count is correct even before the user touches it.
                gameBarWidgetPosition?.SetValue(pos);
                Logger.Info($"[GameBarAutoNav] restored ClawTweaks widget position = {pos}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[GameBarAutoNav] restore position failed: {ex.Message}");
            }
            finally { _loadingGameBarWidgetPosition = false; }
        }

        private void ClawTweaksWidgetPosition_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loadingGameBarWidgetPosition) return;
            int pos = (int)System.Math.Round(e.NewValue);
            if (ClawTweaksWidgetPositionValue != null) ClawTweaksWidgetPositionValue.Text = pos.ToString();
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[GameBarWidgetPositionKey] = pos;
            }
            catch (Exception ex) { Logger.Warn($"[GameBarAutoNav] persist position failed: {ex.Message}"); }
            gameBarWidgetPosition?.SetValue(pos);
            Logger.Info($"[GameBarAutoNav] ClawTweaks widget position set to {pos}");
        }

        // ── LED Color ────────────────────────────────────────────────────────────────

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

        // Mouse Settings card expander (Mouse Mode options split out of the old Virtual Controller
        // card; collapsed by default, gated + hidden in-game like the other controller sections).
        private bool _mouseSettingsExpanded;
        internal void MouseSettingsExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            _mouseSettingsExpanded = !_mouseSettingsExpanded;
            if (MouseSettingsContent != null)
                MouseSettingsContent.Visibility = _mouseSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            // E70E = ChevronUp (expanded), E70D = ChevronDown (collapsed)
            if (MouseSettingsExpandIcon != null)
                MouseSettingsExpandIcon.Glyph = _mouseSettingsExpanded ? "" : "";
        }

        // Fires a short test rumble pulse at the current intensity (no game needed).
        // Send a UNIQUE value each press — the trigger property dedupes equal values, so a constant
        // "test" would only fire once. The helper ignores the content (it just checks for a Set).
        private int _testVibrationSeq;
        // Monotonic counter so a repeated "Xbox Button" tap always changes the trigger value
        // (WidgetProperty dedupes equal values). Mirrors _testVibrationSeq.
        private int _emulateXboxGuideSeq;
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

        // Static/Breathing colour picker (in the swatch flyout) → the editor's colour for the current zone.
        internal void MsiLedColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender,
                                                      Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (_msiLedLoading) return;
            OnEditorColorChanged(args.NewColor);
        }

        // Legacy LED-by-battery toggle — SUPERSEDED by the "Battery" LED mode (per zone). Kept Collapsed
        // so this handler compiles; neutralized.
        private void LedColorBySocToggle_Toggled(object sender, RoutedEventArgs e) { }

        /// <summary>Global LED brightness slider (applies to all zones; 0 = off). Debounced.</summary>
        private void MsiLedBrightnessSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_msiLedLoading) return;
            OnGlobalBrightnessChanged((int)Math.Round(e.NewValue));
        }

        // ── MSI Claw LED on/off (drives the LED quick-settings tile) ─────────────────────
        internal int GetMsiLedBrightness()
        {
            EnsureCompositeLoaded();   // read the saved config, not a stale default (tile can run pre-Restore)
            return _ledComposite != null ? _ledComposite.Brightness : 100;
        }

        internal bool IsMsiLedOn() => GetMsiLedBrightness() > 0;

        /// <summary>Turns the MSI Claw LED on (brightness 100) or off (0), keeping the current effect.</summary>
        internal void ApplyMsiLedOnOff(bool on)
        {
            EnsureCompositeLoaded();   // operate on the persisted composite, never a pre-Restore default
            _ledComposite.Brightness = on ? 100 : 0;
            if (MsiLedBrightnessSlider != null)
            {
                _msiLedLoading = true;
                try { MsiLedBrightnessSlider.Value = _ledComposite.Brightness; } finally { _msiLedLoading = false; }
            }
            SendCompositeNow();
            Logger.Info($"[MsiLed] LED tile → {(on ? "ON (100%)" : "OFF (0%)")}");
        }

        /// <summary>
        /// Re-pushes the composite + startup-cycle preference to the helper on pipe (re)connect (widget is
        /// authoritative). Kept under the original name so the pipe-connect caller stays unchanged.
        /// </summary>
        internal void ResendMsiLedColorToHelper()
        {
            try
            {
                if (!IsMsiClawDevice()) return;
                var s = ApplicationData.Current.LocalSettings.Values;
                bool cycleOn = !(s.TryGetValue(MsiLedBootCycleKey, out var cv) && cv is bool cb) || cb;
                _ = SendMsiLedBootCycleAsync(cycleOn);
                ResendMsiLedEffectToHelper();
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] ResendMsiLedColorToHelper: {ex.Message}"); }
        }

        private async Task SendMsiLedColorAsync(byte r, byte g, byte b, int brightness = 100)
        {
            try
            {
                if (!App.IsConnected) return;
                int bright = Math.Max(0, Math.Min(100, brightness));
                var msg = new Windows.Foundation.Collections.ValueSet
                {
                    { "MsiLedColor", $"{r},{g},{b},{bright}" }
                };
                await App.SendMessageAsync(msg);
                Logger.Info($"[MsiLed] Sent color R={r} G={g} B={b} Brightness={bright}");
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] Send failed: {ex.Message}"); }
        }

        // ── Startup colour cycle (red→green→saved colour) on/off ──────────────────────
        private void RestoreMsiLedBootCycleFromSettings()
        {
            try
            {
                _msiLedBootCycleLoading = true;
                bool on = true; // default: cycle enabled
                var s = ApplicationData.Current.LocalSettings.Values;
                if (s.TryGetValue(MsiLedBootCycleKey, out var v) && v is bool b) on = b;
                if (MsiLedBootCycleToggle != null) MsiLedBootCycleToggle.IsOn = on;
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] Restore boot cycle failed: {ex.Message}"); }
            finally { _msiLedBootCycleLoading = false; }
        }

        internal void MsiLedBootCycleToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiLedBootCycleLoading) return;
            bool on = MsiLedBootCycleToggle?.IsOn ?? true;
            try { ApplicationData.Current.LocalSettings.Values[MsiLedBootCycleKey] = on; }
            catch (Exception ex) { Logger.Warn($"[MsiLed] persist boot cycle failed: {ex.Message}"); }
            _ = SendMsiLedBootCycleAsync(on);
            Logger.Info($"[MsiLed] Startup colour cycle → {(on ? "on" : "off")}");
        }

        private async Task SendMsiLedBootCycleAsync(bool on)
        {
            try
            {
                if (!App.IsConnected) return;
                var msg = new Windows.Foundation.Collections.ValueSet
                {
                    { "MsiLedBootCycle", on ? "1" : "0" }
                };
                await App.SendMessageAsync(msg);
                Logger.Info($"[MsiLed] Sent boot cycle = {on}");
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] Send boot cycle failed: {ex.Message}"); }
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

        // ── Collapsible cards (System tab): Appearance/Theme + Charge Limit ──────────
        // Same custom pattern as the Tab Settings card above (ToggleButton + chevron glyph
        // flip E70D↔E70E + manual Visibility). Both start collapsed.
        private bool _themeExpanded;
        internal void ThemeExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _themeExpanded = !_themeExpanded;
            if (ThemeContent != null)
                ThemeContent.Visibility = _themeExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (ThemeExpandIcon != null)
                ThemeExpandIcon.Glyph = _themeExpanded ? "" : "";
        }

        private bool _chargeLimitExpanded;
        internal void ChargeLimitExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _chargeLimitExpanded = !_chargeLimitExpanded;
            if (MsiChargeLimitContent != null)
                MsiChargeLimitContent.Visibility = _chargeLimitExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (ChargeLimitExpandIcon != null)
                ChargeLimitExpandIcon.Glyph = _chargeLimitExpanded ? "" : "";
        }

        // Saved Profiles card (relocated to the Performance tab) — collapsed by default.
        private bool _savedProfilesExpanded;
        internal void PerfSavedProfilesExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _savedProfilesExpanded = !_savedProfilesExpanded;
            if (PerfSavedProfilesContent != null)
                PerfSavedProfilesContent.Visibility = _savedProfilesExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (PerfSavedProfilesExpandIcon != null)
                PerfSavedProfilesExpandIcon.Glyph = _savedProfilesExpanded ? "" : "";
        }

        // CPU card (Performance tab) — collapsed by default.
        private bool _cpuCardExpanded;
        internal void CpuCardExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _cpuCardExpanded = !_cpuCardExpanded;
            if (CpuSectionContent != null)
                CpuSectionContent.Visibility = _cpuCardExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (CpuCardExpandIcon != null)
                CpuCardExpandIcon.Glyph = _cpuCardExpanded ? "" : "";
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
