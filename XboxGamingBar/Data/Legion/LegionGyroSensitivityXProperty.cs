using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gyro X-axis sensitivity (1-100). Default 70 (tuned sweet spot;
    /// with the ×20 stick scale this is factor 1400).
    /// </summary>
    internal class LegionGyroSensitivityXProperty : WidgetSliderProperty
    {
        public LegionGyroSensitivityXProperty(Slider inUI, Page inOwner) : base(70, Function.LegionGyroSensitivityX, inUI, inOwner)
        {
        }
    }
}
