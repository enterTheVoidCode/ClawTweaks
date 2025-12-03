using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class CPUCoreActiveConfigProperty : HelperProperty<string, SystemManager>
    {
        public CPUCoreActiveConfigProperty(SystemManager inManager) : base("", null, Function.CPUCoreActiveConfig, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            var result = base.SetValue(newValue, updatedTime);
            if (result && manager != null && newValue is string configString && !string.IsNullOrEmpty(configString))
            {
                // Parse "activePCores,activeECores" format
                var parts = configString.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int activePCores) &&
                    int.TryParse(parts[1], out int activeECores))
                {
                    manager.ApplyCoreConfiguration(activePCores, activeECores);
                }
            }
            return result;
        }
    }
}
