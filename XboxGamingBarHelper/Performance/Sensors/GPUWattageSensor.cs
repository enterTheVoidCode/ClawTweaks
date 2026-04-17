using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUWattageSensor : HardwareSensor
    {
        // Sensor names: AMD="GPU Core", Nvidia="GPU Package", Intel="GPU Power"
        private static readonly string[] SensorNames = new[] { "GPU Core", "GPU Package", "GPU Power" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUWattageSensor() : base(SensorNames, GpuTypes, SensorType.Power)
        {
        }
    }
}
