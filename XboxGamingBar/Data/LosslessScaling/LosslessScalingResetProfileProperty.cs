using Shared.Enums;

namespace XboxGamingBar.Data
{
    // Action trigger property — set to true to reset the active LS profile to defaults.
    // Helper resets in-memory property values; user must Apply-and-Restart to persist.
    internal class LosslessScalingResetProfileProperty : WidgetProperty<bool>
    {
        public LosslessScalingResetProfileProperty() : base(false, null, Function.LosslessScalingResetProfile)
        {
        }
    }
}
