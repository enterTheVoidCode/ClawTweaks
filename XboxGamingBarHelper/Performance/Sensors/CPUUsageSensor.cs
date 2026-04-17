using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class CPUUsageSensor : HardwareSensor
    {
        public CPUUsageSensor() : base("CPU Total", HardwareType.Cpu, SensorType.Load)
        {

        }
    }
}
