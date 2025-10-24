using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryDischargeRateSensor : HardwareSensor
    {
        public BatteryDischargeRateSensor() : base("Discharge Rate", HardwareType.Battery, SensorType.Power)
        {
        }
    }
}
