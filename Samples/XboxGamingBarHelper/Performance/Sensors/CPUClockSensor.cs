using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class CPUClockSensor : HardwareSensor
    {
        public CPUClockSensor() : base("Core #1", HardwareType.Cpu, SensorType.Clock)
        {
        }
    }
}
