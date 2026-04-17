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

        protected HardwareType[] hardwareTypes;
        public HardwareType HardwareType
        {
            get { return hardwareTypes?.FirstOrDefault() ?? HardwareType.Cpu; }
        }

        protected SensorType sensorType;
        public SensorType SensorType
        {
            get { return sensorType; }
        }

        public float Value { get; set; }

        protected HardwareSensor(string inSensorName, HardwareType inHardwareType, SensorType inSensorType)
            : this(new[] { inSensorName }, new[] { inHardwareType }, inSensorType)
        {
        }

        protected HardwareSensor(string[] inSensorNames, HardwareType inHardwareType, SensorType inSensorType)
            : this(inSensorNames, new[] { inHardwareType }, inSensorType)
        {
        }

        protected HardwareSensor(string inSensorName, HardwareType[] inHardwareTypes, SensorType inSensorType)
            : this(new[] { inSensorName }, inHardwareTypes, inSensorType)
        {
        }

        protected HardwareSensor(string[] inSensorNames, HardwareType[] inHardwareTypes, SensorType inSensorType)
        {
            sensorNames = inSensorNames;
            hardwareTypes = inHardwareTypes;
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

        /// <summary>
        /// Checks if the given hardware type matches any of the supported types.
        /// </summary>
        public bool MatchesHardwareType(HardwareType type)
        {
            if (hardwareTypes == null)
                return false;

            foreach (var hwType in hardwareTypes)
            {
                if (hwType == type)
                    return true;
            }
            return false;
        }
    }
}
