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

        private bool IsGyroActivationEnabled(XINPUT_GAMEPAD? gamepad)
        {
            switch (gyroActivationMode)
            {
                case 1:
                    if (!gamepad.HasValue)
                    {
                        lastGyroActivationButtonPressed = false;
                        return false;
                    }

                    bool holdPressed = IsGyroActivationButtonPressed(gamepad.Value);
                    lastGyroActivationButtonPressed = holdPressed;
                    return holdPressed;

                case 2:
                    bool togglePressed = gamepad.HasValue && IsGyroActivationButtonPressed(gamepad.Value);
                    if (togglePressed && !lastGyroActivationButtonPressed)
                    {
                        gyroToggleActive = !gyroToggleActive;
                    }

                    lastGyroActivationButtonPressed = togglePressed;
                    return gyroToggleActive;

                default:
                    lastGyroActivationButtonPressed = gamepad.HasValue && IsGyroActivationButtonPressed(gamepad.Value);
                    return true;
            }
        }

        private bool IsGyroActivationButtonPressed(XINPUT_GAMEPAD gamepad)
        {
            switch (gyroActivationButton)
            {
                case 1:
                    return gamepad.bRightTrigger > XINPUT_TRIGGER_THRESHOLD;
                case 2:
                    return gamepad.bLeftTrigger > XINPUT_TRIGGER_THRESHOLD;
                case 3:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
                case 4:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
                case 5:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_A) != 0;
                case 6:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_B) != 0;
                case 7:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_X) != 0;
                case 8:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_Y) != 0;
                case 9:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_THUMB) != 0;
                case 10:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_LEFT_THUMB) != 0;
                case 11:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_DPAD_UP) != 0;
                case 12:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_DPAD_DOWN) != 0;
                case 13:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_DPAD_LEFT) != 0;
                case 14:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0;
                case 15:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_START) != 0;
                case 16:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_BACK) != 0;
                // FR-1 (issue #79): Legion-only auxiliary buttons. Source is the cached
                // LegionHid sample (lastLegionAuxButtons) — only updated when
                // ShouldUseLegionHidInputPath() is true. On non-Legion devices or when
                // improvedInputRead is off, these will read 0 and never trigger.
                case 17:
                    return (lastLegionAuxButtons & LEGION_AUX_EXTRA_R2) != 0;       // M3
                case 18:
                    return (lastLegionAuxButtons & LEGION_AUX_EXTRA_RM1) != 0;      // M1
                case 19:
                    return (lastLegionAuxButtons & LEGION_AUX_EXTRA_R3) != 0;       // M2
                case 20:
                    return (lastLegionAuxButtons & LEGION_AUX_EXTRA_L1) != 0;       // Y1
                case 21:
                    return (lastLegionAuxButtons & LEGION_AUX_EXTRA_L2) != 0;       // Y2
                case 22:
                    return (lastLegionAuxButtons & LEGION_AUX_EXTRA_R1) != 0;       // Y3
                default:
                    return false;
            }
        }

        private ushort ApplyVirtualAbxyLayout(ushort buttons)
        {
            if (virtualAbxyLayout != 1)
            {
                return buttons;
            }

            bool aPressed = (buttons & XINPUT_GAMEPAD_A) != 0;
            bool bPressed = (buttons & XINPUT_GAMEPAD_B) != 0;
            bool xPressed = (buttons & XINPUT_GAMEPAD_X) != 0;
            bool yPressed = (buttons & XINPUT_GAMEPAD_Y) != 0;

            ushort remapped = (ushort)(buttons & ~(XINPUT_GAMEPAD_A | XINPUT_GAMEPAD_B | XINPUT_GAMEPAD_X | XINPUT_GAMEPAD_Y));
            if (bPressed)
            {
                remapped |= XINPUT_GAMEPAD_A;
            }

            if (aPressed)
            {
                remapped |= XINPUT_GAMEPAD_B;
            }

            if (yPressed)
            {
                remapped |= XINPUT_GAMEPAD_X;
            }

            if (xPressed)
            {
                remapped |= XINPUT_GAMEPAD_Y;
            }

            return remapped;
        }

        private void EmitForwardingStatsIfDue()
        {
            long nowUtc = DateTime.UtcNow.Ticks;
            long elapsed = nowUtc - forwardingStatsLastEmitTicksUtc;
            if (elapsed < TimeSpan.TicksPerSecond * 5) return;

            int iters = forwardingIterations;
            int okX = forwardingReadOkXInput;
            int okHid = forwardingReadOkLegionHid;
            int fail = forwardingReadFail;
            int gMerged = forwardingGyroMerged;
            int gNoSample = forwardingGyroNoSample;
            int gGateOff = forwardingGyroGateOff;
            double seconds = elapsed / (double)TimeSpan.TicksPerSecond;
            string physIdx = physicalXboxUserIndex.HasValue ? physicalXboxUserIndex.Value.ToString() : "<none>";
            string virtIdx = virtualXboxUserIndex.HasValue ? virtualXboxUserIndex.Value.ToString() : "<none>";
            string srcPath = ShouldUseLegionHidInputPath() ? "LegionHid+XInput" : "XInput";
            // Bias snapshot is included on every line (matches VIIPER stats)
            // so a single grep can verify the estimator settled on a sane value.
            string biasField = stickGyroBiasEstimator != null && stickGyroBiasEstimator.IsCalibrated
                ? string.Format("bias=[{0:F1},{1:F1},{2:F1}]",
                    stickGyroBiasEstimator.BiasXDegPerSec,
                    stickGyroBiasEstimator.BiasYDegPerSec,
                    stickGyroBiasEstimator.BiasZDegPerSec)
                : "bias=uncal";
            Logger.Info($"CE forwarding stats: {seconds:F1}s iters={iters} okXInput={okX} okLegionHid={okHid} fail={fail} mode={mode} source={srcPath} physIdx={physIdx} virtIdx={virtIdx} gyroMerged={gMerged} gyroGateOff={gGateOff} gyroNoSample={gNoSample} {biasField}");

            forwardingStatsLastEmitTicksUtc = nowUtc;
            forwardingIterations = 0;
            forwardingReadOkXInput = 0;
            forwardingReadOkLegionHid = 0;
            forwardingReadFail = 0;
            forwardingGyroMerged = 0;
            forwardingGyroNoSample = 0;
            forwardingGyroGateOff = 0;
        }

    }
}
