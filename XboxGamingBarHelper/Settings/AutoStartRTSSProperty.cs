using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class AutoStartRTSSProperty : HelperProperty<bool, SettingsManager>
    {
        public AutoStartRTSSProperty(SettingsManager inManager) : base(true, null, Function.Settings_AutoStartRTSS, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Logger.Info($"Auto Start RTSS changed to {Value}");
        }
    }
}
