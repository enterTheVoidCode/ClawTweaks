using System;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Gyro source adapter for MSI Claw A1M / A2VM (Intel Lunar Lake).
    ///
    /// Wraps <see cref="WindowsSensorGyroSourceAdapter"/> and applies device-specific
    /// axis remapping ported 1:1 from HandheldCompanion ClawA1M.cs:
    ///
    ///   GyrometerAxis     = (1.0, 1.0, -1.0)   — Z negated
    ///   GyrometerAxisSwap = { X→X, Y→Z, Z→Y }  — Y and Z axes swapped
    ///
    ///   AccelerometerAxis     = (-1.0, -1.0, 1.0)  — X and Z negated
    ///   AccelerometerAxisSwap = { X→X, Y→Z, Z→Y }  — Y and Z axes swapped
    ///
    /// Combined per output axis:
    ///   gyroX_out  = rawX *  1.0   (X→X,  factor +1)
    ///   gyroY_out  = rawZ *  1.0   (Z→Y,  factor +1)
    ///   gyroZ_out  = rawY * -1.0   (Y→Z,  factor -1)
    ///
    ///   accelX_out = rawX * -1.0   (X→X,  factor -1)
    ///   accelY_out = rawZ * -1.0   (Z→Y,  factor -1)
    ///   accelZ_out = rawY *  1.0   (Y→Z,  factor +1)
    ///
    /// Note: SetMotionStatus (HC command 0x2F) is not required on the Windows Sensor
    /// path — the Windows HID IMU driver enables the sensor automatically. If the
    /// Windows gyrometer is unavailable on a particular firmware, add a
    /// SendSetMotionStatus(true) call from ClawButtonMonitor before Start().
    /// </summary>
    internal sealed class ClawGyroSourceAdapter : IGyroSourceAdapter
    {
        private readonly WindowsSensorGyroSourceAdapter inner;

        public string Name => "MSI Claw Internal Gyro";

        public event Action<GyroSample> SampleReady;

        public ClawGyroSourceAdapter()
        {
            inner = new WindowsSensorGyroSourceAdapter("MSI Claw Internal Gyro");
            inner.SampleReady += OnInnerSampleReady;
        }

        public bool Start() => inner.Start();

        public void Stop() => inner.Stop();

        private void OnInnerSampleReady(GyroSample raw)
        {
            SampleReady?.Invoke(Remap(raw));
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            if (!inner.TryGetLatestSample(out GyroSample raw))
            {
                sample = default;
                return false;
            }

            sample = Remap(raw);
            return true;
        }

        /// <summary>
        /// 1:1 axis remapping from HC ClawA1M.cs
        /// Swap: physical Y → logical Z, physical Z → logical Y
        /// Scale: gyro Z negated; accel X and Y negated
        /// </summary>
        private static GyroSample Remap(GyroSample raw)
        {
            return new GyroSample(
                gyroXDegPerSecond:  raw.GyroXDegPerSecond,          // X→X  ×+1
                gyroYDegPerSecond:  raw.GyroZDegPerSecond,          // Z→Y  ×+1
                gyroZDegPerSecond: -raw.GyroYDegPerSecond,          // Y→Z  ×-1
                accelXG:           -raw.AccelXG,                    // X→X  ×-1
                accelYG:           -raw.AccelZG,                    // Z→Y  ×-1
                accelZG:            raw.AccelYG,                    // Y→Z  ×+1
                timestampTicksUtc:  raw.TimestampTicksUtc);
        }

        public void Dispose()
        {
            inner.SampleReady -= OnInnerSampleReady;
            inner.Dispose();
        }
    }
}
