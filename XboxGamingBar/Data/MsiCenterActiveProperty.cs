using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side mirror of MSI Center M active state.
    /// true  = MSI Center M is running
    /// false = MSI Center M is stopped
    /// Writing toggles the state via the helper.
    /// </summary>
    internal class MsiCenterActiveProperty : WidgetProperty<bool>
    {
        public MsiCenterActiveProperty() : base(false, null, Function.MsiCenterActive) { }
    }
}
