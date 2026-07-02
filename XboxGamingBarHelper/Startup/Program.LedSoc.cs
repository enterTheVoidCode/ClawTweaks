using NLog;
using Shared.Enums;
using System;
using System.Threading;
using XboxGamingBarHelper.Devices.MSIClaw;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// LED color based on battery State of Charge (MSI Claw). When enabled, the controller LED is
    /// tinted by battery %. The LED is only written when the SoC crosses a 10% band (write-on-change)
    /// — never every tick — so there is no HID spam. Only active while the user's LED is ON
    /// (MsiLedColorStore brightness > 0); the user's stored color is left intact and re-applied when
    /// the feature is turned off.
    /// </summary>
    internal partial class Program
    {
        private const string LedColorBySocSettingKey = "LedColorBySoc";
        private const int LedSocPollIntervalMs = 5000;   // battery changes slowly; 5s is ample, write only on band change
        private static bool _ledColorBySocEnabled;
        private static int _lastSocBand = -1;            // -1 = nothing applied yet
        private static Timer _ledSocTimer;
        // False while the controller is mounting (DInput switch + HidHide). The LED HID is busy then,
        // so tinting would fight MsiLedBoot and flicker. Set true at BOOT COMPLETE. Stays true across
        // hibernate (the controller doesn't re-mount), so resume tinting is immediate.
        private static volatile bool _clawControllerReady;

        // 10% bands, low → high SoC (index = ComputeSocBand). A clearly-stepped gradient so adjacent
        // bands are visually distinct on the LED: blue → teal → green → lime → yellow → orange →
        // orange-red → red → purple. (Earlier 80-89 dark-green vs 70-79 green looked identical.)
        private static readonly (byte R, byte G, byte B)[] LedSocBandColors =
        {
            (160,   0, 210),   // band 0: < 20%   — purple (critical, lowest)
            (210,   0,   0),   // band 1: 20-29   — red
            (255,  60,   0),   // band 2: 30-39   — orange-red
            (255, 140,   0),   // band 3: 40-49   — orange
            (255, 220,   0),   // band 4: 50-59   — yellow
            (170, 220,   0),   // band 5: 60-69   — lime / yellow-green
            (  0, 210,   0),   // band 6: 70-79   — green
            (  0, 200, 160),   // band 7: 80-89   — teal / aqua-green (distinct from blue and green)
            (  0, 120, 255),   // band 8: >= 90   — blue (full)
        };

        private static int ComputeSocBand(int soc)
        {
            if (soc >= 90) return 8;
            if (soc >= 80) return 7;
            if (soc >= 70) return 6;
            if (soc >= 60) return 5;
            if (soc >= 50) return 4;
            if (soc >= 40) return 3;
            if (soc >= 30) return 2;
            if (soc >= 20) return 1;
            return 0;
        }

        /// <summary>Loads the persisted setting and starts the lightweight band-check timer. Called once at startup.</summary>
        internal static void InitLedColorBySoc()
        {
            try
            {
                _ledColorBySocEnabled = Settings.LocalSettingsHelper.TryGetValue(LedColorBySocSettingKey, out bool en) && en;
                Logger.Info($"[LedSoC] Init: enabled={_ledColorBySocEnabled}");
                _ledSocTimer = new Timer(_ => LedSocTick(), null, LedSocPollIntervalMs, LedSocPollIntervalMs);
            }
            catch (Exception ex) { Logger.Warn($"[LedSoC] Init failed: {ex.Message}"); }
        }

        /// <summary>
        /// Scales a fixed band colour by the HSV Value (brightness) the user set on the LED colour
        /// slider — i.e. the max channel of the stored user colour. So the LED brightness slider drives
        /// the battery colour's brightness just like it drives a normal colour. No stored colour → full.
        /// </summary>
        private static (byte r, byte g, byte b) ScaleByUserValue((byte R, byte G, byte B) band, byte ur, byte ug, byte ub)
        {
            int mx = Math.Max(ur, Math.Max(ug, ub));
            if (mx <= 0) return band;                 // nothing to scale from → keep full band colour
            double v = mx / 255.0;
            return ((byte)Math.Round(band.R * v), (byte)Math.Round(band.G * v), (byte)Math.Round(band.B * v));
        }

        private static void LedSocTick()
        {
            try
            {
                if (!_ledColorBySocEnabled) return;
                if (!_clawControllerReady) return;   // controller still mounting → don't fight MsiLedBoot / flicker
                if (Devices.DeviceDetector.DetectDevice().DeviceType != DeviceType.MSIClaw) return;

                // Only drive the LED if the user has it ON. MsiLedColorStore is the single source of
                // truth for "user chose a custom LED state"; brightness 0 = LED off.
                if (!MsiLedColorStore.TryLoad(out byte ur, out byte ug, out byte ub, out byte brightness)) return;
                if (brightness == 0)
                {
                    _lastSocBand = -1;   // LED off → don't touch; re-apply when it comes back on
                    return;
                }

                int soc = (int)(performanceManager?.BatteryLevel?.Value ?? -1f);
                if (soc <= 0 || soc > 100) return;   // 0/invalid = not read yet; a real 0% can't happen (device off)

                int band = ComputeSocBand(soc);
                if (band == _lastSocBand) return;    // no band change → no HID write (anti-spam)

                // Apply the band hue at the brightness the user set on the LED brightness slider — the
                // slider's value is the HSV Value of the stored colour, so scale the band colour by it.
                var (br, bg, bb) = ScaleByUserValue(LedSocBandColors[band], ur, ug, ub);
                bool ok = MsiClawLedController.TrySetLedColor(br, bg, bb, brightness);
                if (ok)
                {
                    _lastSocBand = band;
                    Logger.Info($"[LedSoC] SoC={soc}% → band {band} → LED {br},{bg},{bb} @ {brightness}%");
                }
            }
            catch (Exception ex) { Logger.Debug($"[LedSoC] tick threw: {ex.Message}"); }
        }

        /// <summary>
        /// If LED-by-SoC is enabled and the battery is readable, returns the current band color so the
        /// boot LED (MsiLedBoot) can apply it DIRECTLY instead of the user's saved colour — avoiding a
        /// saved-colour → SoC-colour flash during boot. Returns false when the feature is off or the
        /// SoC isn't available yet (caller keeps the saved colour).
        /// </summary>
        internal static bool TryGetSocBandColorForBoot(out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (!_ledColorBySocEnabled) return false;
            int soc = (int)(performanceManager?.BatteryLevel?.Value ?? -1f);
            if (soc <= 0 || soc > 100) return false;
            var c = LedSocBandColors[ComputeSocBand(soc)];
            // Match the runtime path: honour the user's LED brightness slider (stored colour's Value).
            if (MsiLedColorStore.TryLoad(out byte ur, out byte ug, out byte ub, out _))
                (r, g, b) = ScaleByUserValue(c, ur, ug, ub);
            else { r = c.R; g = c.G; b = c.B; }
            return true;
        }

        /// <summary>Pushes the persisted LED-by-SoC setting to the widget so its toggle reflects reality on connect.</summary>
        internal static void PushLedColorBySocState()
        {
            try
            {
                Program.SendPipeMessage(new Shared.IPC.PipeMessage
                {
                    Function = Function.LedColorBySoc,
                    Command = Command.Set,
                    Content = _ledColorBySocEnabled ? "true" : "false"
                });
                Logger.Info($"[LedSoC] pushed state to widget: {_ledColorBySocEnabled}");
            }
            catch (Exception ex) { Logger.Debug($"[LedSoC] push failed: {ex.Message}"); }
        }

        /// <summary>
        /// Re-asserts the SoC tint if the LED was just overwritten by an external color set (the
        /// widget re-pushing its saved MsiLedColor on connect). Called from the MsiLedColor handler.
        /// </summary>
        internal static void ReassertLedColorBySocIfActive()
        {
            if (!_ledColorBySocEnabled) return;
            _lastSocBand = -1;   // force a re-write even though the SoC band hasn't changed
            LedSocTick();
        }

        /// <summary>
        /// Resume-from-sleep/hibernate hook. The controller power-cycles across hibernate and comes
        /// back showing its EEPROM colour (the user's normal colour), but the SoC band is unchanged,
        /// so the timer would not re-tint. Force a re-write so the LED returns to the SoC colour
        /// without needing the widget to be opened. The timer retries if the HID isn't ready yet.
        /// </summary>
        internal static void OnResumeReassertLedColorBySoc()
        {
            if (!_ledColorBySocEnabled) return;
            _lastSocBand = -1;
            LedSocTick();
        }

        /// <summary>Pipe handler target. Persists the setting and applies/restores immediately.</summary>
        internal static void SetLedColorBySoc(bool on)
        {
            _ledColorBySocEnabled = on;
            try { Settings.LocalSettingsHelper.SetValue(LedColorBySocSettingKey, on); }
            catch (Exception ex) { Logger.Warn($"[LedSoC] save failed: {ex.Message}"); }
            Logger.Info($"[LedSoC] enabled set to {on}");

            if (on)
            {
                _lastSocBand = -1;   // force apply on the next evaluation
                LedSocTick();        // apply immediately
            }
            else
            {
                _lastSocBand = -1;
                RestoreUserLedColor();
            }
        }

        /// <summary>Re-applies the user's stored LED color/brightness (used when SoC tinting is turned off).</summary>
        private static void RestoreUserLedColor()
        {
            try
            {
                if (Devices.DeviceDetector.DetectDevice().DeviceType != DeviceType.MSIClaw) return;
                if (MsiLedColorStore.TryLoad(out byte r, out byte g, out byte b, out byte brightness))
                {
                    MsiClawLedController.TrySetLedColor(r, g, b, brightness);
                    Logger.Info($"[LedSoC] Restored user LED color {r},{g},{b} @ {brightness}%");
                }
            }
            catch (Exception ex) { Logger.Debug($"[LedSoC] restore threw: {ex.Message}"); }
        }
    }
}
