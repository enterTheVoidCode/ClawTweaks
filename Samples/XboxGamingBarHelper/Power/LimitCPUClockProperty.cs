using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class LimitCPUClockProperty : HelperProperty<bool, PowerManager>
    {
        public LimitCPUClockProperty(bool inValue, PowerManager inManager) : base(inValue, null, Function.LimitCPUClock, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Logger.Info($"{(Value ? "Enable" : "Disable")} CPU Clock limit.");
            var maxCPUClock = (uint)(Value ? Manager.CPUClockMax : 0);
            PowerManager.SetCpuFreqLimit(true, maxCPUClock, false);
            PowerManager.SetCpuFreqLimit(false, maxCPUClock, false);
            PowerManager.SetCpuFreqLimit(true, maxCPUClock, true);
            PowerManager.SetCpuFreqLimit(false, maxCPUClock, true);
        }
    }
}
