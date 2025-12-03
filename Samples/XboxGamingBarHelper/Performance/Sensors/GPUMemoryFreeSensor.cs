using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUMemoryFreeSensor : HardwareSensor
    {
        public GPUMemoryFreeSensor() : base("GPU Memory Free", HardwareType.GpuAmd, SensorType.SmallData)
        {
        }
    }
}
