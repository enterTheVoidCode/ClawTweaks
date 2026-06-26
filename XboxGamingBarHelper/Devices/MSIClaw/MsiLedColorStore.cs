using System;
using System.IO;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Persists the user's chosen MSI Claw LED color helper-side, so the helper can re-apply it on
    /// its own at startup — independently of the widget (which may not be connected yet right after a
    /// reboot). Stored as "R,G,B" or "R,G,B,Brightness" (brightness 0-100; 0 = LED off) in LocalState.
    ///
    /// The presence of this file is the single source of truth for "the user has chosen a custom LED
    /// color". When it is absent the helper must NOT touch the LED at all and leave whatever MSI set.
    ///
    /// Brightness MUST be persisted here too — otherwise the LED on/off tile's "off" state (brightness
    /// 0) is lost on reboot and the helper re-applies the saved colour at full brightness (white/bright).
    /// </summary>
    internal static class MsiLedColorStore
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string FileName = "msi_led_color.txt";
        private const string BootCycleFileName = "msi_led_bootcycle.txt";

        private static string ResolvePath() => ResolvePath(FileName);

        private static string ResolvePath(string fileName)
        {
            string localState;
            try
            {
                // Works when running in package context.
                localState = global::Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                // Elevated mode (no package identity): same hardcoded ClawTweaks family the heartbeat
                // writer falls back to, so widget and helper agree on the LocalState folder.
                localState = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", "MSIClaw.ClawTweaks_7eszav2039cvc", "LocalState");
            }
            return Path.Combine(localState, fileName);
        }

        /// <summary>Persists the user's chosen color and brightness. Called whenever the widget pushes a new color.</summary>
        public static void Save(byte r, byte g, byte b, byte brightness = 100)
        {
            try
            {
                string path = ResolvePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, $"{r},{g},{b},{Math.Min((byte)100, brightness)}");
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiLedStore] Save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the user's stored color and brightness. Returns false when no custom color was ever
        /// saved — in which case the LED must be left untouched. Brightness defaults to 100 for legacy
        /// "R,G,B" files written before brightness was persisted.
        /// </summary>
        public static bool TryLoad(out byte r, out byte g, out byte b, out byte brightness)
        {
            r = g = b = 0;
            brightness = 100;
            try
            {
                string path = ResolvePath();
                if (!File.Exists(path)) return false;

                string[] parts = File.ReadAllText(path).Trim().Split(',');
                if (parts.Length < 3
                    || !byte.TryParse(parts[0].Trim(), out r)
                    || !byte.TryParse(parts[1].Trim(), out g)
                    || !byte.TryParse(parts[2].Trim(), out b))
                    return false;

                // Optional 4th field = brightness (0-100). Legacy 3-field files → default 100.
                if (parts.Length >= 4 && byte.TryParse(parts[3].Trim(), out byte br))
                    brightness = Math.Min((byte)100, br);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiLedStore] Load failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Persists the startup colour-cycle on/off preference (red→green→colour at boot).</summary>
        public static void SaveBootCycle(bool enabled)
        {
            try
            {
                string path = ResolvePath(BootCycleFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, enabled ? "1" : "0");
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiLedStore] SaveBootCycle failed: {ex.Message}");
            }
        }

        /// <summary>Loads the startup colour-cycle preference. Defaults to TRUE (enabled) when unset.</summary>
        public static bool LoadBootCycle()
        {
            // Startup colour cycle (red loading flash) is disabled: the toggle was removed from the UI
            // and the cycle code must not run. Forced off here so MsiLedBoot always takes the cycle-off
            // path (apply the saved colour, no red flash). The original preference-read logic below is
            // intentionally kept (unreached) so the feature can be re-enabled later.
            return false;
#pragma warning disable CS0162 // Unreachable code detected (kept on purpose)
            try
            {
                string path = ResolvePath(BootCycleFileName);
                if (!File.Exists(path)) return true; // default on
                return File.ReadAllText(path).Trim() != "0";
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiLedStore] LoadBootCycle failed: {ex.Message}");
                return true;
            }
#pragma warning restore CS0162
        }
    }
}
