using Shared.Enums;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingInstalledProperty : WidgetProperty<bool>
    {
        public LosslessScalingInstalledProperty() : base(false, null, Function.LosslessScalingInstalled)
        {
        }
    }
}
