using System;
using NLog;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Gyro source adapter for MSI Claw A1M / A2VM (Intel Lunar Lake) and
    /// Claw 8 AI+ EX (Intel Panther Lake).
    ///
    /// Acquisition is a two-step fallback, decided at Start():
    ///   1. <see cref="WindowsSensorGyroSourceAdapter"/> — the standard WinRT
    ///      Gyrometer path. Works on A1M/A2VM, where the sensor stack publishes a
    ///      normal Gyrometer. Fails fast (GetDefault() == null) on the EX.
    ///   2. <see cref="CustomSensorGyroSourceAdapter"/> — the EX's IMU is published
    ///      ONLY as HID custom sensors ("Physical Gyrometer"), invisible to every
    ///      standard sensor API; see that adapter's header and
    ///      docs/hardware/CLAW8_EX_PORT_LOG.md (2026-07-05) for the root cause.
    /// Runtime fallback (instead of keying on the detected device config) keeps
    /// A2VM on its proven path with zero behavior change and needs no plumbing.
    ///
    /// Both paths then get device-specific axis remapping ported 1:1 from
    /// HandheldCompanion ClawA1M.cs:
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
    /// NOTE (EX): the CustomSensor path bypasses whatever orientation normalization
    /// the standard sensor stack applies, so applying the A1M remap there is a
    /// same-vendor-chassis starting hypothesis, NOT a measurement. Axis directions
    /// and signs on the EX are pending physical verification (Phase 5 validation
    /// rows 6/7 in docs/PORT_PLAN_CLAW_8_AI_PLUS_EX.md); if aim/cursor moves the
    /// wrong way on the EX, this remap is the first suspect.
    ///
    /// Note: SetMotionStatus (HC command 0x2F) is not required on either path —
    /// the sensors deliver without it (verified on A2VM for the WinRT path and on
    /// the EX for the CustomSensor path via Diagnostics/Claw8EXProbes).
    /// </summary>
    internal sealed class ClawGyroSourceAdapter : IGyroSourceAdapter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private IGyroSourceAdapter inner;

        public string Name => "MSI Claw Internal Gyro";

        public event Action<GyroSample> SampleReady;

        /// <summary>Which underlying source Start() settled on — for diagnostics/logs.</summary>
        public string ActiveSourceName => inner?.Name ?? "(not started)";

        public bool Start()
        {
            if (inner != null)
            {
                return inner.Start();
            }

            var winrt = new WindowsSensorGyroSourceAdapter("MSI Claw Internal Gyro (WinRT)");
            if (winrt.Start())
            {
                Adopt(winrt);
                return true;
            }
            winrt.Dispose();

            var custom = new CustomSensorGyroSourceAdapter("MSI Claw Internal Gyro (CustomSensor)");
            if (custom.Start())
            {
                Logger.Info("ClawGyroSourceAdapter: WinRT Gyrometer unavailable — using CustomSensor path (Claw 8 EX)");
                Adopt(custom);
                return true;
            }
            custom.Dispose();

            Logger.Warn("ClawGyroSourceAdapter: no gyro source available (WinRT and CustomSensor paths both failed)");
            return false;
        }

        private void Adopt(IGyroSourceAdapter source)
        {
            inner = source;
            inner.SampleReady += OnInnerSampleReady;
        }

        public void Stop() => inner?.Stop();

        private void OnInnerSampleReady(GyroSample raw)
        {
            SampleReady?.Invoke(Remap(raw));
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            if (inner == null || !inner.TryGetLatestSample(out GyroSample raw))
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
            if (inner == null)
            {
                return;
            }

            inner.SampleReady -= OnInnerSampleReady;
            inner.Dispose();
        }
    }
}
