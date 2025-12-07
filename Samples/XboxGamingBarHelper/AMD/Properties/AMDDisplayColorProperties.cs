using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    // Brightness
    internal class AMDDisplayBrightnessSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDDisplayBrightnessSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDDisplayBrightnessSupported, inManager) { }
    }

    internal class AMDDisplayBrightnessProperty : HelperProperty<int, AMDManager>
    {
        public AMDDisplayBrightnessProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDDisplayBrightness, inManager) { }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.AMDDisplayCustomColorSetting?.SetBrightness(Value);
        }
    }

    // Contrast
    internal class AMDDisplayContrastSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDDisplayContrastSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDDisplayContrastSupported, inManager) { }
    }

    internal class AMDDisplayContrastProperty : HelperProperty<int, AMDManager>
    {
        public AMDDisplayContrastProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDDisplayContrast, inManager) { }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.AMDDisplayCustomColorSetting?.SetContrast(Value);
        }
    }

    // Saturation
    internal class AMDDisplaySaturationSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDDisplaySaturationSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDDisplaySaturationSupported, inManager) { }
    }

    internal class AMDDisplaySaturationProperty : HelperProperty<int, AMDManager>
    {
        public AMDDisplaySaturationProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDDisplaySaturation, inManager) { }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.AMDDisplayCustomColorSetting?.SetSaturation(Value);
        }
    }

    // Temperature
    internal class AMDDisplayTemperatureSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDDisplayTemperatureSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDDisplayTemperatureSupported, inManager) { }
    }

    internal class AMDDisplayTemperatureProperty : HelperProperty<int, AMDManager>
    {
        public AMDDisplayTemperatureProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDDisplayTemperature, inManager) { }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.AMDDisplayCustomColorSetting?.SetTemperature(Value);
        }
    }
}
