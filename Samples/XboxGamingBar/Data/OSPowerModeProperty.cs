using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Windows 11 OS Power Mode (power slider).
    /// 0 = Best Power Efficiency, 1 = Balanced, 2 = Best Performance
    /// </summary>
    internal class OSPowerModeProperty : WidgetProperty<int>
    {
        public OSPowerModeProperty() : base(1, null, Function.OSPowerMode)
        {
        }
    }
}
