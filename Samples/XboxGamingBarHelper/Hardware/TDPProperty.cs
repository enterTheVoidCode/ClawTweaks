using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Hardware
{
    internal class TDPProperty : HelperProperty<int, HardwareManager>
    {
        public TDPProperty(int inValue, IProperty inParentProperty, HardwareManager inManager) : base(inValue, inParentProperty, Function.TDP, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.SetTDP(Value);
        }
    }
}
