using System;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Tiny coordination primitive between the two controller-emulation
    /// managers' deferred startup applies.
    ///
    /// Helper init defers both
    /// <see cref="ControllerEmulationManager.ApplyCurrentConfiguration"/> ("startup")
    /// and <see cref="Viiper.ViiperEmulationManager"/>'s initial
    /// <c>ApplyBackend</c> to the thread pool so the main init thread can
    /// reach <c>_managersReady=true</c> inside a second instead of 14. Left
    /// unconstrained, the two tasks race on a single shared resource: HidHide
    /// suppression state.
    ///
    /// Observed failure (2026-04-19 20:11:46 log): VIIPER enabled HidHide and
    /// completed its PnP cycle-port, then the legacy manager's Apply fired
    /// Disable on HidHide and tried to re-enum — but Windows refuses a second
    /// cycle-port on the same device within a narrow window so the re-enum
    /// failed, leaving the physical Legion controller visible to games next to
    /// the VIIPER virtual one.
    ///
    /// This lock gives VIIPER's apply a barrier to wait on: ControllerEmulation
    /// signals when its legacy-settle pass is done, VIIPER then runs last and
    /// its HidHide state is the final state.
    /// </summary>
    internal static class ControllerEmulationStartupLock
    {
        private static readonly TaskCompletionSource<bool> _legacyApplyComplete =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Awaited by ViiperEmulationManager's deferred startup apply. Returns
        /// immediately if the legacy settle is already done (e.g. on second
        /// init after a disconnect/reconnect — rare, but belt-and-suspenders).
        /// Guards against indefinite hang with a 10s fallback timeout: if
        /// ControllerEmulationManager's deferred apply never fires (disabled
        /// device, exception path), VIIPER still gets to bring itself up.
        /// </summary>
        public static Task WaitForLegacyApplyAsync()
        {
            return Task.WhenAny(_legacyApplyComplete.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        }

        /// <summary>
        /// Called from ControllerEmulationManager's deferred apply finally
        /// block. Idempotent — extra calls are no-ops because TCS tolerates
        /// a single transition.
        /// </summary>
        public static void MarkLegacyApplyComplete()
        {
            _legacyApplyComplete.TrySetResult(true);
        }
    }
}
