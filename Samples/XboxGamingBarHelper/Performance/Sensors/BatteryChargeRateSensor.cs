using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryChargeRateSensor : HardwareSensor
    {
        public BatteryChargeRateSensor() : base("Charge Rate", HardwareType.Battery, SensorType.Power)
        {
        }
    }
}
