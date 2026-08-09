using NLog;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace XboxGamingBar
{
    /// <summary>
    /// The parts of the dispatcher-teardown handling that are NOT tied to a widget instance, so that
    /// <see cref="App"/> can use them too.
    ///
    /// The teardown does not only hit a LIVE instance (which the per-instance logic in
    /// <see cref="GamingWidget"/> covers) — it also hits instances that are still being CONSTRUCTED.
    /// Measured across three EX machines on 0.1.8.29: the Game Bar tears the CoreWindow down 205-292ms
    /// after "constructor START", i.e. inside InitializeComponent, which takes 244-354ms. Navigate()
    /// then throws, and the old catch in App.OnActivated logged it and carried on to
    /// Window.Current.Activate() — presenting an EMPTY frame. That is literally the blank widget users
    /// report: title bar, no content. In the 28.07 20:04 occurrence no other instance survived, so
    /// neither the Quick Metrics canary nor the resume ping saw anything and it stayed blank for
    /// 3.5 minutes, until "App suspending".
    /// </summary>
    internal static class DispatcherHealth
    {
        // "The object invoked has disconnected from its clients" - the COMException form of the same
        // teardown. InvalidComObjectException is the common one, but both mean the apartment is gone.
        private const int RpcEDisconnected = unchecked((int)0x80010108);

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Process-wide, set once an exit call has been issued. Deliberately NOT the same latch as
        /// <see cref="GamingWidget"/>'s per-instance one: a dead dispatcher is a property of an
        /// instance, but "we already asked this process to exit" is a property of the process.
        /// </summary>
        private static bool _exitRequested;

        /// <summary>
        /// True if this exception means the CoreDispatcher/CoreWindow behind the caller is gone.
        /// This is an unambiguous signal - a healthy widget never produces it - which is what makes
        /// automatic recovery safe to gate on.
        /// </summary>
        public static bool IsSeparated(Exception ex)
        {
            switch (ex)
            {
                case InvalidComObjectException _:
                    return true;
                case COMException com:
                    return com.HResult == RpcEDisconnected;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Exits the process so the Game Bar re-hosts a fresh widget with a working CoreDispatcher.
        /// Returns true if an exit was actually issued; false means the caller must fall back to its
        /// previous behaviour.
        ///
        /// The false cases are both deliberate. If <see cref="_exitRequested"/> is already set we do
        /// NOT exit again: a construction that fails systematically (a real bug in the widget, not a
        /// teardown race) would otherwise turn into an exit/re-host loop that the user cannot escape.
        /// One attempt, then degrade to the old behaviour — a blank widget is bad, an unkillable
        /// restart loop is worse. If both exit primitives throw we leave the latch clear so a later
        /// attempt can still try.
        ///
        /// Exit primitive order matters: <see cref="Windows.UI.Xaml.Application"/>.Current.Exit() is a
        /// call on the XAML app object and is affine to the apartment that just died, so it can hang
        /// or throw the same separated-RCW. <see cref="Windows.ApplicationModel.Core.CoreApplication"/>
        /// .Exit() is process-scoped and thread-agile, so it goes first.
        ///
        /// Reconnect after the exit is proven from the logs: the helper sees "Widget disconnected (end
        /// of stream)", re-arms, and the fresh widget reconnects on attempt 1 in 1ms with a full
        /// resync (~140ms).
        ///
        /// Note this kills the whole process, including an open standalone app-mode window. That was
        /// already true for the existing Quick Metrics recovery; this path can fire at widget-open
        /// time and therefore more often.
        /// </summary>
        /// <param name="site">Call site name - appears in the log so we can tell the paths apart.</param>
        public static bool ExitForRehost(string site)
        {
            if (_exitRequested)
            {
                Log.Error($"[WidgetDead] {site}: dispatcher separated again, but an exit was already " +
                          "requested in this process - not retrying, falling back to the old behaviour.");
                return false;
            }

            Log.Error($"[WidgetDead] recovery via {site}: the CoreWindow was torn down while the widget " +
                      "was still being built - exiting so the Game Bar re-hosts a fresh instance " +
                      "instead of showing an empty frame.");

            try
            {
                Windows.ApplicationModel.Core.CoreApplication.Exit();
                _exitRequested = true;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[WidgetDead] CoreApplication.Exit() threw: {ex.GetType().Name}: {ex.Message} - trying XAML Application.Exit()");
            }

            try
            {
                Windows.UI.Xaml.Application.Current.Exit();
                _exitRequested = true;
                return true;
            }
            catch (Exception ex)
            {
                // Latch stays clear: a later attempt may still succeed.
                Log.Error($"[WidgetDead] Application.Current.Exit() also threw: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Detection + recovery for the "blank widget after hibernate / Modern Standby" bug.
    ///
    /// WHAT HAPPENS: after a long S0 standby or hibernate the Game Bar keeps THIS widget instance
    /// alive, but its CoreWindow/CoreDispatcher has been torn down underneath it. Every subsequent
    /// touch of the dispatcher throws InvalidComObjectException ("COM object ... separated from its
    /// underlying RCW"), so nothing is ever redrawn -> the widget renders empty. Re-opening Win+G
    /// does not help because the instance is never replaced.
    ///
    /// STEP 1 (shipped 0.1.7.117): diagnostics only. Before shipping automatic recovery we needed
    /// proof from affected users that this is really what they are hitting - until then the failure
    /// was logged as "Error parsing Quick Metrics JSON" (the RunAsync call sits inside the JSON try
    /// block), the exception TYPE was never recorded, and it repeated at 1 Hz - unreadable and
    /// misleading. That shipped the `[WidgetDead]` marker plus a throttled summary below.
    ///
    /// STEP 2 (this recovery): confirmed live on a user machine 2026-07-20 (widget_2026-07-20_13.log)
    /// - streak grew 1 -> 16 over 15s with no "recovered" in between, i.e. not transient, and the
    /// widget stayed blank until an unrelated helper restart happened to terminate and re-host the
    /// whole process. `IsDispatcherSeparated` is an unambiguous signal (a healthy widget never
    /// produces it), so once the streak proves this isn't transient (>= RecoveryStreakThreshold,
    /// 3 observations at 5s apart = well past `NoteDispatcherAlive`'s reset-on-any-success), a
    /// controlled process exit (see <see cref="TriggerDispatcherDeadRecovery"/>) recovers it the
    /// same way the old crash-path accidentally did, minus the crash and its log noise.
    ///
    /// TWO detection paths feed the same recovery:
    ///  1. The 1 Hz Quick Metrics canary (<see cref="NoteDispatcherDead"/> streak) - steady state,
    ///     but SILENT when the user has Quick Metrics disabled.
    ///  2. A resume ping the helper pushes right after it re-applies TDP on resume
    ///     (<see cref="OnHelperResumePing"/>) - metrics-independent, so it closes the gap for
    ///     metrics-off users, who otherwise have no periodic heartbeat at all. It watches for
    ///     <see cref="ResumeWatchWindowMs"/> rather than probing once: the ping is only processed when
    ///     the suspended process is woken, which is the same moment the teardown happens, so a healthy
    ///     first probe proves nothing (see the constant's remarks for the field evidence).
    ///
    /// Confirmed in the field 2026-07-28 (widget_2026-07-28_23.log): the old instance's dispatcher was
    /// separated, a NEW instance the Game Bar created 0.7s earlier threw separated-RCW out of its own
    /// constructor (UpdateViGEmBusInstalledUI -> SetOnbStatus) and never came up, and the recovery's
    /// process-global exit took both down so the Game Bar could re-host cleanly. Blank window ~8s
    /// instead of "until something unrelated restarts the process". The process-global exit is
    /// therefore correct even when a newer instance exists - that instance is born broken too.
    ///
    /// Note the streak is deliberately a per-instance field: a dead dispatcher is a property of this
    /// widget instance, and a fresh instance must start from zero.
    ///
    /// STEP 3 covers the case neither path here can see: the teardown hitting an instance that is
    /// still in its CONSTRUCTOR, where there is no instance to hold a streak and no ping to receive.
    /// That lives in App.OnActivated and uses <see cref="DispatcherHealth"/> above.
    /// </summary>
    public sealed partial class GamingWidget
    {
        /// <summary>Consecutive dead-dispatcher observations (5s apart) before we self-recover.</summary>
        private const int RecoveryStreakThreshold = 3;

        private int _dispatcherDeadStreak;
        private DateTime _dispatcherDeadFirstSeen = DateTime.MinValue;
        private DateTime _dispatcherDeadLastLog = DateTime.MinValue;
        private bool _dispatcherRecoveryTriggered;

        /// <summary>How long a dead dispatcher must persist before we log again (avoids 1 Hz spam).</summary>
        private static readonly TimeSpan DispatcherDeadLogInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Settle delay before the resume-ping path re-checks a dead dispatcher. One dead probe right
        /// at resume could still be a momentary blip while the Game Bar rebuilds the view; two dead
        /// probes this far apart are the real teardown.
        /// </summary>
        private const int ResumeReprobeDelayMs = 2000;

        /// <summary>
        /// How long the resume ping keeps watching, and how often it probes.
        ///
        /// A SINGLE probe on arrival is systematically too early and was proven useless in the field
        /// (widget_2026-07-28_23.log): the ping is delivered only once the suspended process is woken,
        /// which is the same moment the Game Bar tears the old instance down. There the probe reported
        /// "healthy" at 23:12:03.19 and the dispatcher was separated at 23:12:04.00 — 0.8s later. Only
        /// the Quick Metrics canary caught it, i.e. the one path this ping exists to back up was blind.
        /// So the ping now watches for a while instead of sampling once.
        /// </summary>
        private const int ResumeWatchWindowMs = 15000;
        private const int ResumeWatchIntervalMs = 1000;

        /// <summary>
        /// True if this exception means the CoreDispatcher/CoreWindow behind this instance is gone.
        /// The test itself lives in <see cref="DispatcherHealth"/> because App needs it too (the
        /// constructor-time variant of the same teardown); this stays as the name the exception
        /// filters below read with.
        /// </summary>
        private static bool IsDispatcherSeparated(Exception ex) => DispatcherHealth.IsSeparated(ex);

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread, reporting dispatcher death instead of
        /// letting it surface as an unrelated error (or, on async void handlers, crash the process).
        /// Returns false if the dispatcher is gone.
        /// </summary>
        /// <param name="site">Call site name - appears in the log so we can tell which paths die first.</param>
        private bool TryRunOnDispatcher(Windows.UI.Core.DispatchedHandler action, string site)
        {
            try
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, action);
                NoteDispatcherAlive();
                return true;
            }
            catch (Exception ex) when (IsDispatcherSeparated(ex))
            {
                NoteDispatcherDead(site, ex);
                return false;
            }
        }

        /// <summary>
        /// Records one dispatcher-dead observation. Logs the first one in full, then at most one line
        /// per <see cref="DispatcherDeadLogInterval"/> so a wedged widget leaves a readable trail
        /// rather than thousands of identical lines.
        /// </summary>
        private void NoteDispatcherDead(string site, Exception ex)
        {
            DateTime now = DateTime.Now;
            _dispatcherDeadStreak++;

            if (_dispatcherDeadStreak == 1)
            {
                _dispatcherDeadFirstSeen = now;
                _dispatcherDeadLastLog = now;
                // The one line to grep for in user logs.
                Logger.Error($"[WidgetDead] dispatcher separated at {site} - the widget can no longer " +
                             $"redraw and will appear blank. instance={GetHashCode()}, " +
                             $"exception={ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (now - _dispatcherDeadLastLog < DispatcherDeadLogInterval) return;

            _dispatcherDeadLastLog = now;
            Logger.Error($"[WidgetDead] still dead at {site} - streak={_dispatcherDeadStreak}, " +
                         $"for {(now - _dispatcherDeadFirstSeen).TotalSeconds:F0}s, instance={GetHashCode()}");

            if (_dispatcherDeadStreak >= RecoveryStreakThreshold) TriggerDispatcherDeadRecovery(site);
        }

        /// <summary>
        /// Self-recovers a confirmed-dead widget instance by exiting the process, so the Game Bar
        /// re-hosts a fresh one with a working CoreDispatcher - the same outcome the old unhandled
        /// crash produced, but deliberate and without the crash-log noise / risk of taking anything
        /// else down with it.
        ///
        /// Called from a BACKGROUND thread (the named-pipe reader task - see
        /// <see cref="TryRunOnDispatcher"/>'s callers), which is exactly why this is reachable while
        /// the UI thread is wedged. That also dictates the exit primitive:
        /// <see cref="Windows.UI.Xaml.Application"/>.Current.Exit() is a call on the XAML app object,
        /// which is affine to the DEAD UI apartment - invoking it from here has to marshal into that
        /// torn-down apartment and can itself hang or throw the same separated-RCW. So the primary
        /// path is <see cref="Windows.ApplicationModel.Core.CoreApplication"/>.Exit(), which is
        /// process-scoped and thread-agile; the XAML Exit() is only a fallback.
        ///
        /// The once-per-instance latch is set only AFTER an exit call returns without throwing - if
        /// both throw, we leave it clear so the next 5s tick retries rather than giving up on a
        /// permanently blank widget. A successful CoreApplication.Exit() tears the process down, so
        /// any duplicate calls from later ticks in the meantime are harmless.
        /// </summary>
        private void TriggerDispatcherDeadRecovery(string site)
        {
            if (_dispatcherRecoveryTriggered) return;

            Logger.Error($"[WidgetDead] recovery via {site}: confirmed post-resume dispatcher teardown - " +
                         $"exiting so the Game Bar re-hosts a fresh instance. instance={GetHashCode()}");

            try
            {
                Windows.ApplicationModel.Core.CoreApplication.Exit();
                _dispatcherRecoveryTriggered = true;
                return;
            }
            catch (Exception ex)
            {
                Logger.Error($"[WidgetDead] CoreApplication.Exit() threw: {ex.GetType().Name}: {ex.Message} - trying XAML Application.Exit()");
            }

            try
            {
                Windows.UI.Xaml.Application.Current.Exit();
                _dispatcherRecoveryTriggered = true;
            }
            catch (Exception ex)
            {
                // Leave the latch clear: the next tick (5s) retries instead of leaving the widget blank.
                Logger.Error($"[WidgetDead] Application.Current.Exit() also threw: {ex.GetType().Name}: {ex.Message} - will retry next tick");
            }
        }

        /// <summary>
        /// Handles the one-shot "system just resumed" ping the helper pushes right after it re-applies
        /// TDP on resume (see PushResumePingToWidget on the helper). Runs on the pipe-reader background
        /// thread, so it is reachable even when the UI thread is wedged. The resume is the exact moment
        /// the Game Bar can tear down this instance's CoreDispatcher, and this ping is the only
        /// detection path that fires when the 1 Hz Quick Metrics canary is disabled.
        ///
        /// Most resumes do NOT kill the widget, so the common case here is "healthy for the whole
        /// window, do nothing". The teardown can land AFTER the ping arrives, so a healthy first probe
        /// proves nothing — we keep probing for <see cref="ResumeWatchWindowMs"/>. Only when two probes
        /// <see cref="ResumeReprobeDelayMs"/> apart both find a separated dispatcher — an unambiguous
        /// signal, correlated with an actual resume — do we recover.
        /// </summary>
        private async void OnHelperResumePing(string payload)
        {
            Logger.Info($"[Resume] helper reported system resume ({payload}) - watching dispatcher health " +
                        $"for {ResumeWatchWindowMs / 1000}s. instance={GetHashCode()}");

            var deadline = DateTime.Now.AddMilliseconds(ResumeWatchWindowMs);
            bool sawDead = false;

            while (DateTime.Now < deadline)
            {
                if (ProbeDispatcherAlive())
                {
                    await Task.Delay(ResumeWatchIntervalMs);
                    continue;
                }

                sawDead = true;
                Logger.Warn($"[Resume] dispatcher probe failed - re-checking in {ResumeReprobeDelayMs}ms to rule out a blip. instance={GetHashCode()}");
                await Task.Delay(ResumeReprobeDelayMs);

                if (ProbeDispatcherAlive())
                {
                    // Back to healthy: keep watching the rest of the window rather than trusting it.
                    Logger.Info($"[Resume] dispatcher recovered on re-probe - was transient, still watching. instance={GetHashCode()}");
                    continue;
                }

                Logger.Error($"[WidgetDead] resume ping + two failed dispatcher probes {ResumeReprobeDelayMs}ms apart confirm the post-resume teardown. instance={GetHashCode()}");
                TriggerDispatcherDeadRecovery("ResumePing");
                return;
            }

            if (sawDead)
                Logger.Info($"[Resume] dispatcher healthy at the end of the watch window (had a transient blip). instance={GetHashCode()}");
            else
                Logger.Info($"[Resume] dispatcher healthy for the whole watch window - no recovery needed. instance={GetHashCode()}");
        }

        /// <summary>
        /// Lightweight, streak-independent check of whether this instance's CoreDispatcher is still
        /// alive: a separated dispatcher throws synchronously on RunAsync (see
        /// <see cref="IsDispatcherSeparated"/>); anything else counts as alive. Deliberately does NOT
        /// touch the 1 Hz metrics-canary streak counters, so the resume path and the steady-state path
        /// stay independently readable in the logs.
        /// </summary>
        private bool ProbeDispatcherAlive()
        {
            try
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => { });
                return true;
            }
            catch (Exception ex) when (IsDispatcherSeparated(ex))
            {
                return false;
            }
        }

        /// <summary>
        /// Clears the streak after a successful dispatch. Anything transient therefore never
        /// accumulates - important for the recovery step that will later build on this counter.
        /// </summary>
        private void NoteDispatcherAlive()
        {
            if (_dispatcherDeadStreak == 0) return;

            Logger.Warn($"[WidgetDead] recovered after {_dispatcherDeadStreak} failed dispatch(es) over " +
                        $"{(DateTime.Now - _dispatcherDeadFirstSeen).TotalSeconds:F0}s - this was transient, " +
                        $"not the post-resume teardown. instance={GetHashCode()}");
            _dispatcherDeadStreak = 0;
            _dispatcherDeadFirstSeen = DateTime.MinValue;
            _dispatcherDeadLastLog = DateTime.MinValue;
        }
    }
}
