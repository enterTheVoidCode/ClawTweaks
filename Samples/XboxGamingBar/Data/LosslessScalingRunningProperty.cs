using Shared.Enums;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingRunningProperty : WidgetProperty<bool>
    {
        public LosslessScalingRunningProperty() : base(false, null, Function.LosslessScalingRunning)
        {
        }
    }
}
