using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUMemoryUsedSensor : HardwareSensor
    {
        public GPUMemoryUsedSensor() : base("GPU Memory Used", HardwareType.GpuAmd, SensorType.SmallData)
        {
        }
    }
}
