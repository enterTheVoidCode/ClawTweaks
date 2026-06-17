using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    /// <summary>
    /// Per-core CPU load (%) for a single physical core, matched by its exact LHM sensor name
    /// (e.g. "P-Core #1", "E-Core #2") — the same hybrid naming LHM uses for the per-core Clock
    /// sensors, but with <see cref="SensorType.Load"/>. Cores that don't expose a load sensor under
    /// this name simply stay at -1 (N/A) and the OSD omits the % for that core.
    /// </summary>
    internal class CPUCoreLoadSensor : HardwareSensor
    {
        public string CoreName { get; }

        public CPUCoreLoadSensor(string coreName)
            : base(coreName, HardwareType.Cpu, SensorType.Load)
        {
            CoreName = coreName;
        }
    }
}
