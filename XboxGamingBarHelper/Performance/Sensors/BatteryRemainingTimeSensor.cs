using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class BatteryRemainingTimeSensor : HardwareSensor
    {
        public BatteryRemainingTimeSensor() : base("Remaining Time (Estimated)", HardwareType.Battery, SensorType.TimeSpan)
        {
        }
    }
}
