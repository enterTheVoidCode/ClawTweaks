using System;
using System.Threading;
using NLog;
using Windows.Devices.Sensors;
using XboxGamingBarHelper.Labs;

namespace XboxGamingBarHelper.ControllerEmulation
{
    internal readonly struct GyroSample
    {
        public readonly float GyroXDegPerSecond;
        public readonly float GyroYDegPerSecond;
        public readonly float GyroZDegPerSecond;
        public readonly float AccelXG;
        public readonly float AccelYG;
        public readonly float AccelZG;
        public readonly long TimestampTicksUtc;

        public GyroSample(
            float gyroXDegPerSecond,
            float gyroYDegPerSecond,
            float gyroZDegPerSecond,
            float accelXG,
            float accelYG,
            float accelZG,
            long timestampTicksUtc)
        {
            GyroXDegPerSecond = gyroXDegPerSecond;
            GyroYDegPerSecond = gyroYDegPerSecond;
            GyroZDegPerSecond = gyroZDegPerSecond;
            AccelXG = accelXG;
            AccelYG = accelYG;
            AccelZG = accelZG;
            TimestampTicksUtc = timestampTicksUtc;
        }
    }

    internal interface IGyroSourceAdapter : IDisposable
    {
        string Name { get; }

        bool Start();

        void Stop();

        bool TryGetLatestSample(out GyroSample sample);
    }

    internal sealed class WindowsSensorGyroSourceAdapter : IGyroSourceAdapter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const uint FallbackReportIntervalMs = 4;
        private const int WarmupTimeoutMs = 800;
        private readonly string name;
        private readonly object sampleLock = new object();
        private Gyrometer gyrometer;
        private Accelerometer accelerometer;
        private bool started;
        private uint originalGyroReportInterval;
        private uint originalAccelReportInterval;
        private long lastGyroTimestampTicksUtc;
        private bool hasAccelSample;
        private float latestAccelX;
        private float latestAccelY;
        private float latestAccelZ;
        private GyroSample latestSample;
        private bool hasUnreadSample;
        private ManualResetEventSlim firstGyroSampleEvent;

        public string Name => name;

        public WindowsSensorGyroSourceAdapter(string name)
        {
            this.name = name;
        }

