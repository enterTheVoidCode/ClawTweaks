using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonBoostSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonBoostSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonBoostSupported, inManager)
        {
        }
    }

    internal class AMDRadeonBoostEnabledProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonBoostEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonBoostEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.AMDRadeonBoostSetting.SetEnabled(Value);
        }
    }

    internal class AMDRadeonBoostResolutionProperty : HelperProperty<int, AMDManager>
    {
        public AMDRadeonBoostResolutionProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonBoostResolution, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            (int min, int max) = Manager.AMDRadeonBoostSetting.GetResolutionRange();
            Manager.AMDRadeonBoostSetting.SetResolution(Value == 0 ? min : max);
        }
    }
}
