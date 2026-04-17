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

            // Show GPU usage, wattage and temperature when enabled
            osdItems.Add(new OSDItemValue(gpuUsageSensor.Value, "%", OSDValueType.Percentage));
            osdItems.Add(new OSDItemValue(gpuWattageSensor.Value, "W", OSDValueType.Wattage));
            osdItems.Add(new OSDItemValue(gpuTemperatureSensor.Value, "C", OSDValueType.Temperature));

            // Show clock speed if enabled separately
            if (showClock)
            {
                osdItems.Add(new OSDItemValue(gpuClockSensor.Value, "MHz", OSDValueType.Speed));
            }

            return osdItems;
        }
    }
}
