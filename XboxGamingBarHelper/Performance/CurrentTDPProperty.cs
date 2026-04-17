using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Performance
{
    internal class CurrentTDPProperty : HelperProperty<string, PerformanceManager>
    {
        public CurrentTDPProperty(string inValue, IProperty inParentProperty, PerformanceManager inManager) : base(inValue, inParentProperty, Function.CurrentTDP, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            // CurrentTDP is read-only, no action needed when it changes
            // The value is updated by PerformanceManager timer
        }
    }
}
