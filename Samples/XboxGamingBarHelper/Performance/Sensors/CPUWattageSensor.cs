using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class CPUWattageSensor : HardwareSensor
    {
        // Common sensor names for CPU package power across different CPUs
        private static readonly string[] SensorNames = new[]
        {
            "Package",           // AMD Ryzen (common)
            "CPU Package",       // Intel (common)
            "CPU Cores",         // Some Intel CPUs
            "Cores",             // Alternative
            "CPU Package Power", // Alternative
        };

        public CPUWattageSensor() : base(SensorNames, HardwareType.Cpu, SensorType.Power)
        {
        }
    }
}
