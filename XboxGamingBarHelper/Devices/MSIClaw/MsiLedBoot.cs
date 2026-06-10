using System;
using System.Threading.Tasks;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// MSI Claw LED at startup: apply the user's saved color, both as early as the helper starts and
    /// again once the virtual ViGEm controller is mounted (in case the LED HID wasn't ready yet at the
    /// very start). Runs ONLY when the user has saved a custom LED color (<see cref="MsiLedColorStore"/>
    /// has a value); with no saved color the helper never touches the LED and MSI's own color stays.
    ///
    /// (The earlier red→green "booting/ready" indicator was dropped: green was never visible, red
    /// sometimes lingered or wasn't visible at all. We just restore the user's color now.)
    /// </summary>
    internal static class MsiLedBoot
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>Applies the user's saved color as the helper starts. No-op without a saved color.</summary>
        public static void SignalHelperStarting() => ApplySavedColor("helper start");

        /// <summary>Re-applies the user's saved color when the virtual controller is ready (covers the
        /// case where the LED HID wasn't ready at the very start). No-op without a saved color.</summary>
        public static void SignalControllerReady() => ApplySavedColor("controller ready");

        private static void ApplySavedColor(string reason)
        {
            if (!MsiLedColorStore.TryLoad(out byte r, out byte g, out byte b)) return; // no custom color → leave MSI's

            Task.Run(() =>
            {
                try
                {
                    bool ok = MsiClawLedController.TrySetLedColor(r, g, b);
                    Logger.Info($"[MsiLedBoot] Applied saved color R={r} G={g} B={b} ({reason}) → ok={ok}");
                }
                catch (Exception ex) { Logger.Debug($"[MsiLedBoot] apply saved color failed ({reason}): {ex.Message}"); }
            });
        }
    }
}
