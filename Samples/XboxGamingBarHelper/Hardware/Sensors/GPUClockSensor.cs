using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Hardware.Sensors
{
    internal class GPUClockSensor : HardwareSensor
    {
        public GPUClockSensor() : base("GPU Core", HardwareType.GpuAmd, SensorType.Clock)
        {
        }
    }
}
