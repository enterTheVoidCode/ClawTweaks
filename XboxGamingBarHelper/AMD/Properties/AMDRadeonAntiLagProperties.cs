using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonAntiLagSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonAntiLagSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonAntiLagSupported, inManager)
        {
        }
    }

    internal class AMDRadeonAntiLagEnabledProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonAntiLagEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonAntiLagEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.AMDRadeonAntiLagSetting.SetEnabled(Value);
        }
    }
}
