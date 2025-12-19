using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gyro X-axis sensitivity (1-100)
    /// </summary>
    internal class LegionGyroSensitivityXProperty : WidgetSliderProperty
    {
        public LegionGyroSensitivityXProperty(Slider inUI, Page inOwner) : base(50, Function.LegionGyroSensitivityX, inUI, inOwner)
        {
        }
    }
}
