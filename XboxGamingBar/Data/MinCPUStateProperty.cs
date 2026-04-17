using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Minimum CPU State percentage (0-100).
    /// </summary>
    internal class MinCPUStateProperty : WidgetProperty<int>
    {
        public MinCPUStateProperty() : base(5, null, Function.MinCPUState)
        {
        }
    }
}
