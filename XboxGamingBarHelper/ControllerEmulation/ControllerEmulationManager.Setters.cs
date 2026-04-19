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

        public void SetGyroSource(int source)
        {
            int normalized = NormalizeGyroSource(source);
            if (gyroSource == normalized)
            {
                return;
            }

            gyroSource = normalized;
            SaveSettings();
            ApplyCurrentConfiguration("gyro source changed");
        }

        public void SetMode(int inMode)
        {
            int normalized = NormalizeMode(inMode);
            if (mode == normalized)
            {
                return;
            }

            mode = normalized;
            SaveSettings();
            ApplyCurrentConfiguration("mode changed");
        }

        public void SetRumbleProfile(int value)
        {
            int normalized = NormalizeRumbleProfile(value);
            if (rumbleProfile == normalized)
            {
                return;
            }

            rumbleProfile = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation rumble profile set to {rumbleProfile}");
        }

        public void SetGyroActivationMode(int value)
        {
            int normalized = NormalizeGyroActivationMode(value);
            if (gyroActivationMode == normalized)
            {
                return;
            }

            gyroActivationMode = normalized;
            ResetGyroActivationRuntimeState();
            SaveSettings();
            Logger.Info($"Controller emulation gyro activation mode set to {gyroActivationMode}");
        }

        public void SetGyroActivationButton(int value)
        {
            int normalized = NormalizeGyroActivationButton(value);
            if (gyroActivationButton == normalized)
            {
                return;
            }

            gyroActivationButton = normalized;
            ResetGyroActivationRuntimeState();
            SaveSettings();
            Logger.Info($"Controller emulation gyro activation button set to {gyroActivationButton}");
        }

        public void SetDs4Orientation(int value)
        {
            int normalized = NormalizeDs4Orientation(value);
            if (ds4Orientation == normalized)
            {
                return;
            }

            ds4Orientation = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation DS4 motion orientation set to {ds4Orientation}");
        }

        public void SetPs4TouchpadEnabled(bool value)
        {
            if (ps4TouchpadEnabled == value)
            {
                return;
            }

            ps4TouchpadEnabled = value;
            SaveSettings();
            Logger.Info($"Controller emulation PS4 touchpad forwarding set to {ps4TouchpadEnabled}");
        }

        public void SetLedForwardingEnabled(bool value)
        {
            if (ledForwardingEnabled == value)
            {
                return;
            }

            ledForwardingEnabled = value;
            SaveSettings();
            Logger.Info($"Controller emulation LED forwarding set to {ledForwardingEnabled}");

            if (!ledForwardingEnabled && hasForwardedLed)
            {
                RevertLegionLed();
            }
        }

        public void CalibrateGyro()
        {
            if (deviceType != SharedDeviceType.LegionGo && deviceType != SharedDeviceType.LegionGo2)
            {
                Logger.Warn("Calibrate gyro: not supported on this device");
                return;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var controller = new LegionGoController())
                    {
                        if (!controller.Connect())
                        {
                            Logger.Warn("Calibrate gyro: controller not connected");
                            return;
                        }

                        bool ok = controller.CalibrateGyro();
                        Logger.Info($"Calibrate gyro: {(ok ? "success" : "failed")}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Calibrate gyro failed: {ex.Message}");
                }
            });
        }

        public void SetEnabled(bool value)
        {
            if (enabled == value)
            {
                return;
            }

            enabled = value;
            SaveSettings();
            ApplyCurrentConfiguration(enabled ? "enabled changed: on" : "enabled changed: off");
            RaiseEmulationEnabledChanged();
        }

        /// <summary>
        /// Runtime mutual-exclusion with the VIIPER backend. When set to true, this manager
        /// stops forwarding immediately; when cleared, it re-applies its persisted configuration.
        /// The user's saved <see cref="SetEnabled"/> value is untouched either way.
        /// </summary>
        public void SetSuppressedByViiper(bool value)
        {
            if (suppressedByViiper == value) return;
            suppressedByViiper = value;
            ApplyCurrentConfiguration(value ? "suppressed by VIIPER" : "VIIPER suppression cleared");
        }

        public void SetHideStockController(bool value)
        {
            if (hideStockController == value)
            {
                return;
            }

            hideStockController = value;
            SaveSettings();
            ApplySuppressionConfiguration(hideStockController ? "hide stock controller changed: on" : "hide stock controller changed: off");
        }

        public void SetHideTarget(int value)
        {
            int normalized = NormalizeHideTarget(value);
            if (hideTarget == normalized)
            {
                return;
            }

            hideTarget = normalized;
            SaveSettings();
            ApplySuppressionConfiguration($"hide target changed: {hideTarget}");
        }

        public void SetImprovedInputRead(bool value)
        {
            if (improvedInputRead == value)
            {
                return;
            }

            improvedInputRead = value;
            SaveSettings();
            ApplyCurrentConfiguration(improvedInputRead ? "improved input changed: on" : "improved input changed: off");
        }

        public void SetMouseSensitivity(int value)
        {
            int normalized = NormalizeMouseSensitivity(value);
            if (mouseSensitivity == normalized)
            {
                return;
            }

            mouseSensitivity = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse sensitivity set to {mouseSensitivity}");
        }

        public void SetMouseThreshold(int value)
        {
            int normalized = NormalizeMouseThreshold(value);
            if (mouseThreshold == normalized)
            {
                return;
            }

            mouseThreshold = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse threshold set to {mouseThreshold}");
        }

        public void SetMouseAxis(int value)
        {
            int normalized = NormalizeMouseAxis(value);
            if (mouseAxis == normalized)
            {
                return;
            }

            mouseAxis = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse axis set to {mouseAxis}");
        }

        public void SetMouseInvertX(bool value)
        {
            if (mouseInvertX == value)
            {
                return;
            }

            mouseInvertX = value;
            SaveSettings();
            Logger.Info($"Controller emulation mouse invert X set to {mouseInvertX}");
        }

        public void SetMouseInvertY(bool value)
        {
            if (mouseInvertY == value)
            {
                return;
            }

            mouseInvertY = value;
            SaveSettings();
            Logger.Info($"Controller emulation mouse invert Y set to {mouseInvertY}");
        }

        public void SetMouseGainX(int value)
        {
            int normalized = NormalizeMouseGain(value);
            if (mouseGainX == normalized)
            {
                return;
            }

            mouseGainX = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse gain X set to {mouseGainX}");
        }

        public void SetMouseGainY(int value)
        {
            int normalized = NormalizeMouseGain(value);
            if (mouseGainY == normalized)
            {
                return;
            }

            mouseGainY = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse gain Y set to {mouseGainY}");
        }

        public void SetStickSensitivity(int value)
        {
            int normalized = NormalizeStickSensitivity(value);
            if (stickSensitivity == normalized)
            {
                return;
            }

            stickSensitivity = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick sensitivity set to {stickSensitivity}");
        }

        public void SetStickThreshold(int value)
        {
            int normalized = NormalizeStickThreshold(value);
            if (stickThreshold == normalized)
            {
                return;
            }

            stickThreshold = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick threshold set to {stickThreshold}");
        }

        public void SetStickAxis(int value)
        {
            int normalized = NormalizeStickAxis(value);
            if (stickAxis == normalized)
            {
                return;
            }

            stickAxis = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick axis set to {stickAxis}");
        }

        public void SetStickInvertX(bool value)
        {
            if (stickInvertX == value)
            {
                return;
            }

            stickInvertX = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick invert X set to {stickInvertX}");
        }

        public void SetStickInvertY(bool value)
        {
            if (stickInvertY == value)
            {
                return;
            }

            stickInvertY = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick invert Y set to {stickInvertY}");
        }

        public void SetStickGainX(int value)
        {
            int normalized = NormalizeStickGain(value);
            if (stickGainX == normalized)
            {
                return;
            }

            stickGainX = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick gain X set to {stickGainX}");
        }

        public void SetStickGainY(int value)
        {
            int normalized = NormalizeStickGain(value);
            if (stickGainY == normalized)
            {
                return;
            }

            stickGainY = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick gain Y set to {stickGainY}");
        }

        public void SetStickSelect(int value)
        {
            int normalized = NormalizeStickSelect(value);
            if (stickSelect == normalized)
            {
                return;
            }

            stickSelect = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick select set to {stickSelect}");
        }

        public void SetStickExcessMove(bool value)
        {
            if (stickExcessMove == value)
            {
                return;
            }

            stickExcessMove = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick excess move set to {stickExcessMove}");
        }

        public void SetStickRange(int value)
        {
            int normalized = NormalizeStickRange(value);
            if (stickRange == normalized)
            {
                return;
            }

            stickRange = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick range set to {stickRange}");
        }

        public void SetStickOnlyJoystickData(bool value)
        {
            if (stickOnlyJoystickData == value)
            {
                return;
            }

            stickOnlyJoystickData = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick only joystick data set to {stickOnlyJoystickData}");
        }

        public void SetStickMinGyroSpeed(int value)
        {
            int normalized = Math.Max(0, Math.Min(100, value));
            if (stickMinGyroSpeed == normalized) return;
            stickMinGyroSpeed = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick min gyro speed set to {stickMinGyroSpeed}");
        }

        public void SetStickMaxGyroSpeed(int value)
        {
            int normalized = Math.Max(50, Math.Min(720, value));
            if (stickMaxGyroSpeed == normalized) return;
            stickMaxGyroSpeed = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick max gyro speed set to {stickMaxGyroSpeed}");
        }

        public void SetStickMinOutput(int value)
        {
            int normalized = Math.Max(0, Math.Min(100, value));
            if (stickMinOutput == normalized) return;
            stickMinOutput = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick min output set to {stickMinOutput}");
        }

        public void SetStickMaxOutput(int value)
        {
            int normalized = Math.Max(1, Math.Min(100, value));
            if (stickMaxOutput == normalized) return;
            stickMaxOutput = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick max output set to {stickMaxOutput}");
        }

        public void SetStickPowerCurve(int value)
        {
            int normalized = Math.Max(10, Math.Min(400, value));
            if (stickPowerCurve == normalized) return;
            stickPowerCurve = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick power curve set to {stickPowerCurve}");
        }

        public void SetStickSensitivityV2(int value)
        {
            int normalized = Math.Max(1, Math.Min(400, value));
            if (stickSensitivityV2 == normalized) return;
            stickSensitivityV2 = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick sensitivity v2 set to {stickSensitivityV2}");
        }

        public void SetStickDeadzone(int value)
        {
            int normalized = Math.Max(0, Math.Min(50, value));
            if (stickDeadzone == normalized) return;
            stickDeadzone = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick deadzone set to {stickDeadzone}");
        }

        public void SetStickPrecisionSpeed(int value)
        {
            int normalized = Math.Max(0, Math.Min(100, value));
            if (stickPrecisionSpeed == normalized) return;
            stickPrecisionSpeed = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick precision speed set to {stickPrecisionSpeed}");
        }

        public void SetStickOutputMix(int value)
        {
            int normalized = Math.Max(-100, Math.Min(100, value));
            if (stickOutputMix == normalized) return;
            stickOutputMix = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick output mix set to {stickOutputMix}");
        }

        public void SetStickOrientationV2(int value)
        {
            int normalized = (value == 1) ? 1 : 0;
            if (stickOrientationV2 == normalized) return;
            stickOrientationV2 = normalized;
            stickFilterInitialized = false;
            SaveSettings();
            Logger.Info($"Controller emulation stick orientation v2 set to {stickOrientationV2}");
        }

        public void SetStickConversion(int value)
        {
            int normalized = Math.Max(0, Math.Min(2, value));
            if (stickConversion == normalized) return;
            stickConversion = normalized;
            stickFilterInitialized = false;
            SaveSettings();
            Logger.Info($"Controller emulation stick conversion set to {stickConversion}");
        }

        public void SetVirtualABXYLayout(int value)
        {
            int normalized = NormalizeVirtualAbxyLayout(value);
            if (virtualAbxyLayout == normalized)
            {
                return;
            }

            virtualAbxyLayout = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation virtual ABXY layout set to {virtualAbxyLayout}");
        }

    }
}
