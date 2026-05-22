using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Intel
{
    /// <summary>
    /// Tracks which FPS-cap source is currently active: 0=RTSS, 1=Intel.
    /// Mutual exclusion (disabling the other source) is handled in Program.ProfileHandlers.cs callbacks.
    /// Ported from IntelGameBar.
    /// </summary>
    internal class FpsCapModeProperty : HelperProperty<int, IntelGpuManager>
    {
        public FpsCapModeProperty(IntelGpuManager manager)
            : base(0, null, Function.FpsCapMode, manager)
        {
        }
    }
}
