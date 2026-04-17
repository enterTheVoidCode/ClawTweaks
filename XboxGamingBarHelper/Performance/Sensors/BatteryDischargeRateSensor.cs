using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryDischargeRateSensor : HardwareSensor
    {
        // LibreHardwareMonitor reports different sensor names depending on state:
        // - On AC: "Charge/Discharge Rate" (combined, value=0 when full)
        // - On Battery: "Discharge Rate" (separate sensor)
        private static readonly string[] SensorNames = new[] { "Discharge Rate", "Charge/Discharge Rate" };

        public BatteryDischargeRateSensor() : base(SensorNames, HardwareType.Battery, SensorType.Power)
        {
        }
    }
}
