namespace XboxGamingBarHelper.Performance
{
    using LibreHardwareMonitor.Hardware;
    using System.Collections.Generic;
    using System.Linq;

    internal abstract class HardwareSensor
    {
        protected string[] sensorNames;
        public string SensorName
        {
            get { return sensorNames?.FirstOrDefault() ?? string.Empty; }
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
            : this(new[] { inSensorName }, inHardwareType, inSensorType)
        {
        }

        protected HardwareSensor(string[] inSensorNames, HardwareType inHardwareType, SensorType inSensorType)
        {
            sensorNames = inSensorNames;
            hardwareType = inHardwareType;
            sensorType = inSensorType;
        }

        /// <summary>
        /// Checks if the given sensor name matches any of the supported names.
        /// </summary>
        public bool MatchesSensorName(string name)
        {
            if (sensorNames == null || name == null)
                return false;

            foreach (var sensorName in sensorNames)
            {
                if (sensorName == name)
                    return true;
            }
            return false;
        }
    }
}
