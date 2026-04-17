using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class CPUClockSensor : HardwareSensor
    {
        // Common sensor names for CPU clock across different CPUs
        // Prefer average clocks for more accurate representation
        private static readonly string[] SensorNames = new[]
        {
            "Cores (Average)",  // Average of all cores (preferred)
            "Core Average",     // Alternative average name
            "Cores Average",    // Alternative average name
            "Core #1",          // Fallback to first core
            "CPU Core #1",      // Alternative
            "Core 1",           // Alternative format
        };

        public CPUClockSensor() : base(SensorNames, HardwareType.Cpu, SensorType.Clock)
        {
        }
    }
}
