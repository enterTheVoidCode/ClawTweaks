using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUTemperatureSensor : HardwareSensor
    {
        // Sensor names: AMD="GPU VR SoC" or "GPU Core", Nvidia="GPU Core", Intel="GPU Core"
        private static readonly string[] SensorNames = new[] { "GPU VR SoC", "GPU Core" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUTemperatureSensor() : base(SensorNames, GpuTypes, SensorType.Temperature)
        {
        }
    }
}
