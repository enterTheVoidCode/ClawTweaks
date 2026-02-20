using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using NLog;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Virtual Xbox 360 controller using ViGEmBus driver.
    /// Used to send Xbox Guide button presses when Legion L is pressed.
    /// </summary>
    internal class ViGEmController : IDisposable
    {
        internal enum VirtualGamepadType
        {
            Xbox360 = 0,
            DualShock4 = 1,
        }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public event Action<byte, byte> RumbleReceived;

        private ViGEmClient client;
        private IVirtualGamepad virtualGamepad;
        private IXbox360Controller xboxController;
        private IDualShock4Controller dualShockController;
        private VirtualGamepadType currentType = VirtualGamepadType.Xbox360;
        private bool isConnected = false;
        private bool isDisposed = false;
        private ushort dualShockTimestamp = 0;
        private byte dualShockTouchPacketNumber = 0;
        private byte dualShockTouchFingerId = 0;
        private bool dualShockTouchWasActive = false;

        // XInput button flags
        private const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        private const ushort XINPUT_GAMEPAD_START = 0x0010;
        private const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        private const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        private const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        private const ushort XINPUT_GAMEPAD_A = 0x1000;
        private const ushort XINPUT_GAMEPAD_B = 0x2000;
        private const ushort XINPUT_GAMEPAD_X = 0x4000;
        private const ushort XINPUT_GAMEPAD_Y = 0x8000;

        public bool IsPluggedIn => isConnected && virtualGamepad != null;
        public VirtualGamepadType CurrentType => currentType;
        public int? VirtualXboxUserIndex
        {
            get
            {
                if (xboxController == null)
                {
                    return null;
                }

                try
                {
                    return xboxController.UserIndex;
                }
                catch (Exception ex)
                {
                    Logger.Debug($"ViGEmController: Xbox user index not yet reported ({ex.GetType().Name})");
                    return null;
                }
            }
        }

        public bool Connect()
        {
            try
            {
                client = new ViGEmClient();
                Logger.Info("ViGEmController: Connected to ViGEmBus");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Failed to connect - {ex.Message}");
                return false;
            }
        }

        public bool PlugIn()
        {
            return PlugIn(VirtualGamepadType.Xbox360);
        }

        public bool PlugIn(VirtualGamepadType gamepadType)
        {
            if (client == null)
            {
                return false;
            }

            try
            {
                DisconnectVirtualGamepad();

                currentType = gamepadType;
                switch (gamepadType)
                {
                    case VirtualGamepadType.DualShock4:
                        dualShockController = client.CreateDualShock4Controller();
                        dualShockController.FeedbackReceived += OnDualShock4FeedbackReceived;
                        virtualGamepad = dualShockController;
                        break;
                    case VirtualGamepadType.Xbox360:
                    default:
                        xboxController = client.CreateXbox360Controller();
                        xboxController.FeedbackReceived += OnXbox360FeedbackReceived;
                        virtualGamepad = xboxController;
                        break;
                }

                virtualGamepad.Connect();
                isConnected = true;
                Logger.Info($"ViGEmController: Virtual {gamepadType} controller plugged in");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Failed to plug in {gamepadType} - {ex.Message}");
                DisconnectVirtualGamepad();
                return false;
            }
        }

        public bool Unplug()
        {
            if (virtualGamepad == null)
            {
                return true;
            }

            try
            {
                DisconnectVirtualGamepad();
                isConnected = false;
                Logger.Info("ViGEmController: Virtual controller unplugged");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Failed to unplug - {ex.Message}");
                return false;
            }
        }

        public bool EnsureConnected()
        {
            return EnsureConnected(VirtualGamepadType.Xbox360);
        }

        public bool EnsureConnected(VirtualGamepadType gamepadType)
        {
            if (IsPluggedIn && currentType == gamepadType)
            {
                return true;
            }

            Logger.Info("ViGEmController: Reconnecting...");
            Dispose();
            isDisposed = false;
            return Connect() && PlugIn(gamepadType);
        }

        public bool SetGuide(bool pressed)
        {
            if (!IsPluggedIn || xboxController == null)
            {
                return false;
            }

            try
            {
                xboxController.SetButtonState(Xbox360Button.Guide, pressed);
                virtualGamepad.SubmitReport();
                Logger.Debug($"ViGEmController: SetGuide({pressed})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: SetGuide failed - {ex.Message}");
                return false;
            }
        }

        public bool SubmitXboxState(
            ushort buttons,
            byte leftTrigger,
            byte rightTrigger,
            short leftThumbX,
            short leftThumbY,
            short rightThumbX,
            short rightThumbY)
        {
            if (!IsPluggedIn || xboxController == null)
            {
                return false;
            }

            try
            {
                xboxController.SetButtonState(Xbox360Button.Up, (buttons & XINPUT_GAMEPAD_DPAD_UP) != 0);
                xboxController.SetButtonState(Xbox360Button.Down, (buttons & XINPUT_GAMEPAD_DPAD_DOWN) != 0);
                xboxController.SetButtonState(Xbox360Button.Left, (buttons & XINPUT_GAMEPAD_DPAD_LEFT) != 0);
                xboxController.SetButtonState(Xbox360Button.Right, (buttons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0);
                xboxController.SetButtonState(Xbox360Button.Start, (buttons & XINPUT_GAMEPAD_START) != 0);
                xboxController.SetButtonState(Xbox360Button.Back, (buttons & XINPUT_GAMEPAD_BACK) != 0);
                xboxController.SetButtonState(Xbox360Button.LeftThumb, (buttons & XINPUT_GAMEPAD_LEFT_THUMB) != 0);
                xboxController.SetButtonState(Xbox360Button.RightThumb, (buttons & XINPUT_GAMEPAD_RIGHT_THUMB) != 0);
                xboxController.SetButtonState(Xbox360Button.LeftShoulder, (buttons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0);
                xboxController.SetButtonState(Xbox360Button.RightShoulder, (buttons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0);
                xboxController.SetButtonState(Xbox360Button.A, (buttons & XINPUT_GAMEPAD_A) != 0);
                xboxController.SetButtonState(Xbox360Button.B, (buttons & XINPUT_GAMEPAD_B) != 0);
                xboxController.SetButtonState(Xbox360Button.X, (buttons & XINPUT_GAMEPAD_X) != 0);
                xboxController.SetButtonState(Xbox360Button.Y, (buttons & XINPUT_GAMEPAD_Y) != 0);

                xboxController.SetSliderValue(Xbox360Slider.LeftTrigger, leftTrigger);
                xboxController.SetSliderValue(Xbox360Slider.RightTrigger, rightTrigger);
                xboxController.SetAxisValue(Xbox360Axis.LeftThumbX, leftThumbX);
                xboxController.SetAxisValue(Xbox360Axis.LeftThumbY, leftThumbY);
                xboxController.SetAxisValue(Xbox360Axis.RightThumbX, rightThumbX);
                xboxController.SetAxisValue(Xbox360Axis.RightThumbY, rightThumbY);

                virtualGamepad.SubmitReport();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: SubmitXboxState failed - {ex.Message}");
                return false;
            }
        }

        public bool SubmitDualShock4State(
            ushort buttons,
            byte leftTrigger,
            byte rightTrigger,
            short leftThumbX,
            short leftThumbY,
            short rightThumbX,
            short rightThumbY)
        {
            return SubmitDualShock4StateRaw(
                buttons,
                leftTrigger,
                rightTrigger,
                leftThumbX,
                leftThumbY,
                rightThumbX,
                rightThumbY,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        public bool SubmitDualShock4StateRaw(
            ushort buttons,
            byte leftTrigger,
            byte rightTrigger,
            short leftThumbX,
            short leftThumbY,
            short rightThumbX,
            short rightThumbY,
            short gyroXRaw,
            short gyroYRaw,
            short gyroZRaw,
            short accelXRaw,
            short accelYRaw,
            short accelZRaw,
            bool touchActive = false,
            ushort touchX = 0,
            ushort touchY = 0,
            bool touchpadButtonPressed = false)
        {
            if (!IsPluggedIn || dualShockController == null)
            {
                return false;
            }

            try
            {
                byte[] rawReport = new byte[63];

                rawReport[0] = ConvertXInputAxisToDs4(leftThumbX);
                rawReport[1] = ConvertXInputAxisToDs4Inverted(leftThumbY);
                rawReport[2] = ConvertXInputAxisToDs4(rightThumbX);
                rawReport[3] = ConvertXInputAxisToDs4Inverted(rightThumbY);

                byte dpad = MapDPadNibble(buttons);
                byte face = 0;
                if ((buttons & XINPUT_GAMEPAD_X) != 0) { face |= 0x10; } // Square
                if ((buttons & XINPUT_GAMEPAD_A) != 0) { face |= 0x20; } // Cross
                if ((buttons & XINPUT_GAMEPAD_B) != 0) { face |= 0x40; } // Circle
                if ((buttons & XINPUT_GAMEPAD_Y) != 0) { face |= 0x80; } // Triangle
                rawReport[4] = (byte)(dpad | face);

                byte sharedButtons = 0;
                if ((buttons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0) { sharedButtons |= 0x01; }   // L1
                if ((buttons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) { sharedButtons |= 0x02; }  // R1
                if (leftTrigger > 30) { sharedButtons |= 0x04; }                                  // L2 button
                if (rightTrigger > 30) { sharedButtons |= 0x08; }                                 // R2 button
                if ((buttons & XINPUT_GAMEPAD_BACK) != 0) { sharedButtons |= 0x10; }             // Share
                if ((buttons & XINPUT_GAMEPAD_START) != 0) { sharedButtons |= 0x20; }            // Options
                if ((buttons & XINPUT_GAMEPAD_LEFT_THUMB) != 0) { sharedButtons |= 0x40; }       // L3
                if ((buttons & XINPUT_GAMEPAD_RIGHT_THUMB) != 0) { sharedButtons |= 0x80; }      // R3
                rawReport[5] = sharedButtons;

                rawReport[6] = touchpadButtonPressed ? (byte)0x02 : (byte)0x00; // PS / touchpad special buttons
                rawReport[7] = leftTrigger;
                rawReport[8] = rightTrigger;

                unchecked { dualShockTimestamp += 188; }
                rawReport[9] = (byte)(dualShockTimestamp & 0xFF);
                rawReport[10] = (byte)((dualShockTimestamp >> 8) & 0xFF);

                rawReport[11] = 0x7F;      // Battery level placeholder
                WriteInt16(rawReport, 12, gyroXRaw);
                WriteInt16(rawReport, 14, gyroYRaw);
                WriteInt16(rawReport, 16, gyroZRaw);
                WriteInt16(rawReport, 18, accelXRaw);
                WriteInt16(rawReport, 20, accelYRaw);
                WriteInt16(rawReport, 22, accelZRaw);
                rawReport[29] = 0x0B;      // DS4 "full battery" flag used by most emulators
                rawReport[32] = dualShockTouchPacketNumber++;
                WriteDs4TouchPacket(rawReport, 33, touchActive, touchX, touchY, ref dualShockTouchFingerId, ref dualShockTouchWasActive);
                byte inactiveFingerId = 1;
                bool inactiveFingerState = false;
                WriteDs4TouchPacket(rawReport, 37, false, 0, 0, ref inactiveFingerId, ref inactiveFingerState);

                dualShockController.SubmitRawReport(rawReport);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: SubmitDualShock4StateRaw failed - {ex.Message}");
                return false;
            }
        }

        private static void WriteInt16(byte[] buffer, int offset, short value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteDs4TouchPacket(
            byte[] buffer,
            int offset,
            bool isTouching,
            ushort x,
            ushort y,
            ref byte fingerId,
            ref bool previousTouchState)
        {
            if (buffer == null || offset < 0 || offset + 3 >= buffer.Length)
            {
                return;
            }

            const int Ds4TouchMaxX = 1919;
            const int Ds4TouchMaxY = 943;

            if (isTouching && !previousTouchState)
            {
                unchecked { fingerId++; }
            }

            previousTouchState = isTouching;

            if (x > Ds4TouchMaxX)
            {
                x = Ds4TouchMaxX;
            }

            if (y > Ds4TouchMaxY)
            {
                y = Ds4TouchMaxY;
            }

            byte contactAndId = isTouching
                ? (byte)(fingerId & 0x7F)
                : (byte)(0x80 | (fingerId & 0x7F));

            buffer[offset] = contactAndId;
            buffer[offset + 1] = (byte)(x & 0xFF);
            buffer[offset + 2] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
            buffer[offset + 3] = (byte)((y >> 4) & 0xFF);
        }

        private static byte MapDPadNibble(ushort buttons)
        {
            bool up = (buttons & XINPUT_GAMEPAD_DPAD_UP) != 0;
            bool down = (buttons & XINPUT_GAMEPAD_DPAD_DOWN) != 0;
            bool left = (buttons & XINPUT_GAMEPAD_DPAD_LEFT) != 0;
            bool right = (buttons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0;

            if (up && right) return 1;
            if (right && down) return 3;
            if (down && left) return 5;
            if (left && up) return 7;
            if (up) return 0;
            if (right) return 2;
            if (down) return 4;
            if (left) return 6;
            return 8;
        }

        private static byte ConvertXInputAxisToDs4(short value)
        {
            // Maps -32768..32767 to 0..255
            int normalized = (value + 32768) >> 8;
            if (normalized < 0) normalized = 0;
            if (normalized > 255) normalized = 255;
            return (byte)normalized;
        }

        private static byte ConvertXInputAxisToDs4Inverted(short value)
        {
            return (byte)(255 - ConvertXInputAxisToDs4(value));
        }

        private void OnXbox360FeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
        {
            try
            {
                RumbleReceived?.Invoke(e.LargeMotor, e.SmallMotor);
            }
            catch (Exception ex)
            {
                Logger.Debug($"ViGEmController: Xbox feedback dispatch failed - {ex.Message}");
            }
        }

        private void OnDualShock4FeedbackReceived(object sender, DualShock4FeedbackReceivedEventArgs e)
        {
            try
            {
                RumbleReceived?.Invoke(e.LargeMotor, e.SmallMotor);
            }
            catch (Exception ex)
            {
                Logger.Debug($"ViGEmController: DualShock4 feedback dispatch failed - {ex.Message}");
            }
        }

        private void DisconnectVirtualGamepad()
        {
            try { virtualGamepad?.Disconnect(); } catch { }

            if (xboxController != null)
            {
                try { xboxController.FeedbackReceived -= OnXbox360FeedbackReceived; } catch { }
            }

            if (dualShockController != null)
            {
                try { dualShockController.FeedbackReceived -= OnDualShock4FeedbackReceived; } catch { }
            }

            virtualGamepad = null;
            xboxController = null;
            dualShockController = null;
            dualShockTimestamp = 0;
            dualShockTouchPacketNumber = 0;
            dualShockTouchFingerId = 0;
            dualShockTouchWasActive = false;
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            DisconnectVirtualGamepad();
            try { client?.Dispose(); } catch { }

            client = null;
            isConnected = false;
            Logger.Info("ViGEmController: Disposed");
        }
    }
}
