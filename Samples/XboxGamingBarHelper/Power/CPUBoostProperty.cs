using Shared.Enums;
using System.Runtime.CompilerServices;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class CPUBoostProperty : HelperProperty<bool, PowerManager>
    {
        public CPUBoostProperty(bool inValue, PowerManager inManager) : base(inValue, null, Function.CPUBoost, inManager)
        {
        }

        protected override void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            PowerManager.SetCpuBoostMode(false, Value);
            PowerManager.SetCpuBoostMode(true, Value);
        }
    }
}
