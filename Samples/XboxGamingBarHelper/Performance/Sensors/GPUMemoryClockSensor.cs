using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUMemoryClockSensor : HardwareSensor
    {
        public GPUMemoryClockSensor() : base("GPU Memory", HardwareType.GpuAmd, SensorType.Clock)
        {
        }
    }
}
