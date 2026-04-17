using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class MemoryAvailableSensor : HardwareSensor
    {
        public MemoryAvailableSensor() : base("Memory Available", HardwareType.Memory, SensorType.Data)
        {
        }
    }
}
