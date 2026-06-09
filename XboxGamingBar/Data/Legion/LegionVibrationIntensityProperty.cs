using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for stepless controller vibration intensity (0-100 %, default 100).
    /// MSI Claw: scales the rumble report ClawButtonMonitor sends to the physical controller.
    /// Stored global + per-game like the gyro-sensitivity sliders.
    /// </summary>
    internal class LegionVibrationIntensityProperty : WidgetSliderProperty
    {
        public LegionVibrationIntensityProperty(Slider inUI, Page inOwner) : base(100, Function.LegionVibrationIntensity, inUI, inOwner)
        {
        }
    }
}
