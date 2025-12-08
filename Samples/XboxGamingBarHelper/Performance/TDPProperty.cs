using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Performance
{
    internal class TDPProperty : HelperProperty<int, PerformanceManager>
    {
        private bool forceNextApply = true; // Force apply on first SetValue after startup

        public TDPProperty(int inValue, IProperty inParentProperty, PerformanceManager inManager) : base(inValue, inParentProperty, Function.TDP, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            // On first SetValue after startup, force apply even if value matches
            // Convert to int first since ValueSet may deserialize as different numeric types
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
                // Invalidate cache by setting different value, use timestamp 0 so incoming message is always newer
                InvalidateCache(intValue - 1);
            }

            return base.SetValue(newValue, updatedTime);
        }

        /// <summary>
        /// Invalidates the cached value without updating the timestamp.
        /// This allows the next SetValue call to proceed even with an older timestamp.
        /// </summary>
        private void InvalidateCache(int newValue)
        {
            // Directly set the backing field without updating lastUpdatedTime
            // This is a workaround since SetValueSilent updates the timestamp
            typeof(Shared.Data.GenericProperty<int>)
                .GetField("value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(this, newValue);
            Logger.Debug($"Invalidated TDP cache to {newValue}");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.SetTDP(Value);
        }
    }
}
