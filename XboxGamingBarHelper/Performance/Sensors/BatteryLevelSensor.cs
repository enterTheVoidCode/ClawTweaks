using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryLevelSensor : HardwareSensor
    {
        public BatteryLevelSensor() : base("Charge Level", HardwareType.Battery, SensorType.Level)
        {
        }
    }
}
