using System;
using System.Runtime.InteropServices;

namespace XboxGamingBar
{
    /// <summary>
    /// Detection (diagnostics only) for the "blank widget after hibernate / Modern Standby" bug.
    ///
    /// WHAT HAPPENS: after a long S0 standby or hibernate the Game Bar keeps THIS widget instance
    /// alive, but its CoreWindow/CoreDispatcher has been torn down underneath it. Every subsequent
    /// touch of the dispatcher throws InvalidComObjectException ("COM object ... separated from its
    /// underlying RCW"), so nothing is ever redrawn -> the widget renders empty. Re-opening Win+G
    /// does not help because the instance is never replaced; only a helper restart fixes it, and it
    /// only fixes it by crashing this process so the Game Bar re-hosts a fresh one.
    ///
    /// WHY THIS FILE EXISTS (step 1 of 2): before shipping any automatic recovery we need proof from
    /// affected users that this is really what they are hitting. Until now the failure was logged as
    /// "Error parsing Quick Metrics JSON" (the RunAsync call sits inside the JSON try block), the
    /// exception TYPE was never recorded, and it repeated at 1 Hz - unreadable and misleading. This
    /// adds one unambiguous marker plus a throttled summary.
    ///
    /// The recovery itself (controlled Application.Current.Exit so the Game Bar re-hosts) is NOT
    /// implemented here - it waits on the user logs this produces.
    ///
    /// Note the streak is deliberately a per-instance field: a dead dispatcher is a property of this
    /// widget instance, and a fresh instance must start from zero.
    /// </summary>
    public sealed partial class GamingWidget
    {
        // "The object invoked has disconnected from its clients" - the COMException form of the same
        // teardown. InvalidComObjectException is the common one, but both mean the apartment is gone.
        private const int RpcEDisconnected = unchecked((int)0x80010108);

        private int _dispatcherDeadStreak;
        private DateTime _dispatcherDeadFirstSeen = DateTime.MinValue;
        private DateTime _dispatcherDeadLastLog = DateTime.MinValue;

        /// <summary>How long a dead dispatcher must persist before we log again (avoids 1 Hz spam).</summary>
        private static readonly TimeSpan DispatcherDeadLogInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// True if this exception means the CoreDispatcher/CoreWindow behind this instance is gone.
        /// This is an unambiguous signal - a healthy widget never produces it - which is what makes a
        /// later automatic recovery safe to gate on.
        /// </summary>
        private static bool IsDispatcherSeparated(Exception ex)
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
