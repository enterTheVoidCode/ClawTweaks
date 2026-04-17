using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class CPUTemperatureSensor : HardwareSensor
    {
        // Common sensor names for CPU temperature across different CPUs
        private static readonly string[] SensorNames = new[]
        {
            "Core (Tctl/Tdie)", // AMD Ryzen (most common)
            "Tctl/Tdie",        // AMD Ryzen (alternative)
            "Tctl",             // AMD Ryzen (alternative)
            "Tdie",             // AMD Ryzen (alternative)
            "CPU Package",      // Intel (common)
            "Core Max",         // Some CPUs
            "Core Average",     // Some CPUs
            "Package",          // Alternative
        };

        public CPUTemperatureSensor() : base(SensorNames, HardwareType.Cpu, SensorType.Temperature)
        {
        }
    }
}
