using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Hardware.Sensors
{
    internal class BatteryChargeRateSensor : HardwareSensor
    {
        public BatteryChargeRateSensor() : base("Charge Rate", HardwareType.Battery, SensorType.Power)
        {
        }
    }
}
