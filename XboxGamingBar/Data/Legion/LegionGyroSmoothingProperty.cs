using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion/Claw Gyro Smoothing — One-Euro min-cutoff amount, stored per engine mode.
    /// Range: 0-100 (higher = smoother). Default: 50. Only the Adaptive + MA engines consume it;
    /// Direct/HC ignores it. See ClawButtonMonitor.SetGyroSmoothing / ActiveMinCutoff.
    /// </summary>
    internal class LegionGyroSmoothingProperty : WidgetSliderProperty
    {
        public LegionGyroSmoothingProperty(Slider inUI, Page inOwner) : base(50, Function.LegionGyroSmoothing, inUI, inOwner)
        {
        }
    }
}
