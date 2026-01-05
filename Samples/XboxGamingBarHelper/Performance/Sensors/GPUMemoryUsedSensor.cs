using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUMemoryUsedSensor : HardwareSensor
    {
        // Sensor names: AMD/Nvidia/Intel="GPU Memory Used" or "D3D Dedicated Memory Used"
        private static readonly string[] SensorNames = new[] { "GPU Memory Used", "D3D Dedicated Memory Used" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUMemoryUsedSensor() : base(SensorNames, GpuTypes, SensorType.SmallData)
        {
        }
    }
}
