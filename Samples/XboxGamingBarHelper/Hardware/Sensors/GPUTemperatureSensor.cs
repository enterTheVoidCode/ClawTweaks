using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Hardware.Sensors
{
    internal class GPUTemperatureSensor : HardwareSensor
    {
        public GPUTemperatureSensor() : base("GPU VR SoC", HardwareType.GpuAmd, SensorType.Temperature)
        {
        }
    }
}
