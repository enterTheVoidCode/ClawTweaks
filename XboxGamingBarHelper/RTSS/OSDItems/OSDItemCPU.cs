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

            // IntelGameBar-style order: usage% | clock (GHz, optional) | temp °C | wattage W
            // Skip sensors with value < 0 (N/A) so unavailable sensors are omitted rather than shown as N/A
            osdItems.Add(new OSDItemValue(cpuUsageSensor.Value, "%", OSDValueType.Percentage));

            // Clock shown inline (before temp/wattage) when CPUClock is enabled for the level
            if (showClock && cpuClockSensor.Value >= 0)
            {
                osdItems.Add(new OSDItemValue(cpuClockSensor.Value, "MHz", OSDValueType.Speed));
            }

            if (cpuTemperatureSensor.Value >= 0)
                osdItems.Add(new OSDItemValue(cpuTemperatureSensor.Value, "C", OSDValueType.Temperature));

            if (cpuWattageSensor.Value >= 0)
                osdItems.Add(new OSDItemValue(cpuWattageSensor.Value, "W", OSDValueType.Wattage));

            return osdItems;
        }
    }
}
