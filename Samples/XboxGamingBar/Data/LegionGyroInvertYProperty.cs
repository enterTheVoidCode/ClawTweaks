using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gyro Y-axis inversion
    /// </summary>
    internal class LegionGyroInvertYProperty : WidgetToggleProperty
    {
        public LegionGyroInvertYProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionGyroInvertY, inUI, inOwner)
        {
        }
    }
}
