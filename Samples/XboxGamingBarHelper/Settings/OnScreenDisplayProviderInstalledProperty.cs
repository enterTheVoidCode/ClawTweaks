using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class OnScreenDisplayProviderInstalledProperty : HelperProperty<bool, SettingsManager>
    {
        public OnScreenDisplayProviderInstalledProperty(SettingsManager inManager) : base(false, null, Function.Settings_OnScreenDisplayProviderInstalled, inManager)
        {

        }
    }
}
