using LibreHardwareMonitor.Hardware;
using System.Collections.Generic;
using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemMemory : OSDItem
    {
        private ISensor memoryUsageSensor;
        private ISensor memoryUsedSensor;

        public OSDItemMemory(ISensor memoryUsageSensor, ISensor memoryUsedSensor) : base("RAM", Color.Purple)
        {
            this.memoryUsageSensor = memoryUsageSensor;
            this.memoryUsedSensor = memoryUsedSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            if (osdLevel >= 3)
            {
                osdItems.Add(new OSDItemValue(memoryUsageSensor != null ? memoryUsageSensor.Value.Value : -1.0f, "%"));
            }

            if (osdLevel >= 4)
            {
                osdItems.Add(new OSDItemValue(memoryUsedSensor != null ? memoryUsedSensor.Value.Value : -1.0f, "GB"));
            }

            return osdItems;
        }
    }
}
