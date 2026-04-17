using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUMemoryClockSensor : HardwareSensor
    {
        // Sensor names: AMD/Nvidia/Intel="GPU Memory"
        private static readonly string[] SensorNames = new[] { "GPU Memory" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUMemoryClockSensor() : base(SensorNames, GpuTypes, SensorType.Clock)
        {
        }
    }
}
