using NLog;
using System;
using XboxGamingBarHelper.Performance.CPUs;
using XboxGamingBarHelper.Performance.CPUs.AMD;

namespace XboxGamingBarHelper.Performance
{
    internal class CPUFactory
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        //private static readonly Type GENERIC_CPU_TYPE = typeof(GenericCPU);
        private static readonly Type[] CPU_TYPES = new Type[]
        {
            typeof(AMD_Ryzen_7_7840U),
        };

        public static CPU CreateCPU(string cpuName)
        {
            foreach (Type cpuType in CPU_TYPES)
            {
                var cpuIdAttribute = (CPUIdAttribute)Attribute.GetCustomAttribute(cpuType, typeof(CPUIdAttribute));

                if (cpuIdAttribute == null)
                {
                    Logger.Error($"CPU type {cpuType.FullName} is missing CPUIdAttribute.");
                    continue;
                }

                if (cpuName.Contains(cpuIdAttribute.Id))
                {
                    return (CPU)Activator.CreateInstance(cpuType);
                }
            }

            Logger.Info($"No matching CPU type found for CPU name: {cpuName}. Using GenericCPU.");
            // If no matching CPU type is found, return a GenericCPU instance
            return new GenericCPU();
        }
    }
}
