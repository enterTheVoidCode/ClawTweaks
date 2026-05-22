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

        private bool showClock = false;

        public OSDItemGPU(HardwareSensor gpuUsageSensor, HardwareSensor gpuClockSensor, HardwareSensor gpuWattageSensor, HardwareSensor gpuTemperatureSensor) : base("GPU", "GPU", Color.LawnGreen)
        {
            this.gpuWattageSensor = gpuWattageSensor;
            this.gpuUsageSensor = gpuUsageSensor;
            this.gpuClockSensor = gpuClockSensor;
            this.gpuTemperatureSensor = gpuTemperatureSensor;
        }

        public void SetShowClock(bool show)
        {
            showClock = show;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // IntelGameBar-style order: usage% | clock (GHz, optional) | temp °C
            // Skip sensors with value < 0 (N/A) so unavailable sensors are omitted rather than shown as N/A
            // GPU wattage omitted — on Intel SoC (MSI Claw) the CPU package power is the relevant TDP metric
            osdItems.Add(new OSDItemValue(gpuUsageSensor.Value, "%", OSDValueType.Percentage));

            // Clock shown inline (before temp) when GPUClock is enabled for the level
            if (showClock && gpuClockSensor.Value >= 0)
            {
                osdItems.Add(new OSDItemValue(gpuClockSensor.Value, "MHz", OSDValueType.Speed));
            }

            if (gpuTemperatureSensor.Value >= 0)
                osdItems.Add(new OSDItemValue(gpuTemperatureSensor.Value, "C", OSDValueType.Temperature));

            // GPU wattage commented out per user request — CPU TDP is more meaningful on SoC devices
            // osdItems.Add(new OSDItemValue(gpuWattageSensor.Value, "W", OSDValueType.Wattage));

            return osdItems;
        }
    }
}
