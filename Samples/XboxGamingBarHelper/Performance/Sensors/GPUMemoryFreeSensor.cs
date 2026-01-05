using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUMemoryFreeSensor : HardwareSensor
    {
        // Sensor names: AMD/Nvidia/Intel="GPU Memory Free"
        private static readonly string[] SensorNames = new[] { "GPU Memory Free" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUMemoryFreeSensor() : base(SensorNames, GpuTypes, SensorType.SmallData)
        {
        }
    }
}
