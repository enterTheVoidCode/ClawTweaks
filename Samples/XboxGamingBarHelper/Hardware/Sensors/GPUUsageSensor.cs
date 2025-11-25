using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Hardware.Sensors
{
    internal class GPUUsageSensor : HardwareSensor
    {
        public GPUUsageSensor() : base("GPU Core", HardwareType.GpuAmd, SensorType.Load)
        {
        }
    }
}
