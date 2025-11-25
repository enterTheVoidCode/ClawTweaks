namespace XboxGamingBarHelper.Hardware
{
    using LibreHardwareMonitor.Hardware;

    internal abstract class HardwareSensor
    {
        protected string sensorName;
        public string SensorName
        {
            get { return sensorName; }
        }

        protected HardwareType hardwareType;
        public HardwareType HardwareType
        {
            get { return hardwareType; }
        }

        protected SensorType sensorType;
        public SensorType SensorType
        {
            get { return sensorType; }
        }

        public float Value { get; set; }

        protected HardwareSensor(string inSensorName, HardwareType inHardwareType, SensorType inSensorType)
        {
            sensorName = inSensorName;
            hardwareType = inHardwareType;
            sensorType = inSensorType;
        }
    }
}
