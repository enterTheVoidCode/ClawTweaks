using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class CPUWattageSensor : HardwareSensor
    {
        public CPUWattageSensor() : base("Package", HardwareType.Cpu, SensorType.Power)
        {
        }
    }
}
