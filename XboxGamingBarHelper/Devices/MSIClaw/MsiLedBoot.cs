using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// MSI Claw LED startup indicator:
    ///   helper starting  → LED red
    ///   controller ready → LED green (brief) → the user's saved color
    ///
    /// This whole sequence runs ONLY when the user has saved a custom LED color
    /// (<see cref="MsiLedColorStore"/> has a value). With no saved color the helper never touches the
    /// LED and MSI's own color stays — per product decision. The green→saved step is the moment the
    /// virtual ViGEm controller finishes mounting, giving the user a clear "controller is ready" cue.
    /// </summary>
    internal static class MsiLedBoot
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const int StateIdle = 0;
        private const int StateRed  = 1;
        private const int StateDone = 2;
        private static int state = StateIdle;

        // green hold before the user's color is applied
        private const int GreenHoldMs = 700;

        /// <summary>
        /// Helper is starting: show red if (and only if) the user has a saved color. Best-effort —
        /// if the LED HID isn't ready yet it simply no-ops and the ready step still applies the color.
        /// </summary>
        public static void SignalHelperStarting()
        {
            if (!MsiLedColorStore.TryLoad(out _, out _, out _)) return; // no custom color → leave MSI's
            if (Interlocked.CompareExchange(ref state, StateRed, StateIdle) != StateIdle) return;

            Task.Run(() =>
            {
                try
                {
                    MsiClawLedController.TrySetLedColor(255, 0, 0);
                    Logger.Info("[MsiLedBoot] Helper starting → LED red");
                }
                catch (Exception ex) { Logger.Debug($"[MsiLedBoot] red failed: {ex.Message}"); }
            });
        }

        /// <summary>
        /// The virtual controller is mounted/ready: flash green, then apply the user's saved color.
        /// Runs once per helper run. No-op when no custom color is saved.
        /// </summary>
        public static void SignalControllerReady()
        {
            if (!MsiLedColorStore.TryLoad(out byte r, out byte g, out byte b)) return; // no custom color
            if (Interlocked.Exchange(ref state, StateDone) == StateDone) return;       // already done

            Task.Run(async () =>
            {
                try
                {
                    MsiClawLedController.TrySetLedColor(0, 255, 0);
                    Logger.Info("[MsiLedBoot] Controller ready → LED green");
                    await Task.Delay(GreenHoldMs);
                    bool ok = MsiClawLedController.TrySetLedColor(r, g, b);
                    Logger.Info($"[MsiLedBoot] Applied saved color R={r} G={g} B={b} → ok={ok}");
                }
                catch (Exception ex) { Logger.Debug($"[MsiLedBoot] ready sequence failed: {ex.Message}"); }
            });
        }
    }
}
