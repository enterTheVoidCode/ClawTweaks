using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemMemory : OSDItem
    {
        private HardwareSensor memoryUsageSensor;
        private HardwareSensor memoryUsedSensor;

        public OSDItemMemory(HardwareSensor memoryUsageSensor, HardwareSensor memoryUsedSensor) : base("RAM", Color.Purple)
        {
            this.memoryUsageSensor = memoryUsageSensor;
            this.memoryUsedSensor = memoryUsedSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            if (osdLevel >= 3)
            {
                osdItems.Add(new OSDItemValue(memoryUsageSensor.Value, "%"));
            }

            if (osdLevel >= 4)
            {
                osdItems.Add(new OSDItemValue(memoryUsedSensor.Value, "GB"));
            }

            return osdItems;
        }
    }
}
