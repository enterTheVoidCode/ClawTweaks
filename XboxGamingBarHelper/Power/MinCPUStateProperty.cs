using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    internal class MinCPUStateProperty : HelperProperty<int, PowerManager>
    {
        public MinCPUStateProperty(int inValue, PowerManager inManager) : base(inValue, null, Function.MinCPUState, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Logger.Info($"Setting Minimum CPU State to {Value}%.");
            PowerManager.SetMinCPUState(false, (uint)Value);
            PowerManager.SetMinCPUState(true, (uint)Value);
        }
    }
}
