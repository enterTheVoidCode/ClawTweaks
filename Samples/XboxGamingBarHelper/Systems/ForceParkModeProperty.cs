using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class ForceParkModeProperty : HelperProperty<bool, SystemManager>
    {
        public ForceParkModeProperty(SystemManager inManager) : base(false, null, Function.ForceParkMode, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            var result = base.SetValue(newValue, updatedTime);
            if (result && manager != null && newValue is bool enabled)
            {
                manager.SetForceParkMode(enabled);
            }
            return result;
        }
    }
}
