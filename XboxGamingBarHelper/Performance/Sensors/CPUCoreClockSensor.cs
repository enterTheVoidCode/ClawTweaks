using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    /// <summary>
    /// Per-core CPU clock for a single physical core, matched by its exact LHM sensor name
    /// (e.g. "P-Core #1", "E-Core #2"). On Intel hybrid CPUs (Lunar Lake / Core Ultra) LHM exposes
    /// one Clock sensor per core under these names — same source as the other CPU stats. Cores that
    /// don't exist on a given SKU simply never get a matching sensor, so their value stays -1 (N/A)
    /// and the OSD omits them.
    /// </summary>
    internal class CPUCoreClockSensor : HardwareSensor
    {
        public string CoreName { get; }

        public CPUCoreClockSensor(string coreName)
            : base(coreName, HardwareType.Cpu, SensorType.Clock)
        {
            CoreName = coreName;
        }
    }
}
