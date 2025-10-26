using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.RTSS
{
    internal class OSDProperty : HelperProperty<int, RTSSManager>
    {
        public OSDProperty(int inValue, IProperty inParentProperty, RTSSManager inManager) : base(inValue, inParentProperty, Function.OSD, inManager)
        {
        }


    }
}
