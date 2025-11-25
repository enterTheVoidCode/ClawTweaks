using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class NetworkUploadSensor : HardwareSensor
    {
        public NetworkUploadSensor() : base("Data Uploaded", HardwareType.Network, SensorType.Throughput)
        {

        }
    }
}
