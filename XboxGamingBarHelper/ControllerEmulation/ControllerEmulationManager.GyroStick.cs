using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.Labs;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Windows;
using SharedDeviceType = Shared.Enums.DeviceType;

namespace XboxGamingBarHelper.ControllerEmulation
{
    internal partial class ControllerEmulationManager
    {

        private void ApplyStickFromGyro(
            GyroSample sample,
            out short outputX,
            out short outputY)
        {
            outputX = 0;
            outputY = 0;

            // Debug: log raw gyro axes periodically to diagnose axis mapping
            if (sample.TimestampTicksUtc - stickLastDiagLogTicksUtc > TimeSpan.TicksPerSecond)
            {
                stickLastDiagLogTicksUtc = sample.TimestampTicksUtc;
                float absX = Math.Abs(sample.GyroXDegPerSecond);
                float absY = Math.Abs(sample.GyroYDegPerSecond);
                float absZ = Math.Abs(sample.GyroZDegPerSecond);
                if (absX > 5 || absY > 5 || absZ > 5)
                {
                    Logger.Info($"StickGyroRaw: X={sample.GyroXDegPerSecond:F1} Y={sample.GyroYDegPerSecond:F1} Z={sample.GyroZDegPerSecond:F1} conv={stickConversion} orient={stickOrientationV2}");
                }
            }

            // 1. Orientation-corrected input axes
            //    Parallel = standard (Y=Yaw, Z=Roll)
            //    Orthogonal = swap Y↔Z for devices where IMU axes are rotated
            float gyroX = sample.GyroXDegPerSecond;
            float gyroY = sample.GyroYDegPerSecond;
            float gyroZ = sample.GyroZDegPerSecond;

            if (stickOrientationV2 == 1)
            {
                float origY = gyroY;
                gyroY = gyroZ;
                gyroZ = -origY;
            }

            // 2. 3DOF-to-2D conversion
            float horizontal;
            float vertical;
            switch (stickConversion)
            {
                case 1: // Roll
                    horizontal = gyroZ;
                    vertical = gyroX;
                    break;
                case 2: // Yaw + Roll
                    horizontal = gyroY + gyroZ;
                    vertical = gyroX;
                    break;
                default: // 0 = Yaw
                    horizontal = gyroY;
                    vertical = gyroX;
                    break;
            }

            // 3. Invert axes
            if (stickInvertX) horizontal = -horizontal;
            if (stickInvertY) vertical = -vertical;

            // 4. Deadzone with smooth recovery
            float deadzone = Math.Max(0.0f, stickDeadzone);
            horizontal = ApplyDeadzone(horizontal, deadzone);
            vertical = ApplyDeadzone(vertical, deadzone);

            // 5. One Euro filter
            float deltaSeconds;
            if (stickLastSampleTicksUtc > 0 && sample.TimestampTicksUtc > stickLastSampleTicksUtc)
            {
                deltaSeconds = (sample.TimestampTicksUtc - stickLastSampleTicksUtc) / (float)TimeSpan.TicksPerSecond;
                deltaSeconds = Math.Max(MinDeltaSeconds, Math.Min(MaxDeltaSeconds, deltaSeconds));
            }
            else
            {
                deltaSeconds = DefaultDeltaSeconds;
            }
            stickLastSampleTicksUtc = sample.TimestampTicksUtc;

            if (!stickFilterInitialized)
            {
                stickFilteredHorizontal = horizontal;
                stickFilteredVertical = vertical;
                stickFilteredDerivativeHorizontal = 0.0f;
                stickFilteredDerivativeVertical = 0.0f;
                stickFilterInitialized = true;
            }
            else
            {
                ApplyOneEuroAxis(horizontal, stickFilteredHorizontal,
                    stickFilteredDerivativeHorizontal, deltaSeconds,
                    out stickFilteredHorizontal, out stickFilteredDerivativeHorizontal);
                ApplyOneEuroAxis(vertical, stickFilteredVertical,
                    stickFilteredDerivativeVertical, deltaSeconds,
                    out stickFilteredVertical, out stickFilteredDerivativeVertical);
                horizontal = stickFilteredHorizontal;
                vertical = stickFilteredVertical;
            }

            // 6. Precision speed
            if (stickPrecisionSpeed > 0)
            {
                float speed = (float)Math.Sqrt(horizontal * horizontal + vertical * vertical);
                if (speed > 0.0f && speed < stickPrecisionSpeed)
                {
                    float scale = speed / stickPrecisionSpeed;
                    horizontal *= scale;
                    vertical *= scale;
                }
            }

            // 7. Sensitivity
            float sensitivity = Math.Max(0.01f, stickSensitivityV2 / 100.0f);
            horizontal *= sensitivity;
            vertical *= sensitivity;

            // 8. Normalize to 0-1 using min/max gyro speed
            float minSpeed = Math.Max(0.0f, stickMinGyroSpeed);
            float maxSpeed = Math.Max(1.0f, stickMaxGyroSpeed);
            float normalizedX = MapAxisWithCurve(horizontal, minSpeed, maxSpeed);
            float normalizedY = MapAxisWithCurve(-vertical, minSpeed, maxSpeed);

            // 9. Power curve
            float power = Math.Max(0.1f, stickPowerCurve / 100.0f);
            normalizedX = Math.Sign(normalizedX) * (float)Math.Pow(Math.Abs(normalizedX), power);
            normalizedY = Math.Sign(normalizedY) * (float)Math.Pow(Math.Abs(normalizedY), power);

            // 10. Output mix
            if (stickOutputMix > 0)
            {
                float vertScale = 1.0f - (stickOutputMix / 100.0f);
                normalizedY *= vertScale;
            }
            else if (stickOutputMix < 0)
            {
                float horizScale = 1.0f + (stickOutputMix / 100.0f);
                normalizedX *= horizScale;
            }

            // 11. Output range (anti-deadzone + max output)
            float minOut = stickMinOutput / 100.0f;
            float maxOut = Math.Max(0.01f, stickMaxOutput / 100.0f);
            normalizedX = ApplyOutputRange(normalizedX, minOut, maxOut);
            normalizedY = ApplyOutputRange(normalizedY, minOut, maxOut);

            // 12. Clamp and convert
            outputX = ConvertNormalizedToInt16(normalizedX);
            outputY = ConvertNormalizedToInt16(normalizedY);
        }

