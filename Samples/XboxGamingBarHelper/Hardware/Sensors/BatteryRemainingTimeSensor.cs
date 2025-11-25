using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Hardware.Sensors
{
    internal class BatteryRemainingTimeSensor : HardwareSensor
    {
        public BatteryRemainingTimeSensor() : base("Remaining Time (Estimated)", HardwareType.Battery, SensorType.TimeSpan)
        {
        }
    }
}
