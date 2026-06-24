using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Performance
{
    internal class TDPProperty : HelperProperty<int, PerformanceManager>
    {
        private bool forceNextApply = true; // Force apply on first SetValue after startup
        private long profileAppliedTimestamp = 0; // Timestamp when profile TDP was applied

        public TDPProperty(int inValue, IProperty inParentProperty, PerformanceManager inManager) : base(inValue, inParentProperty, Function.TDP, inManager)
        {
        }

        /// <summary>
        /// Sets TDP from a profile switch. Uses a guaranteed future timestamp to ensure
        /// it takes precedence over any in-flight widget messages.
        /// </summary>
        public void SetProfileValue(int tdp)
        {
            // Use current time + 1 second buffer to ensure this beats any in-flight widget messages
            long futureTimestamp = System.DateTime.Now.Ticks + System.TimeSpan.TicksPerSecond;
            profileAppliedTimestamp = futureTimestamp;
            Logger.Info($"Setting profile TDP: {tdp}W (timestamp: {futureTimestamp})");
            base.SetValue(tdp, futureTimestamp);
        }

        /// <summary>
        /// Applies the helper-persisted GLOBAL TDP at startup and makes it AUTHORITATIVE for a grace
        /// window. This (a) consumes forceNextApply so the widget's first connect-time slider value
        /// (often the stale 25W default it sends BEFORE loading its saved profile) does NOT force-
        /// apply over us, and (b) sets profileAppliedTimestamp into the future so any widget TDP push
        /// arriving during the window is ignored by SetValue. Genuine user changes after the window
        /// (timestamp &gt; grace) still apply normally.
        /// </summary>
        public void ApplyStartupAuthority(int tdp, int graceMs)
        {
            forceNextApply = false; // we own the initial hardware apply; don't let the widget's first push force it
            long graceTimestamp = System.DateTime.Now.Ticks + (long)graceMs * System.TimeSpan.TicksPerMillisecond;
            profileAppliedTimestamp = graceTimestamp;
            Logger.Info($"TDP startup authority: {tdp}W (ignoring widget TDP pushes for ~{graceMs}ms)");
            base.SetValue(tdp, graceTimestamp); // update cached value (may be a no-op if seeded to the same value)
            // ALWAYS apply to hardware + refresh the PL1/PL2 readout, even when the cached value was
            // already this (seeded in the ctor) — otherwise NotifyPropertyChanged doesn't fire and
            // SetTDP/ApplyMsiClawTdp never runs (CurrentTDP stays "-- W", HW unset).
            Manager.SetTDP(tdp);
        }

        /// <summary>
        /// Same protection as <see cref="ApplyStartupAuthority"/>, but for a per-game WIDGET RECONNECT
        /// (Game Bar opened while a game with a per-game profile is running). The widget's initial
        /// batch-sync on connect pushes its stale TDP slider value (e.g. the previous game's 25W)
        /// BEFORE it loads the current game's profile. Re-assert the current per-game TDP to hardware
        /// and make it authoritative for a grace window so that stale push is IGNORED (both the
        /// hardware apply AND — because SetValue returns false — the per-game profile save). Genuine
        /// user changes after the window still apply normally.
        /// </summary>
        public void ApplyConnectAuthority(int tdp, int graceMs)
        {
            forceNextApply = false; // don't let the widget's connect push force-apply over us
            long graceTimestamp = System.DateTime.Now.Ticks + (long)graceMs * System.TimeSpan.TicksPerMillisecond;
            profileAppliedTimestamp = graceTimestamp;
            Logger.Info($"TDP connect authority: {tdp}W (ignoring stale widget TDP pushes for ~{graceMs}ms)");
            base.SetValue(tdp, graceTimestamp);
            Manager.SetTDP(tdp);
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            // Ignore widget messages with timestamps older than our last profile-applied timestamp
            // This prevents stale widget messages from overwriting profile TDP
            if (updatedTime > 0 && updatedTime < profileAppliedTimestamp)
            {
                Logger.Debug($"Ignoring TDP update with stale timestamp {updatedTime} < {profileAppliedTimestamp}");
                return false;
            }

            // On first SetValue after startup, force apply TDP to hardware
            // This ensures TDP is applied even if the value matches the cached value
            if (forceNextApply)
            {
                forceNextApply = false;
                int intValue;
                if (newValue is int i)
                    intValue = i;
                else if (newValue is long l)
                    intValue = (int)l;
                else if (int.TryParse(newValue?.ToString(), out int parsed))
                    intValue = parsed;
                else
                    intValue = Value; // fallback to current value

                Logger.Info($"Force applying initial TDP value: {intValue}W");
                // Apply TDP directly to hardware, bypassing the cache check
                Manager.SetTDP(intValue);
            }

            return base.SetValue(newValue, updatedTime);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Skip hardware apply if AutoTDP is managing TDP
            if (Manager.IsAutoTDPActive)
            {
                Logger.Debug($"Skipping TDP hardware apply - AutoTDP is active (value={Value}W)");
                return;
            }

            Manager.SetTDP(Value);
        }
    }
}
