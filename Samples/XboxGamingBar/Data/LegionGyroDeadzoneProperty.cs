using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gyro Deadzone - suppresses small motions near center
    /// Range: 1-100, Default: 10
    /// </summary>
    internal class LegionGyroDeadzoneProperty : WidgetSliderProperty
    {
        public LegionGyroDeadzoneProperty(Slider inUI, Page inOwner) : base(10, Function.LegionGyroDeadzone, inUI, inOwner)
        {
        }
    }
}
