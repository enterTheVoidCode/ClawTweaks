using Shared.Enums;

namespace XboxGamingBar.Data
{
    internal class PawnIOAvailableProperty : WidgetProperty<bool>
    {
        public PawnIOAvailableProperty() : base(false, null, Function.TdpMethod_PawnIOAvailable) { }
    }
}
