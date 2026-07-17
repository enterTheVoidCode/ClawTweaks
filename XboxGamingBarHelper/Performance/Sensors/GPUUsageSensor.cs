using LibreHardwareMonitor.Hardware;

namespace XboxGamingBarHelper.Performance.Sensors
{
    internal class GPUUsageSensor : HardwareSensor
    {
        // Sensor names: AMD="GPU Core", Nvidia="GPU Core", Intel="D3D 3D" or "GPU"
        private static readonly string[] SensorNames = new[] { "GPU Core", "D3D 3D", "GPU" };
        private static readonly HardwareType[] GpuTypes = new[] { HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel };

        public GPUUsageSensor() : base(SensorNames, GpuTypes, SensorType.Load)
        {
        }

        /// <summary>
        /// Clamps to 0..100. LibreHardwareMonitor computes the Intel D3D node load as
        /// <c>100f * runningTimeDiff / timeDiff</c> (IntelIntegratedGpu.cs) with no upper bound, so a
        /// sample window where the engine's reported busy time outruns wall-clock — parallel
        /// sub-engines inside one node, jitter between the two queries, a jump after resume — briefly
        /// reports over 100%. The average tracks HWiNFO; only single windows overshoot.
        ///
        /// Not caused by the 0.9.6 -> 0.9.7-pre708 upgrade: no Intel/D3D file changed between those
        /// (only AmdGpu/NvidiaGpu/NvidiaGroup did), so 0.9.6 behaved the same. A load percentage above
        /// 100 is meaningless to every consumer here (tile, OSD), so it is capped rather than shown.
        ///
        /// Only the upper bound is capped. Negative values must pass through untouched: -1 is the
        /// "no reading" marker PerformanceManager seeds pendingValues with, and the widget gates on
        /// `gpuUse >= 0` to decide between showing a percentage and showing "--". Clamping it to 0
        /// would turn "unknown" into a fabricated "0%".
        /// </summary>
        public override float Value
        {
            get => base.Value;
            set => base.Value = value > 100f ? 100f : value;
        }
    }
}
