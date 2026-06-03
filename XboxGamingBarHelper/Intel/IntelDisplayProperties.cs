using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Intel
{
    // Intel Display (IGCL) channels — the full TnC "Color Remaster" set, stored in the
    // existing performance profile (global + per-game). Hue+Saturation are applied together
    // (one wrapper call); Contrast+Brightness+Gamma are applied together; Sharpness separately.
    // Values are in TnC/IGCL units: hue -180..180 (0), saturation/contrast/brightness 0..100 (50),
    // gamma stored ×100 i.e. 30..280 (100 = 1.0), sharpness 0..100 (0).

    internal class IntelColorSaturationProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelColorSaturationProperty(IntelGpuManager m) : base(50, null, Function.IntelColorSaturation, m) { }
        protected override void NotifyPropertyChanged(string p = "") { base.NotifyPropertyChanged(p); Manager.ApplyHueSaturation(); }
    }

    internal class IntelColorHueProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelColorHueProperty(IntelGpuManager m) : base(0, null, Function.IntelColorHue, m) { }
        protected override void NotifyPropertyChanged(string p = "") { base.NotifyPropertyChanged(p); Manager.ApplyHueSaturation(); }
    }

    internal class IntelDisplayContrastProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelDisplayContrastProperty(IntelGpuManager m) : base(50, null, Function.IntelDisplayContrast, m) { }
        protected override void NotifyPropertyChanged(string p = "") { base.NotifyPropertyChanged(p); Manager.ApplyBrightnessContrastGamma(); }
    }

    internal class IntelDisplayBrightnessProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelDisplayBrightnessProperty(IntelGpuManager m) : base(50, null, Function.IntelDisplayBrightness, m) { }
        protected override void NotifyPropertyChanged(string p = "") { base.NotifyPropertyChanged(p); Manager.ApplyBrightnessContrastGamma(); }
    }

    /// <summary>Gamma stored ×100 (100 = 1.0). Range 30..280.</summary>
    internal class IntelDisplayGammaProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelDisplayGammaProperty(IntelGpuManager m) : base(100, null, Function.IntelDisplayGamma, m) { }
        protected override void NotifyPropertyChanged(string p = "") { base.NotifyPropertyChanged(p); Manager.ApplyBrightnessContrastGamma(); }
    }

    internal class IntelAdaptiveSharpnessProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelAdaptiveSharpnessProperty(IntelGpuManager m) : base(0, null, Function.IntelAdaptiveSharpness, m) { }
        protected override void NotifyPropertyChanged(string p = "") { base.NotifyPropertyChanged(p); Manager.ApplyAdaptiveSharpness(Value); }
    }
}
