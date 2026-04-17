using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryFullChargeCapacitySensor : HardwareSensor
    {
        public BatteryFullChargeCapacitySensor() : base("Fully-Charged Capacity", HardwareType.Battery, SensorType.Energy)
        {
        }
    }
}
