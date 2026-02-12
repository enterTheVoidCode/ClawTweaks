using System;
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
        private readonly string name;
        private Gyrometer gyrometer;
        private Accelerometer accelerometer;
        private bool started;

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

                Logger.Info($"Gyro source '{name}' started (accelerometer available: {accelerometer != null})");
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
            started = false;
            gyrometer = null;
            accelerometer = null;
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            sample = default;
            if (!started || gyrometer == null)
            {
                return false;
            }

            try
            {
                var gyroReading = gyrometer.GetCurrentReading();
                if (gyroReading == null)
                {
                    return false;
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

                sample = new GyroSample(
                    (float)gyroReading.AngularVelocityX,
                    (float)gyroReading.AngularVelocityY,
                    (float)gyroReading.AngularVelocityZ,
                    accelX,
                    accelY,
                    accelZ,
                    DateTime.UtcNow.Ticks);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal sealed class LegionControllerGyroSourceAdapter : IGyroSourceAdapter
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
}
