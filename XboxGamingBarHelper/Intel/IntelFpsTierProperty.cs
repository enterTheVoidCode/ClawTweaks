using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Intel
{
    /// <summary>
    /// Receives the Intel FPS tier from the widget and applies it via IGCL.
    /// Values: 0=off, 1=Performance(60fps), 2=Balanced(40fps), 3=Efficiency(30fps).
    /// Ported from IntelGameBar.
    /// </summary>
    internal class IntelFpsTierProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelFpsTierProperty(IntelGpuManager manager)
            : base(0, null, Function.IntelFpsTier, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.ApplyTier(Value);
        }
    }
}
