using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDImageSharpeningSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDImageSharpeningSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDImageSharpeningSupported, inManager)
        {
        }
    }

    internal class AMDImageSharpeningEnabledProperty : HelperProperty<bool, AMDManager>
    {
        public AMDImageSharpeningEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDImageSharpeningEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Notify the listener to start cooldown before we make the change
            // This prevents the listener from reading stale values when the driver callback fires
            Manager.AMD3DSettingsChangedListener?.NotifyRISChanged();

            Manager.AMDImageSharpeningSetting.SetEnabled(Value);
        }
    }

    internal class AMDImageSharpeningSharpnessProperty : HelperProperty<int, AMDManager>
    {
        public AMDImageSharpeningSharpnessProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDImageSharpeningSharpness, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            (int min, int max) = Manager.AMDImageSharpeningSetting.GetSharpnessRange();
            if (min == 0 && max == 100)
            {
                Manager.AMDImageSharpeningSetting.SetSharpness(Value);
            }
            else
            {
                Manager.AMDImageSharpeningSetting.SetSharpness((int)Math.Round(min + Value / 100.0f * (max - min)));
            }
        }
    }
}
