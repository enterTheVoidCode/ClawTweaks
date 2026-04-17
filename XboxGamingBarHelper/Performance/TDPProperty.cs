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
