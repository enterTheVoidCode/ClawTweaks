using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Hardware.Sensors
{
    internal class CPUWattageSensor : HardwareSensor
    {
        public CPUWattageSensor() : base("Package", HardwareType.Cpu, SensorType.Power)
        {
        }
    }
}
