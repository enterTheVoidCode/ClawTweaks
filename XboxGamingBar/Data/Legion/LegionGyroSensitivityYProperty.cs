using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gyro Y-axis sensitivity (1-100). Default 100 (was 50).
    /// </summary>
    internal class LegionGyroSensitivityYProperty : WidgetSliderProperty
    {
        public LegionGyroSensitivityYProperty(Slider inUI, Page inOwner) : base(100, Function.LegionGyroSensitivityY, inUI, inOwner)
        {
        }
    }
}
