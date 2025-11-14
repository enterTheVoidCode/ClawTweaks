using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class IsForegroundProperty : HelperProperty<bool, SettingsManager>
    {
        public IsForegroundProperty(SettingsManager inManager) : base(true, null, Function.Foreground, inManager)
        {
        }
    }
}
