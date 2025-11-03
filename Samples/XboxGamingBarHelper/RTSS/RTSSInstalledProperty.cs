using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.RTSS
{
    internal class RTSSInstalledProperty : HelperProperty<bool, RTSSManager>
    {
        public RTSSInstalledProperty(RTSSManager inManager) : base(false, null, Function.RTSSInstalled, inManager)
        {

        }
    }
}
