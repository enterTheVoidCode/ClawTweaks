using LibreHardwareMonitor.Hardware;
using System.Collections.Generic;
using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemGPU : OSDItem
    {
        private ISensor gpuUsageSensor;
        private ISensor gpuClockSensor;
        private ISensor gpuWattageSensor;
        private ISensor gpuTemperatureSensor;

        public OSDItemGPU(ISensor gpuUsageSensor, ISensor gpuClockSensor, ISensor gpuWattageSensor, ISensor gpuTemperatureSensor) : base("GPU", Color.LawnGreen)
        {
            this.gpuWattageSensor = gpuWattageSensor;
            this.gpuUsageSensor = gpuUsageSensor;
            this.gpuClockSensor = gpuClockSensor;
            this.gpuTemperatureSensor = gpuTemperatureSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // for level 3, only show GPU usage and temperature.
            if (osdLevel >= 3)
            {
                osdItems.Add(new OSDItemValue(gpuUsageSensor != null ? gpuUsageSensor.Value.Value : -1.0f, "%"));
                osdItems.Add(new OSDItemValue(gpuWattageSensor != null ? gpuWattageSensor.Value.Value : -1.0f, "W"));
            }

            // for level 4, show GPU usage, clock speed, wattage and temperature.
            if (osdLevel >= 4)
            {
                osdItems.Add(new OSDItemValue(gpuClockSensor != null ? gpuClockSensor.Value.Value : -1.0f, "MHz"));
                osdItems.Add(new OSDItemValue(gpuTemperatureSensor != null ? gpuTemperatureSensor.Value.Value : -1.0f, "°C"));
            }

            return osdItems;
        }
    }
}
