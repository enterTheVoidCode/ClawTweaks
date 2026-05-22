using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class CPUWattageSensor : HardwareSensor
    {
        // Common sensor names for CPU package power across different CPUs.
        // IMPORTANT: "CPU Cores" and "Cores" are deliberately excluded.
        // On Intel Lunar Lake (Core Ultra 258V) LHM 0.9.6 exposes separate RAPL domains:
        //   "CPU Package" (11-25 W, entire CPU die) — this is what we want for "CPU TDP"
        //   "CPU Cores"   (3-8 W, compute cores only) — too low, not representative
        // Including "CPU Cores" would cause ProcessHardwareSensors to overwrite the
        // correct "CPU Package" value (because newIsNonZero==true triggers the update).
        private static readonly string[] SensorNames = new[]
        {
            "CPU Package",       // Intel RAPL MSR_PKG_ENERGY_STATUS (Lunar Lake, Raptor Lake, …)
            "Package",           // AMD Ryzen — package power (same RAPL concept, different name)
            "CPU Package Power", // Alternative Intel naming
        };

        public CPUWattageSensor() : base(SensorNames, HardwareType.Cpu, SensorType.Power)
        {
        }
    }
}
