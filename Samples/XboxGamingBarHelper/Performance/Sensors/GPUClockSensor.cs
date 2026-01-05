using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUClockSensor : HardwareSensor
    {
        // Sensor names: AMD="GPU Core", Nvidia="GPU Core", Intel="GPU Core"
        private static readonly string[] SensorNames = new[] { "GPU Core" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUClockSensor() : base(SensorNames, GpuTypes, SensorType.Clock)
        {
        }
    }
}
