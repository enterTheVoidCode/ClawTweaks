using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Hardware.Sensors
{
    internal class CPUUsageSensor : HardwareSensor
    {
        public CPUUsageSensor() : base("CPU Total", HardwareType.Cpu, SensorType.Load)
        {

        }
    }
}
