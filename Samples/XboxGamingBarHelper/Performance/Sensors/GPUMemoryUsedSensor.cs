using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUMemoryUsedSensor : HardwareSensor
    {
        // Sensor names: AMD/Nvidia="GPU Memory Used", Nvidia="D3D Dedicated Memory Used", Intel="D3D Shared Memory Used"
        private static readonly string[] SensorNames = new[] { "GPU Memory Used", "D3D Dedicated Memory Used", "D3D Shared Memory Used" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUMemoryUsedSensor() : base(SensorNames, GpuTypes, SensorType.SmallData)
        {
        }
    }
}
