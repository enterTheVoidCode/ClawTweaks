using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace XboxGamingBarHelper.Windows
{
    internal class User32
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;

            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;

            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;

            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out IntPtr lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        // Delegate for the EnumWindows callback function
        private delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

        // DllImport for the EnumWindows function
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);

        // DllImport for IsWindowVisible
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // DllImport for GetShellWindow
        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwFlags);

        private static int GetWindowProcessId(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                // This is normal when no window has focus (UAC prompts, protected processes, etc.)
                Logger.Debug("Can't get process id from invalid window handle.");
                return -1;
            }

            GetWindowThreadProcessId(windowHandle, out IntPtr windowProcessId);
            if (windowProcessId == IntPtr.Zero)
            {
                Logger.Debug("Can't get window process id.");
                return -1;
            }

            return (int)windowProcessId;
        }

        public static string GetWindowTitle(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                Logger.Debug("Can't get window title from invalid window handle.");
                return string.Empty;
            }

            var length = GetWindowTextLength(windowHandle);
            if (length == 0)
            {
                //Logger.Error($"Window doesn't have window title??");
                return string.Empty;
            }

            var sb = new StringBuilder(length + 1);
            GetWindowText(windowHandle, sb, sb.Capacity);

            return sb.ToString();
        }

        /// <summary>
        /// Gets the process ID of the current foreground window.
        /// Returns -1 if no window has focus or process ID cannot be determined.
        /// </summary>
        public static int GetForegroundProcessId()
        {
            return GetWindowProcessId(GetForegroundWindow());
        }

        public static void GetOpenWindows(IDictionary<int, ProcessWindow> windows)
        {
            var shellWindow = GetShellWindow();
            var foregroundWindowProcessId = GetWindowProcessId(GetForegroundWindow());
            if (windows == null)
            {
                windows = new Dictionary<int, ProcessWindow>();
            }
            else
            {
                windows.Clear();
            }

            EnumWindows(delegate (IntPtr hWnd, int lParam)
            {
                // Exclude the shell window itself
                if (hWnd == shellWindow) return true;

                // Exclude invisible windows
                if (!IsWindowVisible(hWnd)) return true;

                var processId = GetWindowProcessId(hWnd);
                var windowTitle = GetWindowTitle(hWnd);
                if (processId == -1)
                {
                    // This is normal for certain system windows
                    return true; // Continue enumeration
                }

                try
                {
                    var process = Process.GetProcessById(processId);
                    string processPath = string.Empty;
                    string processName = string.Empty;
                    try
                    {
                        processName = process.ProcessName;
                    }
                    catch (Exception)
                    {
                        // Can't access ProcessName for protected processes
                    }
                    try
                    {
                        // MainModule access can throw Win32Exception for protected processes
                        // even with null-conditional operator
                        var mainModule = process.MainModule;
                        if (mainModule != null)
                        {
                            processPath = mainModule.FileName ?? string.Empty;
                        }
                    }
                    catch (Exception)
                    {
                        // Can't access MainModule for protected processes (anti-cheat, system processes)
                        // Continue with empty path - this is normal
                    }
                    windows[processId] = new ProcessWindow(processId, hWnd, windowTitle, processName, processPath, processId == foregroundWindowProcessId);
                }
                catch (Exception)
                {
                    // Process may have exited, skip it
                }
                return true; // Continue enumeration
            }, 0);
        }

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int CDS_TEST = 0x02;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DM_DISPLAYFREQUENCY = 0x400000;
        private const int DM_PELSWIDTH = 0x80000;
        private const int DM_PELSHEIGHT = 0x100000;

        public static int GetCurrentRefreshRate()
        {
            DEVMODE vDevMode = new DEVMODE();
            vDevMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref vDevMode))
            {
                return vDevMode.dmDisplayFrequency;
            }
            return 0; // failed
        }

        public static List<int> GetSupportedRefreshRates()
        {
            List<int> refreshRates = new List<int>();
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            int modeIndex = 0;
            while (EnumDisplaySettings(null, modeIndex++, ref devMode))
            {
                int rate = devMode.dmDisplayFrequency;
                if (rate > 0 && !refreshRates.Contains(rate))
                    refreshRates.Add(rate);
            }

            refreshRates.Sort();
            foreach (var refreshRate in refreshRates)
            {
                Logger.Info($"Found refresh rate {refreshRate}");
            }
            return refreshRates;
        }

        /// <summary>
        /// Set monitor refresh rate to a supported value.
        /// </summary>
        public static bool SetRefreshRateTo(int targetRate)
        {
            /*var supported = GetSupportedRefreshRates();
            if (!supported.Contains(targetRate))
            {
                Console.WriteLine($"Error: {targetRate}Hz is not supported. Supported rates: {string.Join(", ", supported)}");
                return false;
            }*/

            DEVMODE mode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };

            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode))
            {
                Console.WriteLine("Error: Could not retrieve current display settings.");
                return false;
            }

            mode.dmDisplayFrequency = targetRate;
            mode.dmFields = DM_DISPLAYFREQUENCY;

            // Test before applying
            int testResult = ChangeDisplaySettings(ref mode, CDS_TEST);
            if (testResult != DISP_CHANGE_SUCCESSFUL)
            {
                Console.WriteLine($"Test failed: {targetRate}Hz not valid on this mode.");
                return false;
            }

            // Apply permanently
            int result = ChangeDisplaySettings(ref mode, CDS_UPDATEREGISTRY);
            if (result == DISP_CHANGE_SUCCESSFUL)
            {
                Console.WriteLine($"Successfully switched to {targetRate}Hz.");
                return true;
            }
            else
            {
                Console.WriteLine($"Failed to apply {targetRate}Hz (error code {result}).");
                return false;
            }
        }

        public static string GetCurrentResolution()
        {
            DEVMODE vDevMode = new DEVMODE();
            vDevMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref vDevMode))
            {
                return $"{vDevMode.dmPelsWidth}x{vDevMode.dmPelsHeight}";
            }
            return "1920x1080"; // default fallback
        }

        public static List<string> GetSupportedResolutions()
        {
            var resolutions = new HashSet<string>();
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            // Find native resolution by highest total pixel count (width * height)
            // This correctly identifies 1920x1200 as native over 1920x1080
            int nativeWidth = 0;
            int nativeHeight = 0;
            long maxPixels = 0;
            int modeIndex = 0;

            // First pass: log all resolutions and find native
            var allResolutions = new List<(int w, int h)>();
            while (EnumDisplaySettings(null, modeIndex++, ref devMode))
            {
                int w = devMode.dmPelsWidth;
                int h = devMode.dmPelsHeight;
                allResolutions.Add((w, h));

                long pixels = (long)w * h;
                if (pixels > maxPixels)
                {
                    maxPixels = pixels;
                    nativeWidth = w;
                    nativeHeight = h;
                }
            }

            // Log all found resolutions for debugging
            foreach (var res in allResolutions.Distinct())
            {
                Logger.Debug($"Display supports: {res.w}x{res.h}");
            }

            // Calculate aspect ratio as a fraction (reduce to simplest form)
            int gcd = GCD(nativeWidth, nativeHeight);
            int aspectW = nativeWidth / gcd;
            int aspectH = nativeHeight / gcd;
            Logger.Info($"Native resolution: {nativeWidth}x{nativeHeight} ({maxPixels} pixels), aspect ratio: {aspectW}:{aspectH}");

            // Filter resolutions by aspect ratio
            foreach (var res in allResolutions)
            {
                int w = res.w;
                int h = res.h;

                // Check if this resolution matches the native aspect ratio
                int resGcd = GCD(w, h);
                int resAspectW = w / resGcd;
                int resAspectH = h / resGcd;

                if (resAspectW == aspectW && resAspectH == aspectH)
                {
                    resolutions.Add($"{w}x{h}");
                }
            }

            // Sort by width then height descending
            var sortedList = resolutions.ToList();
            sortedList.Sort((a, b) =>
            {
                var partsA = a.Split('x');
                var partsB = b.Split('x');
                int widthA = int.Parse(partsA[0]);
                int widthB = int.Parse(partsB[0]);
                int heightA = int.Parse(partsA[1]);
                int heightB = int.Parse(partsB[1]);

                if (widthA != widthB) return widthB.CompareTo(widthA);
                return heightB.CompareTo(heightA);
            });

            foreach (var resolution in sortedList)
            {
                Logger.Info($"Resolution option: {resolution} (matches {aspectW}:{aspectH})");
            }
            return sortedList;
        }

        /// <summary>
        /// Calculate Greatest Common Divisor using Euclidean algorithm
        /// </summary>
        private static int GCD(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        /// <summary>
        /// Set monitor resolution to a supported value.
        /// </summary>
        public static bool SetResolutionTo(string targetResolution)
        {
            var parts = targetResolution.Split('x');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out int targetWidth) ||
                !int.TryParse(parts[1], out int targetHeight))
            {
                Logger.Error($"Error: Invalid resolution format {targetResolution}");
                return false;
            }

            DEVMODE mode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };

            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode))
            {
                Logger.Error("Error: Could not retrieve current display settings.");
                return false;
            }

            mode.dmPelsWidth = targetWidth;
            mode.dmPelsHeight = targetHeight;
            mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;

            // Test before applying
            int testResult = ChangeDisplaySettings(ref mode, CDS_TEST);
            if (testResult != DISP_CHANGE_SUCCESSFUL)
            {
                Logger.Error($"Test failed: {targetResolution} not valid on this display.");
                return false;
            }

            // Apply permanently
            int result = ChangeDisplaySettings(ref mode, CDS_UPDATEREGISTRY);
            if (result == DISP_CHANGE_SUCCESSFUL)
            {
                Logger.Info($"Successfully switched to {targetResolution}.");
                return true;
            }
            else
            {
                Logger.Error($"Failed to apply {targetResolution} (error code {result}).");
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;

        public static void SendKeyCombo(ushort modifier1, ushort modifier2, ushort key)
        {
            INPUT[] inputs = new INPUT[6];

            // Press modifier1 (e.g., Ctrl)
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = modifier1;

            // Press modifier2 (e.g., Shift)
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = modifier2;

            // Press key (e.g., S)
            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].u.ki.wVk = key;

            //// Release key
            //inputs[3].type = INPUT_KEYBOARD;
            //inputs[3].u.ki.wVk = key;
            //inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

            //// Release modifier2
            //inputs[4].type = INPUT_KEYBOARD;
            //inputs[4].u.ki.wVk = modifier2;
            //inputs[4].u.ki.dwFlags = KEYEVENTF_KEYUP;

            //// Release modifier1
            //inputs[5].type = INPUT_KEYBOARD;
            //inputs[5].u.ki.wVk = modifier1;
            //inputs[5].u.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public const UInt32 WM_KEYDOWN = 0x0100;
        public const UInt32 WM_CLOSE = 0x0010;
        public const int VK_CONTROL = 0x11;
        public const int VK_SHIFT = 0x10;
        public const int VK_O = 0x4F; // Virtual key code for 'O'

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, UInt32 Msg, int wParam, int lParam);

        /// <summary>
        /// Close the foreground window by sending WM_CLOSE
        /// Skips Game Bar and other system windows
        /// </summary>
        public static bool CloseForegroundWindow()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    Logger.Warn("No foreground window found");
                    return false;
                }

                // Get window title and process info to avoid closing system windows
                GetWindowThreadProcessId(hwnd, out IntPtr processIdPtr);
                int processId = processIdPtr.ToInt32();

                string windowTitle = GetWindowTitle(hwnd);

                // Skip Game Bar, desktop, and other system windows
                if (string.IsNullOrEmpty(windowTitle) ||
                    windowTitle.Contains("Xbox Game Bar") ||
                    windowTitle.Contains("GameBar") ||
                    windowTitle == "Program Manager" ||
                    windowTitle == "Windows Shell Experience Host")
                {
                    Logger.Info($"Skipping system window: {windowTitle}");
                    return false;
                }

                Logger.Info($"Closing foreground window: '{windowTitle}' (PID: {processId})");
                PostMessage(hwnd, WM_CLOSE, 0, 0);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error closing foreground window: {ex.Message}");
                return false;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern void Sleep(uint dwMilliseconds);

        // Virtual key codes for modifiers
        private const int VK_ALT = 0x12;

        // Extended keys that need the EXTENDEDKEY flag
        private static readonly HashSet<int> ExtendedKeys = new HashSet<int>
        {
            0x21, 0x22, 0x23, 0x24, // PageUp, PageDown, End, Home
            0x25, 0x26, 0x27, 0x28, // Arrow keys
            0x2D, 0x2E,             // Insert, Delete
            0x5B, 0x5C,             // Win keys
            0x6F,                   // NumpadDivide
            0x90,                   // ScrollLock
            0x91,                   // NumLock
            0x2C,                   // PrintScreen
        };

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

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
            { "LeftBracket", 0xDB }, { "RightBracket", 0xDD },
            { "Backslash", 0xDC }, { "Semicolon", 0xBA }, { "Quote", 0xDE },
            { "Comma", 0xBC }, { "Period", 0xBE }, { "Slash", 0xBF },
            { "Backtick", 0xC0 }, { "Tilde", 0xC0 },
        };

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        /// <summary>
        /// Parse and send a keyboard shortcut string (e.g., "Ctrl+Shift+S", "Alt+F4", "Win+G")
        /// </summary>
        public static bool SendKeyboardShortcut(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                Logger.Warn("Empty shortcut string provided");
                return false;
            }

            try
            {
                var parts = shortcut.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
                var modifiers = new List<int>();
                int mainKey = 0;

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    var upper = trimmed.ToUpperInvariant();

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
                        modifiers.Add(0x5B); // VK_LWIN
                    }
                    else if (upper == "RWIN")
                    {
                        modifiers.Add(0x5C); // VK_RWIN
                    }
                    else
                    {
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

                // Press modifiers
                foreach (var mod in modifiers)
                {
                    SendSingleKey((ushort)mod, false);
                    Sleep(10);
                }

                // Press and release main key
                if (mainKey != 0)
                {
                    SendSingleKey((ushort)mainKey, false);
                    Sleep(10);
                    SendSingleKey((ushort)mainKey, true);
                    Sleep(10);
                }

                // Release modifiers in reverse order
                for (int i = modifiers.Count - 1; i >= 0; i--)
                {
                    SendSingleKey((ushort)modifiers[i], true);
                    Sleep(10);
                }

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

        private static bool SendSingleKey(ushort vk, bool keyUp)
        {
            uint flags = keyUp ? KEYEVENTF_KEYUP : 0;

            if (ExtendedKeys.Contains(vk))
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[0].u.ki.dwFlags = flags;

            var result = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            return result == 1;
        }

        private static int GetVirtualKeyCode(string keyName)
        {
            if (KeyNameToVK.TryGetValue(keyName, out int vk))
            {
                return vk;
            }

            if (keyName.Length == 1)
            {
                short result = VkKeyScan(keyName[0]);
                if (result != -1)
                {
                    return result & 0xFF;
                }
            }

            return 0;
        }

        #region HDR / Advanced Color APIs

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE setPacket);

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(
            uint flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
            ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const int ERROR_SUCCESS = 0;

        private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
        private const int DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_2DREGION
        {
            public uint cx;
            public uint cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
        {
            public ulong pixelRate;
            public DISPLAYCONFIG_RATIONAL hSyncFreq;
            public DISPLAYCONFIG_RATIONAL vSyncFreq;
            public DISPLAYCONFIG_2DREGION activeSize;
            public DISPLAYCONFIG_2DREGION totalSize;
            public uint videoStandard;
            public uint scanLineOrdering;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_TARGET_MODE
        {
            public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTL
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SOURCE_MODE
        {
            public uint width;
            public uint height;
            public uint pixelFormat;
            public POINTL position;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct DISPLAYCONFIG_MODE_INFO_UNION
        {
            [FieldOffset(0)]
            public DISPLAYCONFIG_TARGET_MODE targetMode;
            [FieldOffset(0)]
            public DISPLAYCONFIG_SOURCE_MODE sourceMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public int type;
            public int size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value;
            public uint colorEncoding;
            public uint bitsPerColorChannel;

            public bool AdvancedColorSupported => (value & 0x1) == 0x1;
            public bool AdvancedColorEnabled => (value & 0x2) == 0x2;
            public bool WideColorEnforced => (value & 0x4) == 0x4;
            public bool AdvancedColorForceDisabled => (value & 0x8) == 0x8;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint enableAdvancedColor;
        }

        /// <summary>
        /// Get HDR support and enabled status for the primary display.
        /// </summary>
        public static (bool Supported, bool Enabled) GetHDRStatus()
        {
            try
            {
                int result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
                if (result != ERROR_SUCCESS)
                {
                    Logger.Error($"GetDisplayConfigBufferSizes failed with error {result}");
                    return (false, false);
                }

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

                result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result != ERROR_SUCCESS)
                {
                    Logger.Error($"QueryDisplayConfig failed with error {result}");
                    return (false, false);
                }

                // Check the first/primary display
                if (paths.Length > 0)
                {
                    var colorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
                    colorInfo.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
                    colorInfo.header.size = Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>();
                    colorInfo.header.adapterId = paths[0].targetInfo.adapterId;
                    colorInfo.header.id = paths[0].targetInfo.id;

                    result = DisplayConfigGetDeviceInfo(ref colorInfo);
                    if (result == ERROR_SUCCESS)
                    {
                        Logger.Info($"HDR Supported: {colorInfo.AdvancedColorSupported}, Enabled: {colorInfo.AdvancedColorEnabled}");
                        return (colorInfo.AdvancedColorSupported, colorInfo.AdvancedColorEnabled);
                    }
                    else
                    {
                        Logger.Error($"DisplayConfigGetDeviceInfo failed with error {result}");
                    }
                }

                return (false, false);
            }
            catch (Exception ex)
            {
                Logger.Error($"GetHDRStatus exception: {ex}");
                return (false, false);
            }
        }

        /// <summary>
        /// Enable or disable HDR on the primary display.
        /// </summary>
        public static bool SetHDREnabled(bool enable)
        {
            try
            {
                int result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
                if (result != ERROR_SUCCESS)
                {
                    Logger.Error($"GetDisplayConfigBufferSizes failed with error {result}");
                    return false;
                }

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

                result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result != ERROR_SUCCESS)
                {
                    Logger.Error($"QueryDisplayConfig failed with error {result}");
                    return false;
                }

                if (paths.Length > 0)
                {
                    var setState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
                    setState.header.type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
                    setState.header.size = Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>();
                    setState.header.adapterId = paths[0].targetInfo.adapterId;
                    setState.header.id = paths[0].targetInfo.id;
                    setState.enableAdvancedColor = enable ? 1u : 0u;

                    result = DisplayConfigSetDeviceInfo(ref setState);
                    if (result == ERROR_SUCCESS)
                    {
                        Logger.Info($"HDR set to {enable}");
                        return true;
                    }
                    else
                    {
                        Logger.Error($"DisplayConfigSetDeviceInfo failed with error {result}");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"SetHDREnabled exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Get actual current refresh rate using QueryDisplayConfig (more accurate than EnumDisplaySettings).
        /// This properly reports the actual refresh rate on VRR displays and after display changes.
        /// </summary>
        public static int GetCurrentRefreshRateFromDisplayConfig()
        {
            try
            {
                int result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
                if (result != ERROR_SUCCESS)
                {
                    Logger.Warn($"GetDisplayConfigBufferSizes failed with error {result}, falling back to EnumDisplaySettings");
                    return GetCurrentRefreshRate();
                }

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

                result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result != ERROR_SUCCESS)
                {
                    Logger.Warn($"QueryDisplayConfig failed with error {result}, falling back to EnumDisplaySettings");
                    return GetCurrentRefreshRate();
                }

                // Get refresh rate from the first/primary active display path
                if (paths.Length > 0)
                {
                    var refreshRate = paths[0].targetInfo.refreshRate;
                    if (refreshRate.Denominator > 0)
                    {
                        // Calculate actual refresh rate (e.g., 144000/1000 = 144Hz, 143998/1000 ≈ 144Hz)
                        int hz = (int)Math.Round((double)refreshRate.Numerator / refreshRate.Denominator);
                        Logger.Info($"QueryDisplayConfig refresh rate: {refreshRate.Numerator}/{refreshRate.Denominator} = {hz}Hz");
                        return hz;
                    }
                }

                Logger.Warn("No active display paths found, falling back to EnumDisplaySettings");
                return GetCurrentRefreshRate();
            }
            catch (Exception ex)
            {
                Logger.Error($"GetCurrentRefreshRateFromDisplayConfig exception: {ex}, falling back to EnumDisplaySettings");
                return GetCurrentRefreshRate();
            }
        }

        #endregion
    }
}
