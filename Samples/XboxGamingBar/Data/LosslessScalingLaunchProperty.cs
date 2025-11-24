using Shared.Enums;

namespace XboxGamingBar.Data
{
    // Action trigger property - set to true to launch Lossless Scaling (minimized) via helper
    internal class LosslessScalingLaunchProperty : WidgetProperty<bool>
    {
        public LosslessScalingLaunchProperty() : base(false, null, Function.LosslessScalingLaunch)
        {
        }
    }
}
