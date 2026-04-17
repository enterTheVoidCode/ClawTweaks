using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class MemoryUsedSensor : HardwareSensor
    {
        public MemoryUsedSensor() : base("Memory Used", HardwareType.Memory, SensorType.Data)
        {
        }
    }
}