        public bool Start()
        {
            try
            {
                gyrometer = Gyrometer.GetDefault();
                accelerometer = Accelerometer.GetDefault();
                started = gyrometer != null;

                if (!started)
                {
                    Logger.Warn($"Gyro source '{name}' unavailable: Windows gyrometer not found");
                    return false;
                }

                lock (sampleLock)
                {
                    lastGyroTimestampTicksUtc = 0;
                    hasAccelSample = false;
                    latestAccelX = 0.0f;
                    latestAccelY = 0.0f;
                    latestAccelZ = 0.0f;
                    latestSample = default;
                    hasUnreadSample = false;
                }

                originalGyroReportInterval = gyrometer.ReportInterval;
                uint gyroInterval = gyrometer.MinimumReportInterval > 0
                    ? gyrometer.MinimumReportInterval
                    : FallbackReportIntervalMs;
                gyrometer.ReportInterval = gyroInterval;

                if (accelerometer != null)
                {
                    originalAccelReportInterval = accelerometer.ReportInterval;
                    uint accelInterval = accelerometer.MinimumReportInterval > 0
                        ? accelerometer.MinimumReportInterval
                        : FallbackReportIntervalMs;
                    accelerometer.ReportInterval = accelInterval;
                }
                else
                {
                    originalAccelReportInterval = 0;
                }

                firstGyroSampleEvent = new ManualResetEventSlim(false);
                gyrometer.ReadingChanged += OnGyrometerReadingChanged;
                if (accelerometer != null)
                {
                    accelerometer.ReadingChanged += OnAccelerometerReadingChanged;
                }

                if (!TryWarmupGyroReading(out long firstTimestampTicksUtc))
                {
                    Logger.Warn($"Gyro source '{name}' warmup failed: no readings received within {WarmupTimeoutMs}ms");
                    Stop();
                    return false;
                }

                lastGyroTimestampTicksUtc = firstTimestampTicksUtc;
                Logger.Info($"Gyro source '{name}' started (gyro interval: {gyrometer.ReportInterval}ms, accelerometer available: {accelerometer != null})");
                return true;
            }
            catch (Exception ex)
            {
                started = false;
                Logger.Warn($"Gyro source '{name}' failed to start: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            if (gyrometer != null)
            {
                try
                {
                    gyrometer.ReadingChanged -= OnGyrometerReadingChanged;
                }
                catch
                {
                    // Ignore event detach failures.
                }

                try
                {
                    gyrometer.ReportInterval = originalGyroReportInterval;
                }
                catch
                {
                    // Ignore report interval restore failures.
                }
            }

            if (accelerometer != null)
            {
                try
                {
                    accelerometer.ReadingChanged -= OnAccelerometerReadingChanged;
                }
                catch
                {
                    // Ignore event detach failures.
                }

                try
                {
                    accelerometer.ReportInterval = originalAccelReportInterval;
                }
                catch
                {
                    // Ignore report interval restore failures.
                }
            }

            started = false;
            gyrometer = null;
            accelerometer = null;
            firstGyroSampleEvent?.Dispose();
            firstGyroSampleEvent = null;

            lock (sampleLock)
            {
                lastGyroTimestampTicksUtc = 0;
                hasAccelSample = false;
                latestAccelX = 0.0f;
                latestAccelY = 0.0f;
                latestAccelZ = 0.0f;
                latestSample = default;
                hasUnreadSample = false;
            }
        }

        private bool TryWarmupGyroReading(out long timestampTicksUtc)
        {
            timestampTicksUtc = 0;
            if (gyrometer == null)
            {
                return false;
            }

            if (firstGyroSampleEvent != null && firstGyroSampleEvent.Wait(WarmupTimeoutMs))
            {
                lock (sampleLock)
                {
                    if (lastGyroTimestampTicksUtc > 0)
                    {
                        timestampTicksUtc = lastGyroTimestampTicksUtc;
                        return true;
                    }
                }
            }

            // Fallback for devices where event delivery is delayed.
            var reading = gyrometer.GetCurrentReading();
            if (reading == null)
            {
                return false;
            }

            long ticks = reading.Timestamp.UtcDateTime.Ticks;
            if (ticks <= 0)
            {
                ticks = DateTime.UtcNow.Ticks;
            }

            float accelX = 0.0f;
            float accelY = 0.0f;
            float accelZ = 0.0f;
            var accelReading = accelerometer?.GetCurrentReading();
            if (accelReading != null)
            {
                accelX = (float)accelReading.AccelerationX;
                accelY = (float)accelReading.AccelerationY;
                accelZ = (float)accelReading.AccelerationZ;
            }

            lock (sampleLock)
            {
                lastGyroTimestampTicksUtc = ticks;
                latestSample = new GyroSample(
                    (float)reading.AngularVelocityX,
                    (float)reading.AngularVelocityY,
                    (float)reading.AngularVelocityZ,
                    accelX,
                    accelY,
                    accelZ,
                    ticks);
                hasUnreadSample = true;
            }

            timestampTicksUtc = ticks;
            return true;
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            sample = default;
            if (!started || gyrometer == null)
            {
                return false;
            }

            lock (sampleLock)
            {
                if (!hasUnreadSample)
                {
                    return false;
                }

                sample = latestSample;
                hasUnreadSample = false;
                return true;
            }
        }

        private void OnAccelerometerReadingChanged(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
        {
            if (!started)
            {
                return;
            }

            try
            {
                var reading = args?.Reading;
                if (reading == null)
                {
                    return;
                }

                lock (sampleLock)
                {
                    latestAccelX = (float)reading.AccelerationX;
                    latestAccelY = (float)reading.AccelerationY;
                    latestAccelZ = (float)reading.AccelerationZ;
                    hasAccelSample = true;
                }
            }
            catch
            {
                // Ignore transient sensor callback failures.
            }
        }

        private void OnGyrometerReadingChanged(Gyrometer sender, GyrometerReadingChangedEventArgs args)
        {
            if (!started)
            {
                return;
            }

            try
            {
                var reading = args?.Reading;
                if (reading == null)
                {
                    return;
                }

                long timestampTicksUtc = reading.Timestamp.UtcDateTime.Ticks;
                if (timestampTicksUtc <= 0)
                {
                    timestampTicksUtc = DateTime.UtcNow.Ticks;
                }

                lock (sampleLock)
                {
                    if (timestampTicksUtc <= lastGyroTimestampTicksUtc)
                    {
                        return;
                    }

                    lastGyroTimestampTicksUtc = timestampTicksUtc;

                    float accelX = hasAccelSample ? latestAccelX : 0.0f;
                    float accelY = hasAccelSample ? latestAccelY : 0.0f;
                    float accelZ = hasAccelSample ? latestAccelZ : 0.0f;
                    latestSample = new GyroSample(
                        (float)reading.AngularVelocityX,
                        (float)reading.AngularVelocityY,
                        (float)reading.AngularVelocityZ,
                        accelX,
                        accelY,
                        accelZ,
                        timestampTicksUtc);
                    hasUnreadSample = true;
                }

                firstGyroSampleEvent?.Set();
            }
            catch
            {
                // Ignore transient sensor callback failures.
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal interface ILegionControllerGyroSource { }

    internal sealed class LegionControllerGyroSourceAdapter : IGyroSourceAdapter, ILegionControllerGyroSource
    {
        private readonly bool useLeftController;
        private bool started;

        public string Name => useLeftController ? "Legion Left Controller Gyro" : "Legion Right Controller Gyro";

        public LegionControllerGyroSourceAdapter(bool useLeftController)
        {
            this.useLeftController = useLeftController;
        }

        public bool Start()
        {
            started = true;
            return true;
        }

        public void Stop()
        {
            started = false;
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            sample = default;
            if (!started)
            {
                return false;
            }

            if (!LegionButtonMonitor.TryGetLatestGyroSample(useLeftController, out LegionGyroSample parsed))
            {
                return false;
            }

            sample = new GyroSample(
                parsed.GyroXDegPerSecond,
                parsed.GyroYDegPerSecond,
                parsed.GyroZDegPerSecond,
                parsed.AccelXG,
                parsed.AccelYG,
                parsed.AccelZG,
                parsed.TimestampTicksUtc);
            return true;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal sealed class LegionControllerMixedGyroSourceAdapter : IGyroSourceAdapter, ILegionControllerGyroSource
    {
        private bool started;

        public string Name => "Legion Controller Gyro (Mixed)";

        public bool Start()
        {
            started = true;
            return true;
        }

        public void Stop()
        {
            started = false;
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            sample = default;
            if (!started)
            {
                return false;
            }

            bool hasLeft = LegionButtonMonitor.TryGetLatestGyroSample(true, out LegionGyroSample left);
            bool hasRight = LegionButtonMonitor.TryGetLatestGyroSample(false, out LegionGyroSample right);

            if (hasLeft && hasRight)
            {
                sample = new GyroSample(
                    (left.GyroXDegPerSecond + right.GyroXDegPerSecond) / 2f,
                    (left.GyroYDegPerSecond + right.GyroYDegPerSecond) / 2f,
                    (left.GyroZDegPerSecond + right.GyroZDegPerSecond) / 2f,
                    (left.AccelXG + right.AccelXG) / 2f,
                    (left.AccelYG + right.AccelYG) / 2f,
                    (left.AccelZG + right.AccelZG) / 2f,
                    Math.Max(left.TimestampTicksUtc, right.TimestampTicksUtc));
                return true;
            }

            LegionGyroSample source;
            if (hasLeft)
                source = left;
            else if (hasRight)
                source = right;
            else
                return false;

            sample = new GyroSample(
                source.GyroXDegPerSecond,
                source.GyroYDegPerSecond,
                source.GyroZDegPerSecond,
                source.AccelXG,
                source.AccelYG,
                source.AccelZG,
                source.TimestampTicksUtc);
            return true;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
