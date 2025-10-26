using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class CPUClockMaxProperty : HelperProperty<int, PowerManager>
    {
        public CPUClockMaxProperty(int inValue, PowerManager inManager) : base(inValue, null, Function.CPUClockMax, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (Manager.LimitCPUClock)
            {
                PowerManager.SetCpuFreqLimit(true, (uint)Value, false);
                PowerManager.SetCpuFreqLimit(false, (uint)Value, false);
                PowerManager.SetCpuFreqLimit(true, (uint)Value, true);
                PowerManager.SetCpuFreqLimit(false, (uint)Value, true);
            }
            else
            {
                Logger.Info($"CPU clock limit is disabled, skip applying.");
            }
        }
    }
}
