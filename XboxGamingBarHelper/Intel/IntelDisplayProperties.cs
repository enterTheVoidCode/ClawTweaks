using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Intel
{
    /// <summary>
    /// Adaptive sharpness intensity (0 = off, 1..100). Applied via IGCL on change.
    /// Stored in the existing performance profile (global + per-game).
    /// </summary>
    internal class IntelAdaptiveSharpnessProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelAdaptiveSharpnessProperty(IntelGpuManager manager)
            : base(0, null, Function.IntelAdaptiveSharpness, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (Value < 0) return; // unset
            Manager.ApplyAdaptiveSharpness(Value);
        }
    }

    /// <summary>
    /// Colour saturation percent (100 = neutral). Applied via IGCL on change.
    /// Stored in the existing performance profile (global + per-game).
    /// </summary>
    internal class IntelColorSaturationProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelColorSaturationProperty(IntelGpuManager manager)
            : base(100, null, Function.IntelColorSaturation, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (Value < 0) return; // unset
            Manager.ApplySaturation(Value);
        }
    }
}
