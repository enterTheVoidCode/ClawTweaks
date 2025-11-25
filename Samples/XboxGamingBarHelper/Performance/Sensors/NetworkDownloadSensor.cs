using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class NetworkDownloadSensor : HardwareSensor
    {
        public NetworkDownloadSensor() : base("Data Downloaded", HardwareType.Network, SensorType.Throughput)
        {

        }
    }
}
