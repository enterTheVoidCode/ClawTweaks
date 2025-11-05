using Shared.Data;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonSuperResolutionSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonSuperResolutionSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionSupported, inManager)
        {
        }
    }

    internal class AMDRadeonSuperResolutionEnabledProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonSuperResolutionEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.AMDRadeonSuperResolutionSetting.SetEnabled(Value);
        }
    }

    internal class AMDRadeonSuperResolutionSharpnessProperty : HelperProperty<int, AMDManager>
    {
        public AMDRadeonSuperResolutionSharpnessProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionSharpness, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            (int min, int max) = Manager.AMDRadeonSuperResolutionSetting.GetSharpnessRange();
            if (min == 0 && max == 100)
            {
                Manager.AMDRadeonSuperResolutionSetting.SetSharpness(Value);
            }
            else
            {
                Manager.AMDRadeonSuperResolutionSetting.SetSharpness((int)Math.Round(min + Value / 100.0f * (max - min)));
            }
        }
    }
}
