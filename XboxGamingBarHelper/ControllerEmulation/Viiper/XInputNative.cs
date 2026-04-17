using System.Runtime.InteropServices;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// P/Invoke declarations for XInput 1.4 (physical controller reading).
    /// Namespaced to avoid conflict with XInput types already defined elsewhere
    /// in the helper (ControllerEmulationManager uses its own XINPUT_STATE layout).
    /// </summary>
    internal static class ViiperXInput
    {
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        public static extern uint GetState(uint dwUserIndex, ref ViiperXInputState pState);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        public static extern uint SetState(uint dwUserIndex, ref ViiperXInputVibration pVibration);

        public const uint ErrorSuccess = 0;
        public const uint ErrorDeviceNotConnected = 1167;

        // Button flags
        public const ushort DPadUp = 0x0001;
        public const ushort DPadDown = 0x0002;
        public const ushort DPadLeft = 0x0004;
        public const ushort DPadRight = 0x0008;
        public const ushort Start = 0x0010;
        public const ushort Back = 0x0020;
        public const ushort LeftThumb = 0x0040;
        public const ushort RightThumb = 0x0080;
        public const ushort LB = 0x0100;
        public const ushort RB = 0x0200;
        public const ushort Guide = 0x0400;
        public const ushort A = 0x1000;
        public const ushort B = 0x2000;
        public const ushort X = 0x4000;
        public const ushort Y = 0x8000;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ViiperXInputState
    {
        public uint PacketNumber;
        public ViiperXInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ViiperXInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ViiperXInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }
}
