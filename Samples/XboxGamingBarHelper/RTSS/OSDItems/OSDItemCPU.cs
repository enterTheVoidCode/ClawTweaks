using LibreHardwareMonitor.Hardware;
using System.Collections.Generic;
using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemCPU : OSDItem
    {
        private ISensor cpuUsageSensor;
        private ISensor cpuClockSensor;
        private ISensor cpuWattageSensor;
        private ISensor cpuTemperatureSensor;

        public OSDItemCPU(ISensor cpuUsageSensor, ISensor cpuClockSensor, ISensor cpuWattageSensor, ISensor cpuTemperatureSensor) : base("CPU", Color.LimeGreen)
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
                osdItems.Add(new OSDItemValue(cpuUsageSensor != null ? cpuUsageSensor.Value.Value : -1.0f, "%"));
                osdItems.Add(new OSDItemValue(cpuTemperatureSensor != null ? cpuTemperatureSensor.Value.Value : -1.0f, "°C"));
            }

            // for level 4, show CPU usage, clock speed, wattage and temperature.
            if (osdLevel >= 4)
            {
                osdItems.Add(new OSDItemValue(cpuClockSensor != null ? cpuClockSensor.Value.Value : -1.0f, "MHz"));
                osdItems.Add(new OSDItemValue(cpuWattageSensor != null ? cpuWattageSensor.Value.Value : -1.0f, "W"));
            }

            return osdItems;
        }
    }
}
