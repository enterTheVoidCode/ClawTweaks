using System;
using NLog;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Continuously estimates the resting gyro bias on each axis and exposes a
    /// bias-corrected sample. Without this, IMUs with a few °/s residual bias
    /// (typical post-factory-calibration drift, exacerbated by temperature) leak
    /// past the per-axis deadzone and produce a small but persistent stick offset
    /// — felt by users as "the camera spins by itself" in Always-On gyro modes.
    ///
    /// Approach: classify a sample as "stationary" when the rolling magnitude
    /// stays under <see cref="StationaryThresholdDegPerSec"/> for at least
    /// <see cref="StationaryDwellTicks"/>. While stationary, blend the raw
    /// reading into the bias estimate via EMA (alpha = <see cref="BiasUpdateAlpha"/>).
    /// Once at least one valid bias estimate exists, subtract it from every
    /// outgoing sample. While moving, the bias is held — we only refine it
    /// when the user is actually at rest.
    ///
    /// Cheap to run; O(1) state per axis. Safe to call from poll-loop threads.
    /// </summary>
    internal sealed class GyroBiasEstimator
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Master kill switch. vvalente30 reported the controller working fine on
        // build 2137 (issue #79) which predated this estimator entirely. After
        // 319fd38 wired it in, "uncontrollable drift" started showing up — likely
        // because the strict stationary gate never converges on a handheld and
        // the strict-but-uncalibrated state still subtly interacts with the
        // stick math. Disabling correction reverts to 2137 behavior; the rest
        // of the class stays in place so we can iterate on a better calibration
        // strategy (force-snap, longer windows, button-triggered "calibrate now")
        // without re-adding the call sites.
        private const bool DisableBiasCorrection = true;

        // Below this magnitude on each axis, we consider the controller "still."
        // Picked above industry-typical ICM/BMI bias (~5°/s worst case under thermal
        // drift) but below typical hand-held tremor (~10-15°/s).
        private const float StationaryThresholdDegPerSec = 8.0f;

        // Sample must look stationary continuously for this long before bias
        // updates. Avoids learning bias during deliberate slow camera pans.
        // 500ms @ ~64Hz = ~32 samples.
        private static readonly long StationaryDwellTicks = TimeSpan.TicksPerMillisecond * 500;

        // EMA blend factor for new bias readings. 0.05 means each stationary
        // sample shifts the bias estimate by 5%; ~60 samples (≈1s @ 64Hz) to
        // converge on the true bias. Slow enough that brief "almost still"
        // moments don't pull the estimate around, fast enough that thermal
        // drift over minutes is tracked.
        private const float BiasUpdateAlpha = 0.05f;

        // After this many ticks of "no sample seen at all" we drop accumulated
        // stationary state — covers backend swaps and pause/resume gaps.
        private static readonly long IdleResetTicks = TimeSpan.TicksPerSecond * 3;

        private float biasX;
        private float biasY;
        private float biasZ;
        private bool biasInitialized;

        // When the most recent stationary streak began. -1 means "currently moving."
        private long stationarySinceTicks = -1;
        private long lastSampleTicks = -1;

        // Diagnostic snapshots — exposed read-only for the periodic stats logger
        // so triage of "stick still drifts after auto-bias landed" reports doesn't
        // need code changes to inspect.
        public bool IsCalibrated => biasInitialized;
        public float BiasXDegPerSec => biasX;
        public float BiasYDegPerSec => biasY;
        public float BiasZDegPerSec => biasZ;

        /// <summary>
        /// Force a manual zero — captures the next stationary period quickly by
        /// resetting state. Hooked up to the future "Calibrate now" button.
        /// </summary>
        public void Reset()
        {
            biasX = 0f;
            biasY = 0f;
            biasZ = 0f;
            biasInitialized = false;
            stationarySinceTicks = -1;
            lastSampleTicks = -1;
        }

        /// <summary>
        /// Subtract the current bias estimate from <paramref name="sample"/> and
        /// return the corrected reading. Updates the bias estimate as a side
        /// effect when the controller looks stationary. Accelerometer fields
        /// pass through untouched — bias correction only applies to gyro axes.
        /// </summary>
        public GyroSample Correct(GyroSample sample)
        {
            // Disabled: vvalente30 had clean gyro on 2137 with no estimator;
            // 319fd38 added it and his next test reported uncontrollable drift.
            // Returning the raw sample here matches 2137 behavior. Keep the
            // implementation below intact for when we revisit calibration.
            if (DisableBiasCorrection)
            {
                return sample;
            }

            long nowTicks = sample.TimestampTicksUtc > 0 ? sample.TimestampTicksUtc : DateTime.UtcNow.Ticks;

            // Long gap → reset stationary streak; the user may have docked /
            // undocked / paused, so what looked still 3 seconds ago says nothing
            // about now.
            if (lastSampleTicks > 0 && (nowTicks - lastSampleTicks) > IdleResetTicks)
            {
                stationarySinceTicks = -1;
            }
            lastSampleTicks = nowTicks;

            float rawX = sample.GyroXDegPerSecond;
            float rawY = sample.GyroYDegPerSecond;
            float rawZ = sample.GyroZDegPerSecond;

            // "Stationary" check uses bias-corrected magnitude when we already
            // have an estimate (avoids a high constant bias permanently looking
            // like motion); falls back to raw magnitude on the first run before
            // bias is initialized.
            float testX = biasInitialized ? rawX - biasX : rawX;
            float testY = biasInitialized ? rawY - biasY : rawY;
            float testZ = biasInitialized ? rawZ - biasZ : rawZ;
            bool axisQuiet =
                Math.Abs(testX) < StationaryThresholdDegPerSec &&
                Math.Abs(testY) < StationaryThresholdDegPerSec &&
                Math.Abs(testZ) < StationaryThresholdDegPerSec;

            if (axisQuiet)
            {
                if (stationarySinceTicks < 0)
                {
                    stationarySinceTicks = nowTicks;
                }
                else if ((nowTicks - stationarySinceTicks) >= StationaryDwellTicks)
                {
                    if (!biasInitialized)
                    {
                        // First pass: snap to the current sample so we're
                        // immediately useful instead of needing 60 EMA steps.
                        biasX = rawX;
                        biasY = rawY;
                        biasZ = rawZ;
                        biasInitialized = true;
                        Logger.Info($"GyroBiasEstimator: initial bias snapshot X={biasX:F2} Y={biasY:F2} Z={biasZ:F2} deg/s");
                    }
                    else
                    {
                        biasX += (rawX - biasX) * BiasUpdateAlpha;
                        biasY += (rawY - biasY) * BiasUpdateAlpha;
                        biasZ += (rawZ - biasZ) * BiasUpdateAlpha;
                    }
                }
            }
            else
            {
                stationarySinceTicks = -1;
            }

            // Subtract bias whether or not we updated it this tick. Pre-init,
            // bias is zero so this is a no-op.
            return new GyroSample(
                rawX - biasX,
                rawY - biasY,
                rawZ - biasZ,
                sample.AccelXG,
                sample.AccelYG,
                sample.AccelZG,
                sample.TimestampTicksUtc);
        }
    }
}
