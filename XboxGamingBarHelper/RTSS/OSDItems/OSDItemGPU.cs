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

            // Full preset (level 4) only: GPU power draw in parentheses right after the frequency,
            // e.g. "(6 W)". On Intel SoC this is LHM's "GPU Power" sensor.
            if (osdLevel == 4 && gpuWattageSensor != null && gpuWattageSensor.Value >= 0)
            {
                // Secondary info → rendered smaller (75%, same ratio as the FPS "ms"), reset with <S>.
                osdItems.Add(new OSDItemValue(gpuWattageSensor.Value, " W)<S>", "<S=75>(", OSDValueType.Wattage));
            }

            if (gpuTemperatureSensor.Value >= 0)
                osdItems.Add(new OSDItemValue(gpuTemperatureSensor.Value, "C", OSDValueType.Temperature));

            return osdItems;
        }
    }
}
