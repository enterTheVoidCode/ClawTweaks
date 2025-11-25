using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Hardware.Sensors
{
    internal class CPUClockSensor : HardwareSensor
    {
        public CPUClockSensor() : base("Core #1", HardwareType.Cpu, SensorType.Clock)
        {
        }
    }
}
