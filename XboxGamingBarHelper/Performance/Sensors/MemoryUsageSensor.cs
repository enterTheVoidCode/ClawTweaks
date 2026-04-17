using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class MemoryUsageSensor : HardwareSensor
    {
        public MemoryUsageSensor() : base("Memory", HardwareType.Memory, SensorType.Load)
        {
        }
    }
}
