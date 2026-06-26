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

        // 10% bands, low → high SoC (index = ComputeSocBand). Blue (full) down to purple (critical).
        private static readonly (byte R, byte G, byte B)[] LedSocBandColors =
        {
            (150,   0, 200),   // band 0: < 20%   — purple (critical, lowest)
            (200,   0,   0),   // band 1: 20-29   — red
            (255,  69,   0),   // band 2: 30-39   — orange-red
            (255, 140,   0),   // band 3: 40-49   — orange
            (255, 215,   0),   // band 4: 50-59   — yellow
            (160, 220,   0),   // band 5: 60-69   — lime / yellow-green
            (  0, 200,   0),   // band 6: 70-79   — green
            (  0, 100,   0),   // band 7: 80-89   — dark green
            (  0, 110, 255),   // band 8: >= 90   — blue (full)
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

        private static void LedSocTick()
        {
            try
            {
                if (!_ledColorBySocEnabled) return;
                if (Devices.DeviceDetector.DetectDevice().DeviceType != DeviceType.MSIClaw) return;

                // Only drive the LED if the user has it ON. MsiLedColorStore is the single source of
                // truth for "user chose a custom LED state"; brightness 0 = LED off.
                if (!MsiLedColorStore.TryLoad(out _, out _, out _, out byte brightness)) return;
                if (brightness == 0)
                {
                    _lastSocBand = -1;   // LED off → don't touch; re-apply when it comes back on
                    return;
                }

                int soc = (int)(performanceManager?.BatteryLevel?.Value ?? -1f);
                if (soc <= 0 || soc > 100) return;   // 0/invalid = not read yet; a real 0% can't happen (device off)

                int band = ComputeSocBand(soc);
                if (band == _lastSocBand) return;    // no band change → no HID write (anti-spam)

                var c = LedSocBandColors[band];
                bool ok = MsiClawLedController.TrySetLedColor(c.R, c.G, c.B, brightness);
                if (ok)
                {
                    _lastSocBand = band;
                    Logger.Info($"[LedSoC] SoC={soc}% → band {band} → LED {c.R},{c.G},{c.B} @ {brightness}%");
                }
            }
            catch (Exception ex) { Logger.Debug($"[LedSoC] tick threw: {ex.Message}"); }
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
