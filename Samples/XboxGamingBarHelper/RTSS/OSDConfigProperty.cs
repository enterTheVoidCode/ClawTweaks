using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.RTSS
{
    internal class OSDConfigProperty : HelperProperty<string, RTSSManager>
    {
        public OSDConfigProperty(RTSSManager inManager) : base("", null, Function.OSDConfig, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            var result = base.SetValue(newValue, updatedTime);
            if (result && manager != null && newValue is string configString)
            {
                manager.ParseOSDConfig(configString);
            }
            return result;
        }
    }
}
