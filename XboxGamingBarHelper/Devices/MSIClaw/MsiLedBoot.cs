using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Shared.Led;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// MSI Claw boot LED handling. Runs ONLY when the user has a saved LED state — either a per-zone
    /// composite (<see cref="MsiLedCompositeStore"/>) or the legacy solid colour (<see cref="MsiLedColorStore"/>).
    /// With nothing saved the helper never touches the LED and MSI's own colour stays.
    ///
    /// The saved LED is re-applied through <see cref="Program.TryApplyPersistedComposite"/> (which drives
    /// the composite when present, else falls back to the solid colour). During the controller mount
    /// (HidHide + DInput switch) the Claw's command-HID is briefly unavailable, so the apply RETRIES for a
    /// few seconds until the HID is back. A monotonic token cancels an in-flight sequence if a newer one
    /// starts (e.g. a re-mount).
    /// </summary>
    internal static class MsiLedBoot
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const int ApplyRetries    = 12;    // retry the apply this many times…
        private const int ApplyRetryGapMs = 700;   // …this far apart (~8 s window) until the command-HID is back

        // Bumped on every signal; an async sequence aborts if its captured token is no longer current.
        private static int _token;

        /// <summary>Helper start: re-apply the saved LED (composite or legacy colour). No-op when nothing is saved.</summary>
        public static void SignalHelperStarting()
        {
            if (!HasSavedLed()) return;   // nothing saved → leave MSI's LED
            int token = Interlocked.Increment(ref _token);
            _ = ApplyPersistedWithRetryAsync("helper start — applying saved LED", token);
        }

        /// <summary>Virtual ViGEm controller mounted: re-apply the saved LED (the command-HID is reliably up by now).</summary>
        public static void SignalControllerReady()
        {
            if (!HasSavedLed()) return;
            int token = Interlocked.Increment(ref _token);
            _ = ApplyPersistedWithRetryAsync("controller ready — applying saved LED", token);
        }

        private static bool HasSavedLed()
        {
            bool hasComposite = MsiLedCompositeStore.TryLoad(out LedCompositeSpec _);
            bool hasColor     = MsiLedColorStore.TryLoad(out byte _, out byte _, out byte _, out byte _);
            return hasComposite || hasColor;
        }

        /// <summary>
        /// Applies the persisted LED (composite, else legacy colour), retrying while the command-HID is
        /// unavailable (typical during the mount). Aborts early if a newer signal superseded this one.
        /// </summary>
        private static async Task<bool> ApplyPersistedWithRetryAsync(string reason, int token)
        {
            for (int i = 1; i <= ApplyRetries; i++)
            {
                if (token != Volatile.Read(ref _token)) return false; // superseded by a newer signal

                bool ok;
                try { ok = Program.TryApplyPersistedComposite(); }
                catch (Exception ex) { Logger.Debug($"[MsiLedBoot] apply composite threw: {ex.Message}"); ok = false; }

                if (ok)
                {
                    Logger.Info($"[MsiLedBoot] persisted LED applied ({reason}) → ok=True (attempt {i})");
                    return true;
                }
                await Task.Delay(ApplyRetryGapMs).ConfigureAwait(false);
            }
            Logger.Warn($"[MsiLedBoot] persisted LED ({reason}) → failed after {ApplyRetries} attempts (command-HID stayed unavailable)");
            return false;
        }
    }
}
