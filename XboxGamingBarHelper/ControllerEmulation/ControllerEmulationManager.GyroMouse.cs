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

        private void ApplyMouseFromGyro(GyroSample sample)
        {
            // Axis selection:
            // 0 = Yaw/Pitch (Y->X, X->Y)
            // 1 = Yaw/Roll  (Y->X, Z->Y)
            // 2 = Roll/Pitch (Z->X, X->Y)
            float horizontal;
            float vertical;
            switch (mouseAxis)
            {
                case 1:
                    horizontal = sample.GyroYDegPerSecond;
                    vertical = sample.GyroZDegPerSecond;
                    break;
                case 2:
                    horizontal = sample.GyroZDegPerSecond;
                    vertical = sample.GyroXDegPerSecond;
                    break;
                default:
                    horizontal = sample.GyroYDegPerSecond;
                    vertical = sample.GyroXDegPerSecond;
                    break;
            }

            if (mouseInvertX)
            {
                horizontal = -horizontal;
            }

            if (mouseInvertY)
            {
                vertical = -vertical;
            }

            float threshold = Math.Max(0.0f, mouseThreshold);
            horizontal = ApplyDeadzone(horizontal, threshold);
            vertical = ApplyDeadzone(vertical, threshold);
            horizontal = Math.Max(-MouseMaxDegPerSecond, Math.Min(MouseMaxDegPerSecond, horizontal));
            vertical = Math.Max(-MouseMaxDegPerSecond, Math.Min(MouseMaxDegPerSecond, vertical));

            long sampleTicks = sample.TimestampTicksUtc > 0 ? sample.TimestampTicksUtc : DateTime.UtcNow.Ticks;
            float deltaSeconds = DefaultDeltaSeconds;
            if (mouseLastSampleTicksUtc > 0)
            {
                long deltaTicks = sampleTicks - mouseLastSampleTicksUtc;
                if (deltaTicks > 0 && deltaTicks < TimeSpan.TicksPerSecond)
                {
                    deltaSeconds = deltaTicks / (float)TimeSpan.TicksPerSecond;
                }
            }

            mouseLastSampleTicksUtc = sampleTicks;
            if (deltaSeconds < MinDeltaSeconds)
            {
                deltaSeconds = MinDeltaSeconds;
            }
            else if (deltaSeconds > MaxDeltaSeconds)
            {
                deltaSeconds = MaxDeltaSeconds;
            }

            if (!mouseFilterInitialized)
            {
                mouseFilteredHorizontal = horizontal;
                mouseFilteredVertical = vertical;
                mouseFilteredDerivativeHorizontal = 0.0f;
                mouseFilteredDerivativeVertical = 0.0f;
                mouseFilterInitialized = true;
            }
            else
            {
                // Reject single-sample axis spikes that can cause large cursor jumps.
                horizontal = Math.Max(mouseFilteredHorizontal - MouseOutlierMaxDeltaDegPerSecond, Math.Min(mouseFilteredHorizontal + MouseOutlierMaxDeltaDegPerSecond, horizontal));
                vertical = Math.Max(mouseFilteredVertical - MouseOutlierMaxDeltaDegPerSecond, Math.Min(mouseFilteredVertical + MouseOutlierMaxDeltaDegPerSecond, vertical));

                ApplyOneEuroAxis(
                    horizontal,
                    mouseFilteredHorizontal,
                    mouseFilteredDerivativeHorizontal,
                    deltaSeconds,
                    out mouseFilteredHorizontal,
                    out mouseFilteredDerivativeHorizontal);

                ApplyOneEuroAxis(
                    vertical,
                    mouseFilteredVertical,
                    mouseFilteredDerivativeVertical,
                    deltaSeconds,
                    out mouseFilteredVertical,
                    out mouseFilteredDerivativeVertical);
            }

            if (Math.Abs(mouseFilteredHorizontal) < MouseResidualCutoffDegPerSecond)
            {
                mouseFilteredHorizontal = 0.0f;
            }

            if (Math.Abs(mouseFilteredVertical) < MouseResidualCutoffDegPerSecond)
            {
                mouseFilteredVertical = 0.0f;
            }

            float normalizedSensitivity = Math.Max(0.05f, mouseSensitivity / 100.0f);
            float sensitivityScale = (float)Math.Pow(normalizedSensitivity, MouseSensitivityPower);
            float gainXScale = mouseGainX / 100.0f;
            float gainYScale = mouseGainY / 100.0f;

            float moveX = (mouseFilteredHorizontal * deltaSeconds * MousePixelsPerDegree * sensitivityScale * gainXScale) + mouseCarryX;
            float moveY = ((-mouseFilteredVertical) * deltaSeconds * MousePixelsPerDegree * sensitivityScale * gainYScale) + mouseCarryY;

            int deltaX = (int)Math.Round(moveX);
            int deltaY = (int)Math.Round(moveY);

            bool clampedX = false;
            bool clampedY = false;
            if (deltaX > MouseMaxPixelsPerFrame)
            {
                deltaX = MouseMaxPixelsPerFrame;
                clampedX = true;
            }
            else if (deltaX < -MouseMaxPixelsPerFrame)
            {
                deltaX = -MouseMaxPixelsPerFrame;
                clampedX = true;
            }

            if (deltaY > MouseMaxPixelsPerFrame)
            {
                deltaY = MouseMaxPixelsPerFrame;
                clampedY = true;
            }
            else if (deltaY < -MouseMaxPixelsPerFrame)
            {
                deltaY = -MouseMaxPixelsPerFrame;
                clampedY = true;
            }

            mouseCarryX = clampedX ? 0.0f : (moveX - deltaX);
            mouseCarryY = clampedY ? 0.0f : (moveY - deltaY);

            if (deltaX != 0 || deltaY != 0)
            {
                SubmitRelativeMouseMove(deltaX, deltaY);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MapAxisWithCurve(float degPerSec, float minSpeed, float maxSpeed)
        {
            float abs = Math.Abs(degPerSec);
            if (abs <= minSpeed) return 0.0f;
            float range = maxSpeed - minSpeed;
            if (range <= 0.0f) return Math.Sign(degPerSec);
            float normalized = (abs - minSpeed) / range;
            return Math.Sign(degPerSec) * Math.Min(1.0f, normalized);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ApplyOutputRange(float normalized, float minOutput, float maxOutput)
        {
            if (normalized == 0.0f) return 0.0f;
            float abs = Math.Abs(normalized);
            float scaled = minOutput + (abs * (maxOutput - minOutput));
            return Math.Sign(normalized) * Math.Min(1.0f, scaled);
        }

    }
}
