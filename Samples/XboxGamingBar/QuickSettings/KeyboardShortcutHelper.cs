using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NLog;

namespace XboxGamingBar.QuickSettings
{
    /// <summary>
    /// Helper class to send keyboard shortcuts to the system
    /// </summary>
    public static class KeyboardShortcutHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Virtual key codes
        private const int VK_CONTROL = 0x11;
        private const int VK_ALT = 0x12;
        private const int VK_SHIFT = 0x10;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        // Input flags
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        // Extended keys that need the EXTENDEDKEY flag
        private static readonly HashSet<int> ExtendedKeys = new HashSet<int>
        {
            0x21, 0x22, 0x23, 0x24, // PageUp, PageDown, End, Home
            0x25, 0x26, 0x27, 0x28, // Arrow keys
            0x2D, 0x2E,             // Insert, Delete
            0x5B, 0x5C,             // Win keys
            0x6F,                   // NumpadDivide
            0x90,                   // NumLock
            0x91,                   // ScrollLock
            0x2C,                   // PrintScreen
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const int INPUT_KEYBOARD = 1;

        /// <summary>
        /// Dictionary mapping key names to virtual key codes
        /// </summary>
        private static readonly Dictionary<string, int> KeyNameToVK = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // Function keys
            { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
            { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 },
            { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B },

            // Special keys
            { "Enter", 0x0D }, { "Return", 0x0D },
            { "Tab", 0x09 },
            { "Escape", 0x1B }, { "Esc", 0x1B },
            { "Space", 0x20 }, { "Spacebar", 0x20 },
            { "Backspace", 0x08 }, { "Back", 0x08 },
            { "Delete", 0x2E }, { "Del", 0x2E },
            { "Insert", 0x2D }, { "Ins", 0x2D },
            { "Home", 0x24 },
            { "End", 0x23 },
            { "PageUp", 0x21 }, { "PgUp", 0x21 },
            { "PageDown", 0x22 }, { "PgDn", 0x22 },

            // Arrow keys
            { "Up", 0x26 }, { "Down", 0x28 }, { "Left", 0x25 }, { "Right", 0x27 },

            // Numpad
            { "Num0", 0x60 }, { "Num1", 0x61 }, { "Num2", 0x62 }, { "Num3", 0x63 },
            { "Num4", 0x64 }, { "Num5", 0x65 }, { "Num6", 0x66 }, { "Num7", 0x67 },
            { "Num8", 0x68 }, { "Num9", 0x69 },
            { "NumLock", 0x90 },
            { "NumMultiply", 0x6A }, { "NumAdd", 0x6B }, { "NumSubtract", 0x6D },
            { "NumDecimal", 0x6E }, { "NumDivide", 0x6F },

            // Media keys
            { "MediaPlayPause", 0xB3 }, { "MediaStop", 0xB2 },
            { "MediaNext", 0xB0 }, { "MediaPrev", 0xB1 },
            { "VolumeUp", 0xAF }, { "VolumeDown", 0xAE }, { "VolumeMute", 0xAD },

            // Other
            { "PrintScreen", 0x2C }, { "PrtSc", 0x2C },
            { "ScrollLock", 0x91 },
            { "Pause", 0x13 }, { "Break", 0x13 },
            { "CapsLock", 0x14 },

            // Number keys (top row)
            { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 },
            { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },

            // Letter keys
            { "A", 0x41 }, { "B", 0x42 }, { "C", 0x43 }, { "D", 0x44 }, { "E", 0x45 },
            { "F", 0x46 }, { "G", 0x47 }, { "H", 0x48 }, { "I", 0x49 }, { "J", 0x4A },
            { "K", 0x4B }, { "L", 0x4C }, { "M", 0x4D }, { "N", 0x4E }, { "O", 0x4F },
            { "P", 0x50 }, { "Q", 0x51 }, { "R", 0x52 }, { "S", 0x53 }, { "T", 0x54 },
            { "U", 0x55 }, { "V", 0x56 }, { "W", 0x57 }, { "X", 0x58 }, { "Y", 0x59 },
            { "Z", 0x5A },

            // Symbols
            { "Plus", 0xBB }, { "Minus", 0xBD }, { "Equals", 0xBB },
            { "LeftBracket", 0xDB }, { "[", 0xDB }, { "RightBracket", 0xDD }, { "]", 0xDD },
            { "Backslash", 0xDC }, { "Semicolon", 0xBA }, { "Quote", 0xDE },
            { "Comma", 0xBC }, { "Period", 0xBE }, { "Slash", 0xBF },
            { "Backtick", 0xC0 }, { "Tilde", 0xC0 },
        };

        [DllImport("kernel32.dll")]
        private static extern void Sleep(uint dwMilliseconds);

        /// <summary>
        /// Parse and send a keyboard shortcut string (e.g., "Ctrl+Shift+S", "Alt+F4", "Win+G")
        /// </summary>
        /// <param name="shortcut">The shortcut string to parse and send</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SendShortcut(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                Logger.Warn("Empty shortcut string provided");
                return false;
            }

