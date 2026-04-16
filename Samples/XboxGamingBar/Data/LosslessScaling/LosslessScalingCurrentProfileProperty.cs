using Shared.Enums;

namespace XboxGamingBar.Data
{
    // Read-only property - displays the current profile name
    internal class LosslessScalingCurrentProfileProperty : WidgetProperty<string>
    {
        public LosslessScalingCurrentProfileProperty() : base("Default", null, Function.LosslessScalingCurrentProfile)
        {
        }
    }
}
