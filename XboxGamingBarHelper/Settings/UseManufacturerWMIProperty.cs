using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Property to control whether to use manufacturer WMI for TDP instead of RyzenAdj.
    /// When enabled and a supported device (Legion Go) is detected, TDP will be set via WMI.
    /// </summary>
    internal class UseManufacturerWMIProperty : HelperProperty<bool, SettingsManager>
    {
        public UseManufacturerWMIProperty(SettingsManager inManager) : base(true, null, Function.Settings_UseManufacturerWMI, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Logger.Info($"Use Manufacturer WMI changed to {Value}");
        }
    }
}
