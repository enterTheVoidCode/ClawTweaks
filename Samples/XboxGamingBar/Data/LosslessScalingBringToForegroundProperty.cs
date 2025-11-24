using Shared.Enums;

namespace XboxGamingBar.Data
{
    // Action trigger property - set to true to bring Lossless Scaling window to foreground
    internal class LosslessScalingBringToForegroundProperty : WidgetProperty<bool>
    {
        public LosslessScalingBringToForegroundProperty() : base(false, null, Function.LosslessScalingBringToForeground)
        {
        }
    }
}
