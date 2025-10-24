using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUClockSensor : HardwareSensor
    {
        public GPUClockSensor() : base("GPU Core", HardwareType.GpuAmd, SensorType.Clock)
        {
        }
    }
}
