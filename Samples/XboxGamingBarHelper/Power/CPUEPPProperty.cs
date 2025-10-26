using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class CPUEPPProperty : HelperProperty<int, PowerManager>
    {
        public CPUEPPProperty(int inValue, PowerManager inManager) : base(inValue, null, Function.CPUEPP, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            PowerManager.SetEppValue(false, (uint)Value);
            PowerManager.SetEppValue(true, (uint)Value);
        }
    }
}
