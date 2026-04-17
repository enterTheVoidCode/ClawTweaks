using Shared.Data;
using Shared.Enums;

namespace XboxGamingBar.Data
{
    internal class TrackedGameProperty : WidgetProperty<TrackedGame>
    {
        public TrackedGameProperty(TrackedGame inValue) : base(inValue, null, Function.TrackedGame)
        {
        }
    }
}
