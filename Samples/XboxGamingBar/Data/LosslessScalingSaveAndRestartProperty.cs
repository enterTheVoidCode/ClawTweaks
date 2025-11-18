using Shared.Enums;

namespace XboxGamingBar.Data
{
    // Action trigger property - set to true to trigger save and restart
    internal class LosslessScalingSaveAndRestartProperty : WidgetProperty<bool>
    {
        public LosslessScalingSaveAndRestartProperty() : base(false, null, Function.LosslessScalingSaveAndRestart)
        {
        }
    }
}
