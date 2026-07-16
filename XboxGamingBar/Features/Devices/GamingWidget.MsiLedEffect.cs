using System;
using System.Threading.Tasks;
using Shared.Enums;
using Shared.Led;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// Widget-side logic for the per-zone MSI Claw LED system. Three zones (right stick / left stick /
    /// buttons) each get their own effect (Static / Breathing / Color Cycle / Wave / Battery), plus a
    /// global speed + brightness and a "sync all zones" toggle. To keep the UI compact one editor drives
    /// the currently-selected zone (or all zones when synced); the full composite is persisted and pushed
    /// to the helper, which renders it via its frame compositor.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private const string MsiLedCompositeKey = "MsiClaw_LedComposite";   // serialized LedCompositeSpec

        // Authoritative model.
        private LedCompositeSpec _ledComposite = new LedCompositeSpec();
        private LedZoneId _editingZone = LedZoneId.Right;
        // True once _ledComposite reflects LocalSettings (via Restore or EnsureCompositeLoaded). Guards
        // against sending/persisting the pristine default before the saved config has been loaded — the
        // LED on/off tile can run before the LED settings page (and its Restore) is ever opened.
        private bool _ledCompositeLoaded;

        /// <summary>
        /// Loads the persisted composite into _ledComposite on first use, WITHOUT needing the LED settings
        /// UI. Mirrors the old system's "always read LocalSettings directly" behaviour so the on/off tile
        /// and connect-resend never operate on (or persist) a stale default.
        /// </summary>
        private void EnsureCompositeLoaded()
        {
            if (_ledCompositeLoaded) return;
            // NOTE: _ledCompositeLoaded is set true only AFTER a successful load. If the load throws
            // (e.g. LocalSettings hits a separated-RCW on a broken post-hibernate resume), it stays
            // false so the send/persist paths are skipped (no clobber) and the load retries.
            try
            {
                var s = ApplicationData.Current.LocalSettings.Values;
                LedCompositeSpec fromLs = null;
                if (s.TryGetValue(MsiLedCompositeKey, out var cObj) && cObj is string cs && LedCompositeSpec.TryParse(cs, out var c))
                    fromLs = c;

                // A real, user-configured composite in LocalSettings wins (legacy white counts as unconfigured).
                if (fromLs != null && !fromLs.IsPristineOrLegacyDefault) { _ledComposite = fromLs; _ledCompositeLoaded = true; return; }

                // Else recover the user's config from the helper's persisted file (LocalSettings was reset /
                // still on the legacy white default), and adopt it so the widget is authoritative again.
                if (TryLoadCompositeFromHelperFile(out var fromFile) && !fromFile.IsPristineOrLegacyDefault)
                {
                    _ledComposite = fromFile;
                    ApplicationData.Current.LocalSettings.Values[MsiLedCompositeKey] = fromFile.Serialize();
                    Logger.Info("[MsiLed] adopted composite from helper file");
                    _ledCompositeLoaded = true;
                    return;
                }

                // No real config anywhere (fresh / pristine / legacy-white) → (re)assert the factory
                // default so a legacy-white device migrates to it. Assign it explicitly instead of
                // trusting whatever is already in _ledComposite: a construction-time XAML event (e.g.
                // MsiLedSyncToggle's IsOn="True" Toggled) can have written the editor's field defaults —
                // static WHITE — in here first, and on a fresh install there is no saved value to
                // overwrite them, so that white would get persisted and pushed as a real user choice.
                _ledComposite = new LedCompositeSpec();
                _ledCompositeLoaded = true;
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] EnsureCompositeLoaded: {ex.Message}"); }
        }

        /// <summary>Gate for every persist/send: never write before the saved composite is confirmed
        /// loaded, otherwise the constructed default would clobber the user's config (the resume-clobber
        /// bug — a broken resume can fail the load, leaving _ledComposite at its default).</summary>
        private bool CompositeReadyToSend()
        {
            EnsureCompositeLoaded();
            if (!_ledCompositeLoaded)
            {
                Logger.Info("[MsiLed] send/persist skipped — composite not loaded yet (avoid clobber)");
                return false;
            }
            return true;
        }

        // Reads the helper's msi_led_composite.txt from LocalState (same package folder) as a fallback source.
        private bool TryLoadCompositeFromHelperFile(out LedCompositeSpec spec)
        {
            spec = null;
            try
            {
                string path = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "msi_led_composite.txt");
                if (!System.IO.File.Exists(path)) return false;
                return LedCompositeSpec.TryParse(System.IO.File.ReadAllText(path), out spec);
            }
            catch { return false; }
        }

        // Editor working copy (mirrors the visible controls for the currently-edited zone).
        private LedMainMode _msiLedMode      = LedMainMode.Static;
        private bool        _msiLedClockwise = true;
        private bool        _msiLedRainbow;
        private Color       _msiLedColor      = Color.FromArgb(255, 255, 255, 255);
        private Color[]     _msiLedWaveColors = { Color.FromArgb(255, 255, 0, 0), Color.FromArgb(255, 255, 255, 0), Color.FromArgb(255, 0, 255, 0), Color.FromArgb(255, 0, 0, 255) };
        private readonly System.Collections.Generic.HashSet<object> _wiredPickers = new System.Collections.Generic.HashSet<object>();

        // ── Restore ─────────────────────────────────────────────────────────────────────
        private void RestoreMsiLedEffectFromSettings()
        {
            try
            {
                _msiLedLoading = true;
                EnsureCompositeLoaded();   // loads _ledComposite from LocalSettings (or helper-file fallback)

                if (MsiLedSyncToggle != null) MsiLedSyncToggle.IsOn = _ledComposite.Sync;
                if (MsiLedZoneRow != null) MsiLedZoneRow.Visibility = _ledComposite.Sync ? Visibility.Collapsed : Visibility.Visible;
                if (MsiLedZoneCombo != null) MsiLedZoneCombo.SelectedIndex = (int)_editingZone;
                if (MsiLedSpeedCombo != null) MsiLedSpeedCombo.SelectedIndex = Math.Max(0, Math.Min(2, _ledComposite.SpeedIdx));
                if (MsiLedBrightnessSlider != null) MsiLedBrightnessSlider.Value = _ledComposite.Brightness;

                LoadZoneIntoEditor(EditingZoneSpec());
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] Restore composite failed: {ex.Message}"); }
            finally { _msiLedLoading = false; }
        }

        // The zone the editor currently represents (Right is the shared master when synced).
        private LedZoneSpec EditingZoneSpec()
            => _ledComposite.Sync ? _ledComposite.Right : _ledComposite.Zone(_editingZone);

        // ── Global handlers (Sync / Zone / Speed / Brightness) ───────────────────────────
        internal void MsiLedSyncToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiLedLoading || MsiLedSyncToggle == null) return;
            _ledComposite.Sync = MsiLedSyncToggle.IsOn;
            if (MsiLedZoneRow != null) MsiLedZoneRow.Visibility = _ledComposite.Sync ? Visibility.Collapsed : Visibility.Visible;
            if (_ledComposite.Sync)
            {
                // Copy the current editor into all three zones.
                WriteEditorToComposite();
            }
            SendCompositeNow();
        }

        internal void MsiLedZoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_msiLedLoading || MsiLedZoneCombo == null) return;
            int idx = MsiLedZoneCombo.SelectedIndex;
            if (idx < 0) return;
            _editingZone = (LedZoneId)idx;
            _msiLedLoading = true;
            try { LoadZoneIntoEditor(_ledComposite.Zone(_editingZone)); }
            finally { _msiLedLoading = false; }
            // No send — just switches which zone the editor shows.
        }

        internal void MsiLedSpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_msiLedLoading || MsiLedSpeedCombo == null) return;
            _ledComposite.SpeedIdx = Math.Max(0, MsiLedSpeedCombo.SelectedIndex);
            SendCompositeNow();
        }

        // ── Per-zone editor handlers ─────────────────────────────────────────────────────
        internal void MsiLedModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_msiLedLoading || MsiLedModeCombo == null) return;
            int idx = MsiLedModeCombo.SelectedIndex;
            if (idx < 0) return;
            _msiLedMode = (LedMainMode)idx;
            UpdateLedModeVisibility();
            ApplyEditorNow();
        }

        internal void MsiLedDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_msiLedLoading || MsiLedDirectionCombo == null) return;
            _msiLedClockwise = MsiLedDirectionCombo.SelectedIndex == 0;
            ApplyEditorNow();
        }

        internal void MsiLedRainbowToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiLedLoading || MsiLedRainbowToggle == null) return;
            _msiLedRainbow = MsiLedRainbowToggle.IsOn;
            UpdateLedModeVisibility();
            ApplyEditorNow();
        }

        internal void MsiLedWavePicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender,
                                                    Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (_msiLedLoading) return;
            if (!(sender?.Tag is string tag) || !int.TryParse(tag, out int idx) || idx < 0 || idx > 3) return;
            _msiLedWaveColors[idx] = args.NewColor;
            SetSwatch(WaveSwatch(idx), args.NewColor);
            ApplyEditorDebounced();
        }

        // Called from MsiLedColorPicker_ColorChanged (MsiClawSettings.cs).
        private void OnEditorColorChanged(Color c)
        {
            _msiLedColor = c;
            SetSwatch(MsiLedColorSwatch, c);
            ApplyEditorDebounced();
        }

        // Called from MsiLedBrightnessSlider_ValueChanged (MsiClawSettings.cs).
        private void OnGlobalBrightnessChanged(int brightness)
        {
            int b = Math.Max(0, Math.Min(100, brightness));
            _ledComposite.Brightness = b;
            if (b > 0) SetLedOnBrightness(b);   // remember the user's on-level for the on/off tile
            SendCompositeDebounced();
        }

        /// <summary>The brightness the LED tile restores when turning the LED back on — so toggling
        /// off/off doesn't snap back to 100 %. Persisted; defaults to 100 % when never set.</summary>
        private int GetLedOnBrightness()
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(MsiLedOnBrightnessKey, out var v)
                    && v is int b && b > 0)
                    return Math.Max(1, Math.Min(100, b));
            }
            catch { }
            return 100;
        }

        private void SetLedOnBrightness(int brightness)
        {
            if (brightness <= 0) return;
            try { ApplicationData.Current.LocalSettings.Values[MsiLedOnBrightnessKey] = Math.Max(1, Math.Min(100, brightness)); }
            catch { }
        }

        // ── Editor <-> composite ─────────────────────────────────────────────────────────
        private void LoadZoneIntoEditor(LedZoneSpec z)
        {
            _msiLedMode = z.Mode;
            _msiLedRainbow = z.Rainbow;
            _msiLedClockwise = z.Clockwise;
            _msiLedColor = ToColor(z.Color);
            for (int i = 0; i < 4; i++) _msiLedWaveColors[i] = ToColor(z.WaveColors[i]);

            if (MsiLedModeCombo != null) MsiLedModeCombo.SelectedIndex = (int)z.Mode;
            if (MsiLedRainbowToggle != null) MsiLedRainbowToggle.IsOn = z.Rainbow;
            if (MsiLedDirectionCombo != null) MsiLedDirectionCombo.SelectedIndex = z.Clockwise ? 0 : 1;
            if (MsiLedColorPicker != null) MsiLedColorPicker.Color = _msiLedColor;
            SetSwatch(MsiLedColorSwatch, _msiLedColor);
            ApplyWaveColorsToUI();
            UpdateLedModeVisibility();
        }

        private LedZoneSpec BuildEditorZone() => new LedZoneSpec
        {
            Mode = _msiLedMode,
            Rainbow = _msiLedRainbow,
            Clockwise = _msiLedClockwise,
            Color = ToRgb(_msiLedColor),
            WaveColors = new[] { ToRgb(_msiLedWaveColors[0]), ToRgb(_msiLedWaveColors[1]), ToRgb(_msiLedWaveColors[2]), ToRgb(_msiLedWaveColors[3]) },
        };

        private void WriteEditorToComposite()
        {
            var z = BuildEditorZone();
            if (_ledComposite.Sync)
            {
                _ledComposite.Right = z.Clone();
                _ledComposite.Left = z.Clone();
                _ledComposite.Buttons = z.Clone();
            }
            else
            {
                switch (_editingZone)
                {
                    case LedZoneId.Right:   _ledComposite.Right = z; break;
                    case LedZoneId.Left:    _ledComposite.Left = z; break;
                    case LedZoneId.Buttons: _ledComposite.Buttons = z; break;
                }
            }
        }

        private void ApplyEditorNow() { WriteEditorToComposite(); SendCompositeNow(); }
        private void ApplyEditorDebounced() { WriteEditorToComposite(); SendCompositeDebounced(); }

        // ── Visibility per mode ───────────────────────────────────────────────────────
        private void UpdateLedModeVisibility()
        {
            var m = _msiLedMode;
            bool staticMode = m == LedMainMode.Static;
            bool breathing  = m == LedMainMode.Breathing;
            bool cycle      = m == LedMainMode.ColorCycle;
            bool wave       = m == LedMainMode.Wave;
            bool battery    = m == LedMainMode.Battery;

            Vis(MsiLedColorRow,      (staticMode || breathing) && !_msiLedRainbow);
            Vis(MsiLedRainbowRow,    staticMode || breathing || wave);
            Vis(MsiLedWaveSwatchRow, wave && !_msiLedRainbow);
            Vis(MsiLedDirectionRow,  wave);
            // Speed + Brightness are GLOBAL — always visible.
            Vis(MsiLedSpeedRow,        true);
            Vis(MsiLedBrightnessLabel, true);
            Vis(MsiLedBrightnessSlider, true);
            Vis(MsiLedCycleNote,   cycle);
            Vis(MsiLedBatteryNote, battery);

            if (MsiLedRainbowCaption != null)
                MsiLedRainbowCaption.Text =
                    wave      ? "On = a rotating rainbow. Off = your four colours rotate." :
                    breathing ? "On = a breathing rainbow. Off = your colour." :
                                "On = a static rainbow. Off = your colour.";
        }

        private static void Vis(UIElement el, bool show)
        {
            if (el != null) el.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Persist + send ───────────────────────────────────────────────────────────────
        private void SendCompositeNow()
        {
            if (!CompositeReadyToSend()) return;
            _msiLedDebounceTimer?.Stop();
            PersistComposite();
            _ = SendMsiLedCompositeAsync(_ledComposite.Serialize());
            UpdateQuickSettingsTileStates();
        }

        private void SendCompositeDebounced()
        {
            if (!CompositeReadyToSend()) return;
            PersistComposite();
            if (_msiLedDebounceTimer == null)
            {
                _msiLedDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _msiLedDebounceTimer.Tick += (s, ev) =>
                {
                    _msiLedDebounceTimer.Stop();
                    _ = SendMsiLedCompositeAsync(_ledComposite.Serialize());
                    UpdateQuickSettingsTileStates();
                };
            }
            _msiLedDebounceTimer.Stop();
            _msiLedDebounceTimer.Start();
        }

        private void PersistComposite()
        {
            try { ApplicationData.Current.LocalSettings.Values[MsiLedCompositeKey] = _ledComposite.Serialize(); }
            catch (Exception ex) { Logger.Warn($"[MsiLed] persist composite failed: {ex.Message}"); }
        }

        private async Task SendMsiLedCompositeAsync(string spec)
        {
            try
            {
                if (!App.IsConnected) return;
                var msg = new Windows.Foundation.Collections.ValueSet { { "MsiLedComposite", spec } };
                await App.SendMessageAsync(msg);
                Logger.Info($"[MsiLed] Sent composite '{spec}'");
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] Send composite failed: {ex.Message}"); }
        }

        /// <summary>
        /// Re-pushes the persisted composite on pipe (re)connect (widget is authoritative). Reads the
        /// composite straight from LocalSettings — the pipe can connect BEFORE the settings restore runs,
        /// so relying on the in-memory _ledComposite here would send a default (all-white) composite and
        /// clobber the correct boot LED. No-op on a fresh install (nothing persisted → leave MSI's LED).
        /// </summary>
        internal void ResendMsiLedEffectToHelper()
        {
            try
            {
                if (!IsMsiClawDevice()) return;
                EnsureCompositeLoaded();                       // load (with helper-file fallback); no clobber if already editing
                if (_ledComposite.IsPristineDefault)           // never push a default — it would clobber a good helper/boot state
                {
                    Logger.Info("[MsiLed] resend skipped — pristine default (leaving current LED state)");
                    return;
                }
                _ = SendMsiLedCompositeAsync(_ledComposite.Serialize());
                Logger.Info("[MsiLed] Re-pushed composite on connect");
            }
            catch (Exception ex) { Logger.Warn($"[MsiLed] ResendMsiLedEffectToHelper: {ex.Message}"); }
        }

        // ── Lazy picker init + coarse D-pad nav (unchanged behaviour) ────────────────────
        internal void MsiLedPicker_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Microsoft.UI.Xaml.Controls.ColorPicker p)) return;
            if (_wiredPickers.Add(p))
                p.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(MsiLedPicker_KeyDown), true);

            _msiLedLoading = true;
            try
            {
                if (ReferenceEquals(p, MsiLedColorPicker)) p.Color = _msiLedColor;
                else if (p.Tag is string tg && int.TryParse(tg, out int i) && i >= 0 && i < 4) p.Color = _msiLedWaveColors[i];
            }
            finally { _msiLedLoading = false; }
        }

        private void MsiLedPicker_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!(sender is Microsoft.UI.Xaml.Controls.ColorPicker picker)) return;
            double dh = 0, ds = 0;
            switch (e.Key)
            {
                case VirtualKey.Left:  case VirtualKey.GamepadDPadLeft:  dh = -20; break;
                case VirtualKey.Right: case VirtualKey.GamepadDPadRight: dh = +20; break;
                case VirtualKey.Up:    case VirtualKey.GamepadDPadUp:    ds = +0.12; break;
                case VirtualKey.Down:  case VirtualKey.GamepadDPadDown:  ds = -0.12; break;
                default: return;
            }
            var (h, sat, _) = RgbToHsv(picker.Color);
            h = (h + dh) % 360; if (h < 0) h += 360;
            sat = Math.Max(0, Math.Min(1, sat + ds));
            picker.Color = HsvToRgb(h, sat, 1.0);
            e.Handled = true;
        }

        // ── Swatch / colour helpers ────────────────────────────────────────────────────
        private static void SetSwatch(Border swatch, Color c) { if (swatch != null) swatch.Background = new SolidColorBrush(c); }

        private Border WaveSwatch(int i)
        {
            switch (i) { case 0: return MsiLedWaveSwatch0; case 1: return MsiLedWaveSwatch1; case 2: return MsiLedWaveSwatch2; case 3: return MsiLedWaveSwatch3; default: return null; }
        }

        private void ApplyWaveColorsToUI()
        {
            bool prev = _msiLedLoading;
            _msiLedLoading = true;
            try
            {
                if (MsiLedWavePicker0 != null) MsiLedWavePicker0.Color = _msiLedWaveColors[0];
                if (MsiLedWavePicker1 != null) MsiLedWavePicker1.Color = _msiLedWaveColors[1];
                if (MsiLedWavePicker2 != null) MsiLedWavePicker2.Color = _msiLedWaveColors[2];
                if (MsiLedWavePicker3 != null) MsiLedWavePicker3.Color = _msiLedWaveColors[3];
                SetSwatch(MsiLedWaveSwatch0, _msiLedWaveColors[0]);
                SetSwatch(MsiLedWaveSwatch1, _msiLedWaveColors[1]);
                SetSwatch(MsiLedWaveSwatch2, _msiLedWaveColors[2]);
                SetSwatch(MsiLedWaveSwatch3, _msiLedWaveColors[3]);
            }
            finally { _msiLedLoading = prev; }
        }

        private static Color ToColor(LedRgb c) => Color.FromArgb(255, c.R, c.G, c.B);
        private static LedRgb ToRgb(Color c) => new LedRgb(c.R, c.G, c.B);

        private static (double h, double s, double v) RgbToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double d = max - min, h = 0;
            if (d > 0)
            {
                if (max == r) h = 60 * ((((g - b) / d) % 6 + 6) % 6);
                else if (max == g) h = 60 * (((b - r) / d) + 2);
                else h = 60 * (((r - g) / d) + 4);
            }
            double s = max <= 0 ? 0 : d / max;
            return (h, s, max);
        }

        private static Color HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb(255,
                (byte)Math.Max(0, Math.Min(255, Math.Round((r + m) * 255))),
                (byte)Math.Max(0, Math.Min(255, Math.Round((g + m) * 255))),
                (byte)Math.Max(0, Math.Min(255, Math.Round((b + m) * 255))));
        }
    }
}
