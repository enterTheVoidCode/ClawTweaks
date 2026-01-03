using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryFullChargeCapacitySensor : HardwareSensor
    {
        public BatteryFullChargeCapacitySensor() : base("Full Charged Capacity", HardwareType.Battery, SensorType.Energy)
        {
        }
    }
}
