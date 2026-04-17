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

    }
}
