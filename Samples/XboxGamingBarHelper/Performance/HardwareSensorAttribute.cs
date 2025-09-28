using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.Performance
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class HardwareSensorAttribute : Attribute
    {
        public string Name { get; }
        public HardwareType HardwareType { get; }
        public SensorType SensorType { get; }

        public HardwareSensorAttribute(string name, HardwareType hardwareType, SensorType sensorType)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            HardwareType = hardwareType;
            SensorType = sensorType;
        }
    }
}
