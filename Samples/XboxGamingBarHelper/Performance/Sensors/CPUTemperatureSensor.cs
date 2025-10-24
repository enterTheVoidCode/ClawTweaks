using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class CPUTemperatureSensor : HardwareSensor
    {
        public CPUTemperatureSensor() : base("Core (Tctl/Tdie)", HardwareType.Cpu, SensorType.Temperature)
        {
        }
    }
}
