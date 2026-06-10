using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gyro X-axis sensitivity (1-100). Default 100 (was 50).
    /// </summary>
    internal class LegionGyroSensitivityXProperty : WidgetSliderProperty
    {
        public LegionGyroSensitivityXProperty(Slider inUI, Page inOwner) : base(100, Function.LegionGyroSensitivityX, inUI, inOwner)
        {
        }
    }
}
