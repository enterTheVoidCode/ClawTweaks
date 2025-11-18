using Shared.Enums;

namespace XboxGamingBar.Data
{
    // Action trigger property - set to "GameName|ExePath" to create a profile
    internal class LosslessScalingCreateProfileProperty : WidgetProperty<string>
    {
        public LosslessScalingCreateProfileProperty() : base("", null, Function.LosslessScalingCreateProfile)
        {
        }
    }
}
