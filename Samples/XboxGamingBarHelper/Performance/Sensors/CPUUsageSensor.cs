using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.Performance.Sensors
{
    [HardwareSensor("CPU Total", HardwareType.Cpu, SensorType.Load)]
    internal class CPUUsageSensor : HardwareSensor
    {

    }
}
