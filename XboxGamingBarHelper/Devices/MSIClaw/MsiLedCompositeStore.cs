using System;
using System.IO;
using NLog;
using Shared.Led;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>Persists the per-zone LED composite (single source of truth for the boot LED).</summary>
    internal static class MsiLedCompositeStore
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string FileName = "msi_led_composite.txt";

        private static string ResolvePath()
        {
            string localState;
            try { localState = global::Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch
            {
                localState = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", "MSIClaw.ClawTweaks_7eszav2039cvc", "LocalState");
            }
            return Path.Combine(localState, FileName);
        }

        public static void Save(LedCompositeSpec spec)
        {
            try
            {
                string path = ResolvePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, spec.Serialize());
            }
            catch (Exception ex) { Logger.Debug($"[MsiLedComposite] Save failed: {ex.Message}"); }
        }

        public static bool TryLoad(out LedCompositeSpec spec)
        {
            spec = null;
            try
            {
                string path = ResolvePath();
                if (!File.Exists(path)) return false;
                return LedCompositeSpec.TryParse(File.ReadAllText(path), out spec);
            }
            catch (Exception ex) { Logger.Debug($"[MsiLedComposite] Load failed: {ex.Message}"); return false; }
        }
    }
}
