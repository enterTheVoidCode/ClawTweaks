using Shared.Enums;

namespace XboxGamingBar.Data
{
    internal class IsForegroundProperty : WidgetProperty<bool>
    {
        public IsForegroundProperty() : base(true, null, Function.Foreground)
        {
        }
    }
}
