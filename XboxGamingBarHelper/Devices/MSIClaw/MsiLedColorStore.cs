using System;
using System.IO;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Persists the user's chosen MSI Claw LED color helper-side, so the helper can re-apply it on
    /// its own at startup — independently of the widget (which may not be connected yet right after a
    /// reboot). Stored as "R,G,B" in LocalState.
    ///
    /// The presence of this file is the single source of truth for "the user has chosen a custom LED
    /// color". When it is absent the helper must NOT touch the LED at all and leave whatever MSI set.
    /// </summary>
    internal static class MsiLedColorStore
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string FileName = "msi_led_color.txt";

        private static string ResolvePath()
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
            return Path.Combine(localState, FileName);
        }

        /// <summary>Persists the user's chosen color. Called whenever the widget pushes a new color.</summary>
        public static void Save(byte r, byte g, byte b)
        {
            try
            {
                string path = ResolvePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, $"{r},{g},{b}");
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiLedStore] Save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the user's stored color. Returns false when no custom color was ever saved — in which
        /// case the LED must be left untouched.
        /// </summary>
        public static bool TryLoad(out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            try
            {
                string path = ResolvePath();
                if (!File.Exists(path)) return false;

                string[] parts = File.ReadAllText(path).Trim().Split(',');
                return parts.Length == 3
                    && byte.TryParse(parts[0].Trim(), out r)
                    && byte.TryParse(parts[1].Trim(), out g)
                    && byte.TryParse(parts[2].Trim(), out b);
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiLedStore] Load failed: {ex.Message}");
                return false;
            }
        }
    }
}
