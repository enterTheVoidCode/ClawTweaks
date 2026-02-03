using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class CPUEPPProperty : HelperProperty<int, PowerManager>
    {
        // Track whether the user has explicitly changed this value
        // On fresh install, we read from system but don't write back unless user changes it
        private bool _hasUserModified = false;
        private int _initialValue;

        public CPUEPPProperty(int inValue, PowerManager inManager) : base(inValue, null, Function.CPUEPP, inManager)
        {
            _initialValue = inValue;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Only apply to system if user has explicitly changed the value
            // This prevents overwriting system settings on first startup/sync
            if (!_hasUserModified)
            {
                // Check if the value actually changed from the initial system value
                if (Value != _initialValue)
                {
                    _hasUserModified = true;
                }
                else
                {
                    // Value is same as initial - this is just a sync, don't write to system
                    Logger.Debug($"CPU EPP: Skipping system write - value unchanged from initial ({Value})");
                    return;
                }
            }

            PowerManager.SetEppValue(false, (uint)Value);
            PowerManager.SetEppValue(true, (uint)Value);
        }
    }
}
