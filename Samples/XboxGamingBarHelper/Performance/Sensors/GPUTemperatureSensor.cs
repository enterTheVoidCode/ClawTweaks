using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUTemperatureSensor : HardwareSensor
    {
        public GPUTemperatureSensor() : base("GPU VR SoC", HardwareType.GpuAmd, SensorType.Temperature)
        {
        }
    }
}