        private void ApplyDs4Orientation(
            ref float gyroX,
            ref float gyroY,
            ref float gyroZ,
            ref float accelX,
            ref float accelY,
            ref float accelZ)
        {
            if (ds4Orientation != 1)
            {
                return;
            }

            // Orthogonal mode rotates around X so DS4 motion orientation matches
            // users holding the handheld in a perpendicular posture.
            // Swaps Y↔Z (yaw↔roll) with sign flip.
            float originalGyroY = gyroY;
            float originalGyroZ = gyroZ;
            gyroY = originalGyroZ;
            gyroZ = -originalGyroY;

            float originalAccelY = accelY;
            float originalAccelZ = accelZ;
            accelY = originalAccelZ;
            accelZ = -originalAccelY;
        }

        private static short ConvertNormalizedToInt16(float normalized)
        {
            float clamped = Math.Max(-1.0f, Math.Min(1.0f, normalized));
            return (short)Math.Round(clamped * short.MaxValue);
        }

        private static void MergeStickVectors(
            short physicalX,
            short physicalY,
            short gyroX,
            short gyroY,
            out short mergedX,
            out short mergedY)
        {
            float sumX = physicalX + gyroX;
            float sumY = physicalY + gyroY;
            float magnitude = (float)Math.Sqrt((sumX * sumX) + (sumY * sumY));
            if (magnitude > short.MaxValue && magnitude > 0.0f)
            {
                float scale = short.MaxValue / magnitude;
                sumX *= scale;
                sumY *= scale;
            }

            mergedX = ClampToInt16(sumX);
            mergedY = ClampToInt16(sumY);
        }

        private static short ClampToInt16(float value)
        {
            if (value > short.MaxValue)
            {
                return short.MaxValue;
            }

            if (value < short.MinValue)
            {
                return short.MinValue;
            }

            return (short)Math.Round(value);
        }

    }
}
