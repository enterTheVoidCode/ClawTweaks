using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class CPUBoostProperty : HelperProperty<bool, PowerManager>
    {
        // Track whether the user has explicitly changed this value
        // On fresh install, we read from system but don't write back unless user changes it
        private bool _hasUserModified = false;
        private bool _initialValue;

        // Set while a periodic system readback (EnforceCpuAdvanced) is pushing the true current
        // value into this property purely to correct the display — never write it back to the
        // system, or we'd fight whatever external tool (e.g. MSI Center M) just changed it.
        private bool _isSyncingFromSystem = false;

        public CPUBoostProperty(bool inValue, PowerManager inManager) : base(inValue, null, Function.CPUBoost, inManager)
        {
            _initialValue = inValue;
        }

        /// <summary>Reflects the true current system value in the UI without writing back to Windows.</summary>
        public void SyncFromSystem(bool systemValue)
        {
            if (Value == systemValue) return;
            _isSyncingFromSystem = true;
            try { SetValue(systemValue); }
            finally { _isSyncingFromSystem = false; }
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (_isSyncingFromSystem) return;

            // Only apply to system if user has explicitly changed the value
            // This prevents overwriting system settings on first startup/sync
            if (!_hasUserModified)
            {
                // Apply when the value diverged from the initial system read, OR when it arrives at runtime
                // with the widget connected (a genuine user/profile change — the value can equal our stale
                // initial when the system was changed via the Boost Mode dropdown in the meantime). Only
                // skip the pre-connect fresh-startup echo of an unchanged value.
                if (Value != _initialValue || Program.IsPipeConnected)
                {
                    _hasUserModified = true;
                }
                else
                {
                    // Value is same as initial and no widget yet - fresh-startup sync, don't write to system
                    Logger.Debug($"CPU Boost: Skipping system write - value unchanged from initial ({Value})");
                    return;
                }
            }

            PowerManager.SetCpuBoostMode(false, Value);
            PowerManager.SetCpuBoostMode(true, Value);
        }
    }
}
