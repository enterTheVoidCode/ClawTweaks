using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AutoTDP
{
    internal class TDPLimitsProperty : HelperProperty<string, AutoTDPManager>
    {
        public TDPLimitsProperty(AutoTDPManager inManager) : base("4,35", null, Function.TDPLimits, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            var result = base.SetValue(newValue, updatedTime);
            if (result && manager != null && newValue is string limitsString)
            {
                // Parse "min,max" format
                var parts = limitsString.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int min) &&
                    int.TryParse(parts[1], out int max))
                {
                    manager.UpdateTDPLimits(min, max);
                }
            }
            return result;
        }
    }
}
