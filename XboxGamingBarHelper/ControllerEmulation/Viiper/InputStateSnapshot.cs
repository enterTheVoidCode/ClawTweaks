using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// A point-in-time snapshot of controller input state (parsed + raw bytes).
    /// Used by the VIIPER forwarding loop as the translation input.
    /// </summary>
    internal sealed class InputStateSnapshot
    {
        // Buttons (XInput bitfield)
        public ushort Buttons { get; set; }

        // Triggers
        public byte LeftTrigger { get; set; }
        public byte RightTrigger { get; set; }

        // Thumbsticks
        public short ThumbLX { get; set; }
        public short ThumbLY { get; set; }
        public short ThumbRX { get; set; }
        public short ThumbRY { get; set; }

        // IMU input (raw parsed values from Legion Go HID or similar)
        public bool HasImuData { get; set; }
        public short GyroX { get; set; }
        public short GyroY { get; set; }
        public short GyroZ { get; set; }
        public short AccelX { get; set; }
        public short AccelY { get; set; }
        public short AccelZ { get; set; }
        public bool ImuIsHqMode { get; set; }

        // IMU output (scaled values sent to the virtual device)
        public short GyroOutX { get; set; }
        public short GyroOutY { get; set; }
        public short GyroOutZ { get; set; }
        public short AccelOutX { get; set; }
        public short AccelOutY { get; set; }
        public short AccelOutZ { get; set; }

        // Right controller IMU (for Joy-Con pair: separate IMU per side)
        public bool HasRightImuData { get; set; }
        public short GyroXRight { get; set; }
        public short GyroYRight { get; set; }
        public short GyroZRight { get; set; }
        public short AccelXRight { get; set; }
        public short AccelYRight { get; set; }
        public short AccelZRight { get; set; }
        public short GyroOutXRight { get; set; }
        public short GyroOutYRight { get; set; }
        public short GyroOutZRight { get; set; }
        public short AccelOutXRight { get; set; }
        public short AccelOutYRight { get; set; }
        public short AccelOutZRight { get; set; }

        // Touchpad (from Legion Go HID)
        public bool HasTouchpad { get; set; }
        public bool RightPadTouch { get; set; }
        public bool RightPadPress { get; set; }
        public ushort RightPadX { get; set; }
        public ushort RightPadY { get; set; }

        // Auxiliary buttons (Legion paddles, etc.)
        public ushort AuxButtons { get; set; }

        // Input source identifier
        public string InputSource { get; set; } = "XInput";

        // Raw bytes forwarded to the virtual device
        public byte[] RawBytes { get; set; } = new byte[0];

        // Raw source bytes read from the input device (e.g. Legion Go HID report)
        public byte[] SourceRawBytes { get; set; } = new byte[0];

        // Legion HID report layout info
        public bool IsLegionHid { get; set; }
        public bool IsInitMode { get; set; }
        public int DataOffset { get; set; }

        // Timestamp
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets a list of pressed button names.
        /// </summary>
        public IReadOnlyList<string> GetPressedButtons()
        {
            var list = new List<string>();
            if ((Buttons & 0x1000) != 0) list.Add("A");
            if ((Buttons & 0x2000) != 0) list.Add("B");
            if ((Buttons & 0x4000) != 0) list.Add("X");
            if ((Buttons & 0x8000) != 0) list.Add("Y");
            if ((Buttons & 0x0100) != 0) list.Add("LB");
            if ((Buttons & 0x0200) != 0) list.Add("RB");
            if ((Buttons & 0x0010) != 0) list.Add("Start");
            if ((Buttons & 0x0020) != 0) list.Add("Back");
            if ((Buttons & 0x0400) != 0) list.Add("Guide");
            if ((Buttons & 0x0040) != 0) list.Add("LS");
            if ((Buttons & 0x0080) != 0) list.Add("RS");
            if ((Buttons & 0x0001) != 0) list.Add("Up");
            if ((Buttons & 0x0002) != 0) list.Add("Down");
            if ((Buttons & 0x0004) != 0) list.Add("Left");
            if ((Buttons & 0x0008) != 0) list.Add("Right");
            if ((AuxButtons & 0x0001) != 0) list.Add("Y1");
            if ((AuxButtons & 0x0002) != 0) list.Add("Y2");
            if ((AuxButtons & 0x0004) != 0) list.Add("Y3");
            if ((AuxButtons & 0x0008) != 0) list.Add("M3");
            if ((AuxButtons & 0x0010) != 0) list.Add("M1");
            if ((AuxButtons & 0x0020) != 0) list.Add("M2");
            if ((AuxButtons & 0x0040) != 0) list.Add("Mode");
            if ((AuxButtons & 0x0080) != 0) list.Add("Share");
            if ((AuxButtons & 0x0100) != 0) list.Add("FrTop");
            if ((AuxButtons & 0x0200) != 0) list.Add("FrBot");
            return list;
        }
    }
}
