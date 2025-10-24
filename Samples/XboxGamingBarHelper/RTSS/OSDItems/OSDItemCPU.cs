using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemCPU : OSDItem
    {
        private HardwareSensor cpuUsageSensor;
        private HardwareSensor cpuClockSensor;
        private HardwareSensor cpuWattageSensor;
        private HardwareSensor cpuTemperatureSensor;

        public OSDItemCPU(HardwareSensor cpuUsageSensor, HardwareSensor cpuClockSensor, HardwareSensor cpuWattageSensor, HardwareSensor cpuTemperatureSensor) : base("CPU", Color.Turquoise)
        {
            this.cpuWattageSensor = cpuWattageSensor;
            this.cpuUsageSensor = cpuUsageSensor;
            this.cpuClockSensor = cpuClockSensor;
            this.cpuTemperatureSensor = cpuTemperatureSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // for level 3, only show CPU usage and temperature.
            if (osdLevel >= 3)
            {
                osdItems.Add(new OSDItemValue(cpuUsageSensor.Value, "%"));
                osdItems.Add(new OSDItemValue(cpuWattageSensor.Value, "W"));
            }

            // for level 4, show CPU usage, clock speed, wattage and temperature.
            if (osdLevel >= 4)
            {
                osdItems.Add(new OSDItemValue(cpuClockSensor.Value, "MHz"));
                osdItems.Add(new OSDItemValue(cpuTemperatureSensor.Value, "°C"));
            }

            return osdItems;
        }
    }
}
