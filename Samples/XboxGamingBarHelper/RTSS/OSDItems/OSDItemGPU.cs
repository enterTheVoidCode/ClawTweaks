using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemGPU : OSDItem
    {
        private HardwareSensor gpuUsageSensor;
        private HardwareSensor gpuClockSensor;
        private HardwareSensor gpuWattageSensor;
        private HardwareSensor gpuTemperatureSensor;

        public OSDItemGPU(HardwareSensor gpuUsageSensor, HardwareSensor gpuClockSensor, HardwareSensor gpuWattageSensor, HardwareSensor gpuTemperatureSensor) : base("GPU", Color.LawnGreen)
        {
            this.gpuWattageSensor = gpuWattageSensor;
            this.gpuUsageSensor = gpuUsageSensor;
            this.gpuClockSensor = gpuClockSensor;
            this.gpuTemperatureSensor = gpuTemperatureSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // for level 3, show GPU usage, wattage and temperature.
            if (osdLevel >= 3)
            {
                osdItems.Add(new OSDItemValue(gpuUsageSensor.Value, "%"));
                osdItems.Add(new OSDItemValue(gpuWattageSensor.Value, "W"));
                osdItems.Add(new OSDItemValue(gpuTemperatureSensor.Value, "°C"));
            }

            // for level 4, also show clock speed.
            if (osdLevel >= 4)
            {
                osdItems.Add(new OSDItemValue(gpuClockSensor.Value, "MHz"));
            }

            return osdItems;
        }
    }
}
