using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUWattageSensor : HardwareSensor
    {
        // Sensor names: AMD="GPU Core", Nvidia="GPU Power", Intel="GPU Power"
        private static readonly string[] SensorNames = new[] { "GPU Core", "GPU Power" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUWattageSensor() : base(SensorNames, GpuTypes, SensorType.Power)
        {
        }
    }
}
