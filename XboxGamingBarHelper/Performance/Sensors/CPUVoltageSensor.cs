using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    /// <summary>
    /// Aggregate CPU core voltage (Vcore) in volts. LHM exposes this as the single "CPU Core"
    /// Voltage sensor (distinct from the per-core "P-Core #N" / "E-Core #N" Voltage sensors, which
    /// we deliberately don't surface). Used by the Full OSD preset to show Vcore in parentheses
    /// after the CPU temperature. Stays at -1 (N/A) and is omitted when the CPU exposes no Vcore.
    /// </summary>
    internal class CPUVoltageSensor : HardwareSensor
    {
        // Intel = "CPU Core"; AMD typically = "Core (SVI2 TFN)" / "CPU Core".
        private static readonly string[] SensorNames = new[] { "CPU Core", "Core (SVI2 TFN)" };

        public CPUVoltageSensor() : base(SensorNames, HardwareType.Cpu, SensorType.Voltage)
        {
        }
    }
}
