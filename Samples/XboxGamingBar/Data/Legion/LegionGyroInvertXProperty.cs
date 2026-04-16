using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gyro X-axis inversion
    /// </summary>
    internal class LegionGyroInvertXProperty : WidgetToggleProperty
    {
        public LegionGyroInvertXProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.LegionGyroInvertX, inUI, inOwner)
        {
        }
    }
}
