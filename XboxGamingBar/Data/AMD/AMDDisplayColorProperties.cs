using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // Brightness
    internal class AMDDisplayBrightnessSupportedProperty : WidgetControlEnabledProperty<Slider>
    {
        public AMDDisplayBrightnessSupportedProperty(Slider inUI, Page inOwner) : base(Function.AMDDisplayBrightnessSupported, inUI, inOwner)
        {
        }
    }

    internal class AMDDisplayBrightnessProperty : WidgetSliderProperty
    {
        public AMDDisplayBrightnessProperty(Slider inControl, Page inOwner) : base(0, Function.AMDDisplayBrightness, inControl, inOwner)
        {
        }
    }

    // Contrast
    internal class AMDDisplayContrastSupportedProperty : WidgetControlEnabledProperty<Slider>
    {
        public AMDDisplayContrastSupportedProperty(Slider inUI, Page inOwner) : base(Function.AMDDisplayContrastSupported, inUI, inOwner)
        {
        }
    }

    internal class AMDDisplayContrastProperty : WidgetSliderProperty
    {
        public AMDDisplayContrastProperty(Slider inControl, Page inOwner) : base(100, Function.AMDDisplayContrast, inControl, inOwner)
        {
        }
    }

    // Saturation
    internal class AMDDisplaySaturationSupportedProperty : WidgetControlEnabledProperty<Slider>
    {
        public AMDDisplaySaturationSupportedProperty(Slider inUI, Page inOwner) : base(Function.AMDDisplaySaturationSupported, inUI, inOwner)
        {
        }
    }

    internal class AMDDisplaySaturationProperty : WidgetSliderProperty
    {
        public AMDDisplaySaturationProperty(Slider inControl, Page inOwner) : base(100, Function.AMDDisplaySaturation, inControl, inOwner)
        {
        }
    }

    // Temperature
    internal class AMDDisplayTemperatureSupportedProperty : WidgetControlEnabledProperty<Slider>
    {
        public AMDDisplayTemperatureSupportedProperty(Slider inUI, Page inOwner) : base(Function.AMDDisplayTemperatureSupported, inUI, inOwner)
        {
        }
    }

    internal class AMDDisplayTemperatureProperty : WidgetSliderProperty
    {
        public AMDDisplayTemperatureProperty(Slider inControl, Page inOwner) : base(6500, Function.AMDDisplayTemperature, inControl, inOwner)
        {
        }
    }
}
