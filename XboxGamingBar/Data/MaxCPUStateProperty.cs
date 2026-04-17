using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Maximum CPU State percentage (0-100).
    /// </summary>
    internal class MaxCPUStateProperty : WidgetProperty<int>
    {
        public MaxCPUStateProperty() : base(100, null, Function.MaxCPUState)
        {
        }
    }
}
