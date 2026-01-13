using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    /// <summary>
    /// Sensor for VRM/SoC temperature. On AMD APUs like the Legion Go,
    /// this is the "GPU VR SoC" sensor which represents the voltage regulator
    /// temperature that the fan curve controller typically uses.
    /// </summary>
    internal class VRMTemperatureSensor : HardwareSensor
    {
        private static readonly string[] SensorNames = new[]
        {
            "GPU VR SoC",       // AMD APU VRM/SoC temperature (Legion Go)
            "VRM",              // Generic VRM
            "VRM MOS",          // VRM MOSFET
            "SoC",              // System on Chip
            "Chipset",          // Alternative name
        };

        public VRMTemperatureSensor() : base(SensorNames, new[] { HardwareType.GpuAmd, HardwareType.Motherboard }, SensorType.Temperature)
        {
        }
    }
}
