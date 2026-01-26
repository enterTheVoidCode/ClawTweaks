// =============================================================================
// GPDWin5Keycodes.cs
//
// USB HID keycode definitions for GPD Win 5 button remapping.
// Based on USB HID Usage Tables specification and GPD Win 5 protocol captures.
//
// Reference: https://usb.org/sites/default/files/hut1_21.pdf
// =============================================================================

using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.Libraries.GPD
{
    /// <summary>
    /// USB HID keycode definitions for GPD Win 5.
    /// All keycodes are 16-bit little-endian values.
    /// </summary>
    public static class GPDWin5Keycodes
    {
        #region Special Keys

        /// <summary>No key / disabled</summary>
        public const ushort NONE = 0x0000;

        #endregion

        #region Letter Keys (0x04 - 0x1D)

        public const ushort KEY_A = 0x0004;
        public const ushort KEY_B = 0x0005;
        public const ushort KEY_C = 0x0006;
        public const ushort KEY_D = 0x0007;
        public const ushort KEY_E = 0x0008;
        public const ushort KEY_F = 0x0009;
        public const ushort KEY_G = 0x000A;
        public const ushort KEY_H = 0x000B;
        public const ushort KEY_I = 0x000C;
        public const ushort KEY_J = 0x000D;
        public const ushort KEY_K = 0x000E;
        public const ushort KEY_L = 0x000F;
        public const ushort KEY_M = 0x0010;
        public const ushort KEY_N = 0x0011;
        public const ushort KEY_O = 0x0012;
        public const ushort KEY_P = 0x0013;
        public const ushort KEY_Q = 0x0014;
        public const ushort KEY_R = 0x0015;
        public const ushort KEY_S = 0x0016;
        public const ushort KEY_T = 0x0017;
        public const ushort KEY_U = 0x0018;
        public const ushort KEY_V = 0x0019;
        public const ushort KEY_W = 0x001A;
        public const ushort KEY_X = 0x001B;
        public const ushort KEY_Y = 0x001C;
        public const ushort KEY_Z = 0x001D;

        #endregion

        #region Number Keys (0x1E - 0x27)

        public const ushort KEY_1 = 0x001E;
        public const ushort KEY_2 = 0x001F;
        public const ushort KEY_3 = 0x0020;
        public const ushort KEY_4 = 0x0021;
        public const ushort KEY_5 = 0x0022;
        public const ushort KEY_6 = 0x0023;
        public const ushort KEY_7 = 0x0024;
        public const ushort KEY_8 = 0x0025;
        public const ushort KEY_9 = 0x0026;
        public const ushort KEY_0 = 0x0027;

        #endregion

        #region Control Keys (0x28 - 0x38)

        public const ushort KEY_ENTER = 0x0028;
        public const ushort KEY_ESC = 0x0029;
        public const ushort KEY_BACKSPACE = 0x002A;
        public const ushort KEY_TAB = 0x002B;
        public const ushort KEY_SPACE = 0x002C;
        public const ushort KEY_MINUS = 0x002D;
        public const ushort KEY_EQUALS = 0x002E;
        public const ushort KEY_LEFTBRACKET = 0x002F;
        public const ushort KEY_RIGHTBRACKET = 0x0030;
        public const ushort KEY_BACKSLASH = 0x0031;
        public const ushort KEY_SEMICOLON = 0x0033;
        public const ushort KEY_APOSTROPHE = 0x0034;
        public const ushort KEY_GRAVE = 0x0035;
        public const ushort KEY_COMMA = 0x0036;
        public const ushort KEY_PERIOD = 0x0037;
        public const ushort KEY_SLASH = 0x0038;

        #endregion

        #region Function Keys (0x39 - 0x45)

        public const ushort KEY_CAPSLOCK = 0x0039;
        public const ushort KEY_F1 = 0x003A;
        public const ushort KEY_F2 = 0x003B;
        public const ushort KEY_F3 = 0x003C;
        public const ushort KEY_F4 = 0x003D;
        public const ushort KEY_F5 = 0x003E;
        public const ushort KEY_F6 = 0x003F;
        public const ushort KEY_F7 = 0x0040;
        public const ushort KEY_F8 = 0x0041;
        public const ushort KEY_F9 = 0x0042;
        public const ushort KEY_F10 = 0x0043;
        public const ushort KEY_F11 = 0x0044;
        public const ushort KEY_F12 = 0x0045;

        #endregion

        #region Special Keys (0x46 - 0x4E)

        public const ushort KEY_PRINTSCREEN = 0x0046;
        public const ushort KEY_SCROLLLOCK = 0x0047;
        public const ushort KEY_PAUSE = 0x0048;
        public const ushort KEY_INSERT = 0x0049;
        public const ushort KEY_HOME = 0x004A;
        public const ushort KEY_PAGEUP = 0x004B;
        public const ushort KEY_DELETE = 0x004C;
        public const ushort KEY_END = 0x004D;
        public const ushort KEY_PAGEDOWN = 0x004E;

        #endregion

        #region Arrow Keys (0x4F - 0x52)

        public const ushort KEY_RIGHT = 0x004F;
        public const ushort KEY_LEFT = 0x0050;
        public const ushort KEY_DOWN = 0x0051;
        public const ushort KEY_UP = 0x0052;

        #endregion

        #region Numpad Keys (0x53 - 0x63)

        public const ushort KEY_NUMLOCK = 0x0053;
        public const ushort KEY_KP_DIVIDE = 0x0054;
        public const ushort KEY_KP_MULTIPLY = 0x0055;
        public const ushort KEY_KP_MINUS = 0x0056;
        public const ushort KEY_KP_PLUS = 0x0057;
        public const ushort KEY_KP_ENTER = 0x0058;
        public const ushort KEY_KP_1 = 0x0059;
        public const ushort KEY_KP_2 = 0x005A;
        public const ushort KEY_KP_3 = 0x005B;
        public const ushort KEY_KP_4 = 0x005C;
        public const ushort KEY_KP_5 = 0x005D;
        public const ushort KEY_KP_6 = 0x005E;
        public const ushort KEY_KP_7 = 0x005F;
        public const ushort KEY_KP_8 = 0x0060;
        public const ushort KEY_KP_9 = 0x0061;
        public const ushort KEY_KP_0 = 0x0062;
        public const ushort KEY_KP_DECIMAL = 0x0063;

        #endregion

        #region Modifier Keys (0xE0 - 0xE7)

        public const ushort KEY_LEFTCTRL = 0x00E0;
        public const ushort KEY_LEFTSHIFT = 0x00E1;
        public const ushort KEY_LEFTALT = 0x00E2;
        public const ushort KEY_LEFTMETA = 0x00E3;   // Windows key
        public const ushort KEY_RIGHTCTRL = 0x00E4;
        public const ushort KEY_RIGHTSHIFT = 0x00E5;
        public const ushort KEY_RIGHTALT = 0x00E6;
        public const ushort KEY_RIGHTMETA = 0x00E7;  // Windows key (right)

        #endregion

        #region GPD Custom Mouse Codes (0xE8 - 0xED)

        /// <summary>Mouse wheel scroll up</summary>
        public const ushort MOUSE_WHEELUP = 0x00E8;

        /// <summary>Mouse wheel scroll down</summary>
        public const ushort MOUSE_WHEELDOWN = 0x00E9;

        /// <summary>Mouse left click</summary>
        public const ushort MOUSE_LEFT = 0x00EA;

        /// <summary>Mouse right click</summary>
        public const ushort MOUSE_RIGHT = 0x00EB;

        /// <summary>Mouse middle click</summary>
        public const ushort MOUSE_MIDDLE = 0x00EC;

        /// <summary>Mouse fast mode (DPI boost)</summary>
        public const ushort MOUSE_FAST = 0x00ED;

        #endregion

        #region Gamepad Codes (0x80xx)

        /// <summary>Gamepad D-Pad Up</summary>
        public const ushort GAMEPAD_DPAD_UP = 0x8000;

        /// <summary>Gamepad D-Pad Down</summary>
        public const ushort GAMEPAD_DPAD_DOWN = 0x8001;

        /// <summary>Gamepad D-Pad Left</summary>
        public const ushort GAMEPAD_DPAD_LEFT = 0x8002;

        /// <summary>Gamepad D-Pad Right</summary>
        public const ushort GAMEPAD_DPAD_RIGHT = 0x8003;

        /// <summary>Gamepad Start button</summary>
        public const ushort GAMEPAD_START = 0x8004;

        /// <summary>Gamepad Back/Select button</summary>
        public const ushort GAMEPAD_SELECT = 0x8005;

        /// <summary>Gamepad Guide/Xbox button</summary>
        public const ushort GAMEPAD_GUIDE = 0x8006;

        /// <summary>Gamepad A button</summary>
        public const ushort GAMEPAD_A = 0x8007;

        /// <summary>Gamepad B button</summary>
        public const ushort GAMEPAD_B = 0x8008;

        /// <summary>Gamepad X button</summary>
        public const ushort GAMEPAD_X = 0x8009;

        /// <summary>Gamepad Y button</summary>
        public const ushort GAMEPAD_Y = 0x800A;

        /// <summary>Gamepad Left Bumper (LB)</summary>
        public const ushort GAMEPAD_LB = 0x800B;

        /// <summary>Gamepad Right Bumper (RB)</summary>
        public const ushort GAMEPAD_RB = 0x800C;

        /// <summary>Gamepad Left Trigger (LT)</summary>
        public const ushort GAMEPAD_LT = 0x800D;

        /// <summary>Gamepad Right Trigger (RT)</summary>
        public const ushort GAMEPAD_RT = 0x800E;

        /// <summary>Gamepad Left Stick Click (L3)</summary>
        public const ushort GAMEPAD_L3 = 0x800F;

        /// <summary>Gamepad Right Stick Click (R3)</summary>
        public const ushort GAMEPAD_R3 = 0x8010;

        #endregion

        #region Keycode Name Lookup

        /// <summary>
        /// Dictionary mapping keycodes to human-readable names.
        /// </summary>
        private static readonly Dictionary<ushort, string> KeycodeNames = new Dictionary<ushort, string>
        {
            // Special
            { NONE, "None" },

            // Letters
            { KEY_A, "A" }, { KEY_B, "B" }, { KEY_C, "C" }, { KEY_D, "D" },
            { KEY_E, "E" }, { KEY_F, "F" }, { KEY_G, "G" }, { KEY_H, "H" },
            { KEY_I, "I" }, { KEY_J, "J" }, { KEY_K, "K" }, { KEY_L, "L" },
            { KEY_M, "M" }, { KEY_N, "N" }, { KEY_O, "O" }, { KEY_P, "P" },
            { KEY_Q, "Q" }, { KEY_R, "R" }, { KEY_S, "S" }, { KEY_T, "T" },
            { KEY_U, "U" }, { KEY_V, "V" }, { KEY_W, "W" }, { KEY_X, "X" },
            { KEY_Y, "Y" }, { KEY_Z, "Z" },

            // Numbers
            { KEY_1, "1" }, { KEY_2, "2" }, { KEY_3, "3" }, { KEY_4, "4" },
            { KEY_5, "5" }, { KEY_6, "6" }, { KEY_7, "7" }, { KEY_8, "8" },
            { KEY_9, "9" }, { KEY_0, "0" },

            // Control
            { KEY_ENTER, "Enter" }, { KEY_ESC, "Escape" }, { KEY_BACKSPACE, "Backspace" },
            { KEY_TAB, "Tab" }, { KEY_SPACE, "Space" }, { KEY_MINUS, "-" },
            { KEY_EQUALS, "=" }, { KEY_LEFTBRACKET, "[" }, { KEY_RIGHTBRACKET, "]" },
            { KEY_BACKSLASH, "\\" }, { KEY_SEMICOLON, ";" }, { KEY_APOSTROPHE, "'" },
            { KEY_GRAVE, "`" }, { KEY_COMMA, "," }, { KEY_PERIOD, "." }, { KEY_SLASH, "/" },

            // Function
            { KEY_CAPSLOCK, "CapsLock" },
            { KEY_F1, "F1" }, { KEY_F2, "F2" }, { KEY_F3, "F3" }, { KEY_F4, "F4" },
            { KEY_F5, "F5" }, { KEY_F6, "F6" }, { KEY_F7, "F7" }, { KEY_F8, "F8" },
            { KEY_F9, "F9" }, { KEY_F10, "F10" }, { KEY_F11, "F11" }, { KEY_F12, "F12" },

            // Special
            { KEY_PRINTSCREEN, "PrintScreen" }, { KEY_SCROLLLOCK, "ScrollLock" },
            { KEY_PAUSE, "Pause" }, { KEY_INSERT, "Insert" }, { KEY_HOME, "Home" },
            { KEY_PAGEUP, "PageUp" }, { KEY_DELETE, "Delete" }, { KEY_END, "End" },
            { KEY_PAGEDOWN, "PageDown" },

            // Arrow
            { KEY_UP, "Up" }, { KEY_DOWN, "Down" }, { KEY_LEFT, "Left" }, { KEY_RIGHT, "Right" },

            // Numpad
            { KEY_NUMLOCK, "NumLock" }, { KEY_KP_DIVIDE, "NumPad /" },
            { KEY_KP_MULTIPLY, "NumPad *" }, { KEY_KP_MINUS, "NumPad -" },
            { KEY_KP_PLUS, "NumPad +" }, { KEY_KP_ENTER, "NumPad Enter" },
            { KEY_KP_1, "NumPad 1" }, { KEY_KP_2, "NumPad 2" }, { KEY_KP_3, "NumPad 3" },
            { KEY_KP_4, "NumPad 4" }, { KEY_KP_5, "NumPad 5" }, { KEY_KP_6, "NumPad 6" },
            { KEY_KP_7, "NumPad 7" }, { KEY_KP_8, "NumPad 8" }, { KEY_KP_9, "NumPad 9" },
            { KEY_KP_0, "NumPad 0" }, { KEY_KP_DECIMAL, "NumPad ." },

            // Modifiers
            { KEY_LEFTCTRL, "Left Ctrl" }, { KEY_LEFTSHIFT, "Left Shift" },
            { KEY_LEFTALT, "Left Alt" }, { KEY_LEFTMETA, "Left Win" },
            { KEY_RIGHTCTRL, "Right Ctrl" }, { KEY_RIGHTSHIFT, "Right Shift" },
            { KEY_RIGHTALT, "Right Alt" }, { KEY_RIGHTMETA, "Right Win" },

            // Mouse
            { MOUSE_WHEELUP, "Mouse Wheel Up" }, { MOUSE_WHEELDOWN, "Mouse Wheel Down" },
            { MOUSE_LEFT, "Mouse Left" }, { MOUSE_RIGHT, "Mouse Right" },
            { MOUSE_MIDDLE, "Mouse Middle" }, { MOUSE_FAST, "Mouse Fast" },

            // Gamepad
            { GAMEPAD_DPAD_UP, "Gamepad D-Pad Up" }, { GAMEPAD_DPAD_DOWN, "Gamepad D-Pad Down" },
            { GAMEPAD_DPAD_LEFT, "Gamepad D-Pad Left" }, { GAMEPAD_DPAD_RIGHT, "Gamepad D-Pad Right" },
            { GAMEPAD_START, "Gamepad Start" }, { GAMEPAD_SELECT, "Gamepad Select" },
            { GAMEPAD_GUIDE, "Gamepad Guide" },
            { GAMEPAD_A, "Gamepad A" }, { GAMEPAD_B, "Gamepad B" },
            { GAMEPAD_X, "Gamepad X" }, { GAMEPAD_Y, "Gamepad Y" },
            { GAMEPAD_LB, "Gamepad LB" }, { GAMEPAD_RB, "Gamepad RB" },
            { GAMEPAD_LT, "Gamepad LT" }, { GAMEPAD_RT, "Gamepad RT" },
            { GAMEPAD_L3, "Gamepad L3" }, { GAMEPAD_R3, "Gamepad R3" },
        };

        /// <summary>
        /// Gets the human-readable name for a keycode.
        /// </summary>
        /// <param name="keycode">The USB HID keycode.</param>
        /// <returns>Human-readable name, or hex code if unknown.</returns>
        public static string GetKeyName(ushort keycode)
        {
            if (KeycodeNames.TryGetValue(keycode, out string name))
                return name;
            return $"0x{keycode:X4}";
        }

        /// <summary>
        /// Gets the keycode for a key name.
        /// </summary>
        /// <param name="name">The key name.</param>
        /// <returns>Keycode, or NONE if not found.</returns>
        public static ushort GetKeycode(string name)
        {
            foreach (var kvp in KeycodeNames)
            {
                if (kvp.Value.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                    return kvp.Key;
            }
            return NONE;
        }

        /// <summary>
        /// Returns all available key mappings.
        /// </summary>
        public static IReadOnlyDictionary<ushort, string> GetAllKeys()
        {
            return KeycodeNames;
        }

        #endregion

        #region Win 5 Button Definitions

        /// <summary>
        /// Defines mappable buttons on the GPD Win 5.
        /// </summary>
        public enum Win5Button
        {
            /// <summary>D-pad Up (offset 0x14-0x15)</summary>
            DPadUp = 0,
            /// <summary>D-pad Down (offset 0x16-0x17)</summary>
            DPadDown = 1,
            /// <summary>D-pad Left (offset 0x18-0x19)</summary>
            DPadLeft = 2,
            /// <summary>D-pad Right (offset 0x1a-0x1b)</summary>
            DPadRight = 3,
            /// <summary>A button (offset 0x22-0x23)</summary>
            ButtonA = 7,
            /// <summary>B button (offset 0x24-0x25)</summary>
            ButtonB = 8,
            /// <summary>X button (offset 0x26-0x27)</summary>
            ButtonX = 9,
            /// <summary>Y button (offset 0x28-0x29)</summary>
            ButtonY = 10,
            /// <summary>L3 / Left stick click (offset 0x2c-0x2d)</summary>
            L3 = 12,
            /// <summary>R3 / Right stick click (offset 0x2e-0x2f)</summary>
            R3 = 13,
            /// <summary>Left stick Up (offset 0x30-0x31)</summary>
            LStickUp = 14,
            /// <summary>Left stick Down (offset 0x32-0x33)</summary>
            LStickDown = 15,
            /// <summary>Left stick Left (offset 0x34-0x35)</summary>
            LStickLeft = 16,
            /// <summary>Left stick Right (offset 0x3e-0x3f)</summary>
            LStickRight = 21,
            /// <summary>L4 back paddle (separate packet)</summary>
            L4Paddle = 100,
            /// <summary>R4 back paddle (separate packet)</summary>
            R4Paddle = 101,
        }

        /// <summary>
        /// Gets the byte offset for a button within the configuration packet.
        /// </summary>
        /// <param name="button">The button.</param>
        /// <returns>Byte offset from start of packet, or -1 if not applicable.</returns>
        public static int GetButtonOffset(Win5Button button)
        {
            switch (button)
            {
                case Win5Button.DPadUp: return 0x14;
                case Win5Button.DPadDown: return 0x16;
                case Win5Button.DPadLeft: return 0x18;
                case Win5Button.DPadRight: return 0x1A;
                case Win5Button.ButtonA: return 0x22;
                case Win5Button.ButtonB: return 0x24;
                case Win5Button.ButtonX: return 0x26;
                case Win5Button.ButtonY: return 0x28;
                case Win5Button.L3: return 0x2C;
                case Win5Button.R3: return 0x2E;
                case Win5Button.LStickUp: return 0x30;
                case Win5Button.LStickDown: return 0x32;
                case Win5Button.LStickLeft: return 0x34;
                case Win5Button.LStickRight: return 0x3E;
                default: return -1; // Paddles use different packets
            }
        }

        /// <summary>
        /// Gets the default keycode for a Win 5 button.
        /// </summary>
        /// <param name="button">The button.</param>
        /// <returns>Default keycode mapping.</returns>
        public static ushort GetDefaultKeycode(Win5Button button)
        {
            switch (button)
            {
                case Win5Button.DPadUp: return KEY_UP;
                case Win5Button.DPadDown: return KEY_DOWN;
                case Win5Button.DPadLeft: return KEY_LEFT;
                case Win5Button.DPadRight: return KEY_RIGHT;
                case Win5Button.ButtonA: return KEY_LEFTMETA;
                case Win5Button.ButtonB: return KEY_A;
                case Win5Button.ButtonX: return KEY_G;
                case Win5Button.ButtonY: return KEY_C;
                case Win5Button.L3: return MOUSE_LEFT;
                case Win5Button.R3: return MOUSE_RIGHT;
                case Win5Button.LStickUp: return KEY_SPACE;
                case Win5Button.LStickDown: return KEY_LEFTCTRL;
                case Win5Button.LStickLeft: return KEY_Z;
                case Win5Button.LStickRight: return KEY_LEFTSHIFT;
                default: return NONE;
            }
        }

        #endregion
    }
}
