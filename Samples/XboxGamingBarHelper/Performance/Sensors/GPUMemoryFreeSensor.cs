using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUMemoryFreeSensor : HardwareSensor
    {
        // Sensor names: AMD/Nvidia="GPU Memory Free", Intel="D3D Shared Memory Free"
        private static readonly string[] SensorNames = new[] { "GPU Memory Free", "D3D Shared Memory Free" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUMemoryFreeSensor() : base(SensorNames, GpuTypes, SensorType.SmallData)
        {
        }
    }
}
