using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class HDRSupportedProperty : HelperProperty<bool, SystemManager>
    {
        public HDRSupportedProperty(bool inValue, SystemManager inManager) : base(inValue, null, Function.HDRSupported, inManager)
        {
        }
    }
}
