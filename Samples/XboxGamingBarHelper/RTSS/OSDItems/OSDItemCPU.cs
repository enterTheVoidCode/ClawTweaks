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

        private bool showClock = false;

        public OSDItemCPU(HardwareSensor cpuUsageSensor, HardwareSensor cpuClockSensor, HardwareSensor cpuWattageSensor, HardwareSensor cpuTemperatureSensor) : base("CPU", "CPU", Color.Turquoise)
        {
            this.cpuWattageSensor = cpuWattageSensor;
            this.cpuUsageSensor = cpuUsageSensor;
            this.cpuClockSensor = cpuClockSensor;
            this.cpuTemperatureSensor = cpuTemperatureSensor;
        }

        public void SetShowClock(bool show)
        {
            showClock = show;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // Show CPU usage, wattage and temperature when enabled
            osdItems.Add(new OSDItemValue(cpuUsageSensor.Value, "%", OSDValueType.Percentage));
            osdItems.Add(new OSDItemValue(cpuWattageSensor.Value, "W", OSDValueType.Wattage));
            osdItems.Add(new OSDItemValue(cpuTemperatureSensor.Value, "C", OSDValueType.Temperature));

            // Show clock speed if enabled separately
            if (showClock)
            {
                osdItems.Add(new OSDItemValue(cpuClockSensor.Value, "MHz", OSDValueType.Speed));
            }

            return osdItems;
        }
    }
}
