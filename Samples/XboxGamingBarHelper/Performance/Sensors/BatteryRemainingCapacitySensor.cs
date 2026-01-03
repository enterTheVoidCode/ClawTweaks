using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryRemainingCapacitySensor : HardwareSensor
    {
        public BatteryRemainingCapacitySensor() : base("Remaining Capacity", HardwareType.Battery, SensorType.Energy)
        {
        }
    }
}
