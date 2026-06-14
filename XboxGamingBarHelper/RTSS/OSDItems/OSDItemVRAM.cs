using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemVRAM : OSDItem
    {
        private HardwareSensor gpuMemoryUsedSensor;
        private HardwareSensor gpuMemoryFreeSensor;
        private HardwareSensor gpuMemoryClockSensor;

        public OSDItemVRAM(HardwareSensor gpuMemoryUsedSensor, HardwareSensor gpuMemoryFreeSensor, HardwareSensor gpuMemoryClockSensor) : base("VRAM", "VRAM", Color.Cyan)
        {
            this.gpuMemoryUsedSensor = gpuMemoryUsedSensor;
            this.gpuMemoryFreeSensor = gpuMemoryFreeSensor;
            this.gpuMemoryClockSensor = gpuMemoryClockSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // VRAM used / total (GB). The GPU-memory clock is intentionally omitted — the Claw's iGPU
            // uses shared memory and has no separate VRAM clock, so that sensor reads N/A.
            osdItems.Add(new OSDItemValue(gpuMemoryUsedSensor.Value / 1024f, "/", OSDValueType.None, 1)); // Convert MB to GB
            osdItems.Add(new OSDItemValue((gpuMemoryUsedSensor.Value + gpuMemoryFreeSensor.Value) / 1024f, "GB", OSDValueType.None, 1));

            return osdItems;
        }
    }
}
