using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// OSD item that shows only the CPU package wattage.
    /// Used by levels 3 (Horizontal Detailed) and 4 (Full) to group CPU power draw
    /// next to the Battery item (which shows total system power and remaining runtime),
    /// while keeping the CPU column focused on utilisation %.
    /// </summary>
    internal class OSDItemCPUWatts : OSDItem
    {
        private readonly HardwareSensor cpuWattageSensor;

        public OSDItemCPUWatts(HardwareSensor cpuWattageSensor)
            : base("CPU W", "CPUWatts", Color.Turquoise)
        {
            this.cpuWattageSensor = cpuWattageSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var items = base.GetValues(osdLevel);
            if (cpuWattageSensor != null && cpuWattageSensor.Value >= 0)
                items.Add(new OSDItemValue(cpuWattageSensor.Value, "W", OSDValueType.Wattage));
            return items;
        }
    }
}
