using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemMemory : OSDItem
    {
        private HardwareSensor memoryUsageSensor;
        private HardwareSensor memoryUsedSensor;

        public OSDItemMemory(HardwareSensor memoryUsageSensor, HardwareSensor memoryUsedSensor) : base("RAM", "Memory", Color.Purple)
        {
            this.memoryUsageSensor = memoryUsageSensor;
            this.memoryUsedSensor = memoryUsedSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // Always show memory usage when enabled
            osdItems.Add(new OSDItemValue(memoryUsageSensor.Value, "%", OSDValueType.Percentage));
            osdItems.Add(new OSDItemValue(memoryUsedSensor.Value, "GB", OSDValueType.None));

            return osdItems;
        }
    }
}
