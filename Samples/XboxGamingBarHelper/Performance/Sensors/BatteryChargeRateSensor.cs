using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryChargeRateSensor : HardwareSensor
    {
        // LibreHardwareMonitor reports different sensor names depending on state:
        // - Charging: "Charge Rate" (separate sensor)
        // - On AC idle: "Charge/Discharge Rate" (combined, value=0 when full)
        private static readonly string[] SensorNames = new[] { "Charge Rate", "Charge/Discharge Rate" };

        public BatteryChargeRateSensor() : base(SensorNames, HardwareType.Battery, SensorType.Power)
        {
        }
    }
}
