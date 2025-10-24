using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUWattageSensor : HardwareSensor
    {
        public GPUWattageSensor() : base("GPU Core", HardwareType.GpuAmd, SensorType.Power)
        {
        }
    }
}
