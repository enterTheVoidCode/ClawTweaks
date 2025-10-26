using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class CPUBoostProperty : HelperProperty<bool, PowerManager>
    {
        public CPUBoostProperty(bool inValue, PowerManager inManager) : base(inValue, null, Function.CPUBoost, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            PowerManager.SetCpuBoostMode(false, Value);
            PowerManager.SetCpuBoostMode(true, Value);
        }
    }
}
