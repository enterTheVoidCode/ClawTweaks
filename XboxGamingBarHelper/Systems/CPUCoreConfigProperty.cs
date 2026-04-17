using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class CPUCoreConfigProperty : HelperProperty<string, SystemManager>
    {
        public CPUCoreConfigProperty(string inValue, SystemManager inManager) : base(inValue, null, Function.CPUCoreConfig, inManager)
        {
        }
    }
}
