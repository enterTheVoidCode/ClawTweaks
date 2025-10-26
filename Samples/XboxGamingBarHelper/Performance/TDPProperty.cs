using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Performance
{
    internal class TDPProperty : HelperProperty<int, PerformanceManager>
    {
        public TDPProperty(int inValue, IProperty inParentProperty, PerformanceManager inManager) : base(inValue, inParentProperty, Function.TDP, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.SetTDP(Value);
        }
    }
}
