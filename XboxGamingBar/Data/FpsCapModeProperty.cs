using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property tracking which FPS-cap source is active: 0=RTSS, 1=Intel.
    /// Used by Quick Settings tile state to reflect the active limiter mode.
    /// Ported from IntelGameBar.
    /// </summary>
    internal class FpsCapModeProperty : WidgetProperty<int>
    {
        public FpsCapModeProperty() : base(0, null, Function.FpsCapMode)
        {
        }
    }
}
