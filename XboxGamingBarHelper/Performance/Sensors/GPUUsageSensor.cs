using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUUsageSensor : HardwareSensor
    {
        // Sensor names: AMD="GPU Core", Nvidia="GPU Core", Intel="D3D 3D" or "GPU"
        private static readonly string[] SensorNames = new[] { "GPU Core", "D3D 3D", "GPU" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUUsageSensor() : base(SensorNames, GpuTypes, SensorType.Load)
        {
        }
    }
}
