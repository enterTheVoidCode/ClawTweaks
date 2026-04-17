using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class MaxCPUStateProperty : HelperProperty<int, PowerManager>
    {
        public MaxCPUStateProperty(int inValue, PowerManager inManager) : base(inValue, null, Function.MaxCPUState, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Logger.Info($"Setting Maximum CPU State to {Value}%.");
            PowerManager.SetMaxCPUState(false, (uint)Value);
            PowerManager.SetMaxCPUState(true, (uint)Value);
        }
    }
}
