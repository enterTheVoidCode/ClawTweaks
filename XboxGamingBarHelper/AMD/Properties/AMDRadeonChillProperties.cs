using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonChillSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonChillSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonChillSupported, inManager)
        {
        }
    }

    internal class AMDRadeonChillEnabledProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonChillEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonChillEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.AMDRadeonChillSetting.SetEnabled(Value);
        }
    }

    internal class AMDRadeonChillMinFPSProperty : HelperProperty<int, AMDManager>
    {
        public AMDRadeonChillMinFPSProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonChillMinFPS, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.AMDRadeonChillSetting.SetMinFPS(Value);
        }
    }

    internal class AMDRadeonChillMaxFPSProperty : HelperProperty<int, AMDManager>
    {
        public AMDRadeonChillMaxFPSProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonChillMaxFPS, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.AMDRadeonChillSetting.SetMaxFPS(Value);
        }
    }
}
