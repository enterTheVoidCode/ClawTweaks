using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Reflects the running state of MSI Center M and allows the widget to toggle it.
    /// true  = MSI Center M is active (processes/service running)
    /// false = MSI Center M is stopped
    ///
    /// When the widget writes a new value the helper calls MsiCenterManager.ApplyActive().
    /// </summary>
    internal class MsiCenterActiveProperty : HelperProperty<bool, MsiCenterManager>
    {
        public MsiCenterActiveProperty(MsiCenterManager manager)
            : base(false, null, Function.MsiCenterActive, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.ApplyActive(Value);
        }
    }
}
