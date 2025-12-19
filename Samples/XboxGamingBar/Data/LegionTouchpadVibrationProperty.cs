using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Touchpad Vibration (on/off haptic feedback)
    /// Note: This is a GLOBAL setting, not per-game profile
    /// </summary>
    internal class LegionTouchpadVibrationProperty : WidgetToggleProperty
    {
        public LegionTouchpadVibrationProperty(ToggleSwitch inUI, Page inOwner) : base(true, Function.LegionTouchpadVibration, inUI, inOwner)
        {
        }
    }
}
