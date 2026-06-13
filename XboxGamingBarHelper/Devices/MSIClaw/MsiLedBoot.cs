using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// MSI Claw boot LED handling. Runs ONLY when the user has saved a custom LED colour
    /// (<see cref="MsiLedColorStore"/> has a value); with no saved colour the helper never touches
    /// the LED and MSI's own colour stays.
    ///
    /// Two modes, driven by the user's "Startup colour cycle" toggle (<see cref="MsiLedColorStore.LoadBootCycle"/>):
    ///   • Cycle ON  (default): RED while the controller loads → the user's saved colour once the
    ///     ViGEm pad is mounted. (No green step — red→colour is enough and saves a redundant set.)
    ///   • Cycle OFF: no red flash — just put the saved colour on as soon as it can be applied.
    ///
    /// Reliability: during the controller mount (HidHide + DInput mode switch) the Claw's command-HID
    /// is briefly unavailable, so a single <c>TrySetLedColor</c> can fail ("HID device not found").
    /// The important colour applies therefore RETRY for a few seconds until the HID is back — that fixes
    /// the LED getting stuck on a transient colour / not reaching the saved colour. A monotonic token
    /// cancels an in-flight sequence if a newer one starts (e.g. a re-mount).
    /// </summary>
    internal static class MsiLedBoot
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const byte LoadR = 255, LoadG = 0, LoadB = 0;  // red = controller loading
        private const int  LoadingTimeoutMs = 9000;  // (cycle on) restore saved colour if no ready arrives
        private const int  ApplyRetries     = 12;    // retry an important apply this many times…
        private const int  ApplyRetryGapMs  = 700;   // …this far apart (~8 s window) until the HID is back

        // Bumped on every signal; an async sequence aborts if its captured token is no longer current.
        private static int _token;

        /// <summary>
        /// Helper start. Cycle ON → show RED (loading) + a safety net that restores the saved colour
        /// if no controller-ready follows. Cycle OFF → just apply the saved colour. No-op without a
        /// saved colour.
        /// </summary>
        public static void SignalHelperStarting()
        {
            if (!MsiLedColorStore.TryLoad(out byte r, out byte g, out byte b)) return; // no custom colour → leave MSI's
            int token = Interlocked.Increment(ref _token);

            if (!MsiLedColorStore.LoadBootCycle())
            {
                // Cycle off: no flash, just the user's colour (retry until the HID is ready).
                _ = ApplyWithRetryAsync(r, g, b, "helper start — cycle off, applying saved colour", token);
                return;
            }

            // Cycle on: red now (one shot — cosmetic), and a safety net so red never lingers if nothing mounts.
            SetOnce(LoadR, LoadG, LoadB, "helper start — controller loading (red)");
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(LoadingTimeoutMs).ConfigureAwait(false);
                    if (token != Volatile.Read(ref _token)) return; // controller-ready took over
                    await ApplyWithRetryAsync(r, g, b, "helper start — no controller-ready within timeout, restoring saved colour", token).ConfigureAwait(false);
                }
                catch (Exception ex) { Logger.Debug($"[MsiLedBoot] loading safety-net failed: {ex.Message}"); }
            });
        }

        /// <summary>
        /// Virtual ViGEm controller mounted. Cycle ON → switch from the red loading colour to the
        /// user's saved colour (retrying until the command-HID is reachable). Cycle OFF → no-op: the
        /// saved colour was already applied at helper start, so there's nothing to re-set here.
        /// No-op without a saved colour.
        /// </summary>
        public static void SignalControllerReady()
        {
            if (!MsiLedColorStore.TryLoad(out byte r, out byte g, out byte b)) return;
            if (!MsiLedColorStore.LoadBootCycle()) return; // cycle off → colour already on, don't re-set

            int token = Interlocked.Increment(ref _token);
            _ = ApplyWithRetryAsync(r, g, b, "controller ready — applying saved colour", token);
        }

        /// <summary>Single best-effort colour set (no retry) — for the transient red loading flash.</summary>
        private static void SetOnce(byte r, byte g, byte b, string reason)
        {
            bool ok = TrySet(r, g, b);
            Logger.Info($"[MsiLedBoot] LED → R={r} G={g} B={b} ({reason}) → ok={ok}");
        }

        /// <summary>
        /// Applies a colour, retrying while the command-HID is unavailable (typical during the mount).
        /// Aborts early if a newer sequence superseded this one (token changed).
        /// </summary>
        private static async Task<bool> ApplyWithRetryAsync(byte r, byte g, byte b, string reason, int token)
        {
            for (int i = 1; i <= ApplyRetries; i++)
            {
                if (token != Volatile.Read(ref _token)) return false; // superseded by a newer signal
                if (TrySet(r, g, b))
                {
                    Logger.Info($"[MsiLedBoot] LED → R={r} G={g} B={b} ({reason}) → ok=True (attempt {i})");
                    return true;
                }
                await Task.Delay(ApplyRetryGapMs).ConfigureAwait(false);
            }
            Logger.Warn($"[MsiLedBoot] LED → R={r} G={g} B={b} ({reason}) → failed after {ApplyRetries} attempts (command-HID stayed unavailable)");
            return false;
        }

        private static bool TrySet(byte r, byte g, byte b)
        {
            try { return MsiClawLedController.TrySetLedColor(r, g, b); }
            catch (Exception ex) { Logger.Debug($"[MsiLedBoot] set colour threw: {ex.Message}"); return false; }
        }
    }
}
