using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for FPS Limit (RTSS). 0 = unlimited.
    /// </summary>
    internal class FPSLimitProperty : WidgetProperty<int>
    {
        public FPSLimitProperty() : base(0, null, Function.FPSLimit)
        {
        }
    }
}
