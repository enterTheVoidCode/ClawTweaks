namespace XboxGamingBarHelper.Performance
{
    using Core;
    using LibreHardwareMonitor.Hardware;

    internal abstract class HardwareSensor : Sensor
    {
        protected ISensor sensor;
        public ISensor Sensor => sensor;

        protected HardwareSensor()
        {
            name = "Unknown Hardware Sensor";
            sensor = null;
        }

        public HardwareSensor(string name, ISensor sensor) : base(name)
        {
            this.sensor = sensor;
        }
    }
}
