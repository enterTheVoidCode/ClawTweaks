using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Intel Endurance Gaming FPS tier.
    /// 0=Off, 1=Performance(60fps), 2=Balanced(40fps), 3=Efficiency(30fps).
    /// Ported from IntelGameBar.
    /// </summary>
    internal class IntelFpsTierProperty : WidgetProperty<int>
    {
        public IntelFpsTierProperty() : base(0, null, Function.IntelFpsTier)
        {
        }
    }
}