            try
            {
                // Parse the shortcut
                var parts = shortcut.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
                var modifiers = new List<int>();
                int mainKey = 0;

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    var upper = trimmed.ToUpperInvariant();

                    // Check for modifiers
                    if (upper == "CTRL" || upper == "CONTROL")
                    {
                        modifiers.Add(VK_CONTROL);
                    }
                    else if (upper == "ALT")
                    {
                        modifiers.Add(VK_ALT);
                    }
                    else if (upper == "SHIFT")
                    {
                        modifiers.Add(VK_SHIFT);
                    }
                    else if (upper == "WIN" || upper == "WINDOWS" || upper == "LWIN")
                    {
                        modifiers.Add(VK_LWIN);
                    }
                    else if (upper == "RWIN")
                    {
                        modifiers.Add(VK_RWIN);
                    }
                    else
                    {
                        // Try to get the key code
                        mainKey = GetVirtualKeyCode(trimmed);
                        if (mainKey == 0)
                        {
                            Logger.Warn($"Unknown key in shortcut: {trimmed}");
                            return false;
                        }
                    }
                }

                if (mainKey == 0 && modifiers.Count == 0)
                {
                    Logger.Warn($"No valid keys found in shortcut: {shortcut}");
                    return false;
                }

                // Send inputs one at a time with small delays for reliability
                // This is especially important for Alt+Tab which triggers the task switcher

                // Press modifiers
                foreach (var mod in modifiers)
                {
                    SendSingleKey((ushort)mod, false);
                    Sleep(10); // Small delay to ensure key is registered
                }

                // Press and release main key (if any)
                if (mainKey != 0)
                {
                    SendSingleKey((ushort)mainKey, false);
                    Sleep(10);
                    SendSingleKey((ushort)mainKey, true);
                    Sleep(10);
                }

                // Release modifiers (in reverse order)
                for (int i = modifiers.Count - 1; i >= 0; i--)
                {
                    SendSingleKey((ushort)modifiers[i], true);
                    Sleep(10);
                }

                // Extra delay after releasing to ensure all keys are properly released
                Sleep(50);

                Logger.Info($"Sent keyboard shortcut: {shortcut}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending keyboard shortcut '{shortcut}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send a single key press or release
        /// </summary>
        private static bool SendSingleKey(ushort vk, bool keyUp)
        {
            var input = CreateKeyInput(vk, keyUp);
            var inputs = new INPUT[] { input };
            var result = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
            return result == 1;
        }

        /// <summary>
        /// Get virtual key code for a key name or character
        /// </summary>
        private static int GetVirtualKeyCode(string keyName)
        {
            // First check our dictionary
            if (KeyNameToVK.TryGetValue(keyName, out int vk))
            {
                return vk;
            }

            // If single character, use VkKeyScan
            if (keyName.Length == 1)
            {
                short result = VkKeyScan(keyName[0]);
                if (result != -1)
                {
                    return result & 0xFF; // Low byte is the VK code
                }
            }

            return 0;
        }

        /// <summary>
        /// Create a keyboard input structure
        /// </summary>
        private static INPUT CreateKeyInput(ushort vk, bool keyUp)
        {
            uint flags = keyUp ? KEYEVENTF_KEYUP : 0;

            // Add extended key flag for keys that need it
            if (ExtendedKeys.Contains(vk))
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            return new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        /// <summary>
        /// Launch the on-screen keyboard
        /// </summary>
        public static void LaunchOnScreenKeyboard()
        {
            try
            {
                // Try tabtip.exe (Windows 10/11 touch keyboard)
                var tabtipPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                    "microsoft shared", "ink", "TabTip.exe");

                if (System.IO.File.Exists(tabtipPath))
                {
                    System.Diagnostics.Process.Start(tabtipPath);
                    Logger.Info("Launched TabTip.exe (touch keyboard)");
                    return;
                }

                // Fallback to osk.exe (classic on-screen keyboard)
                System.Diagnostics.Process.Start("osk.exe");
                Logger.Info("Launched osk.exe (on-screen keyboard)");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching on-screen keyboard: {ex.Message}");
            }
        }
    }
}
