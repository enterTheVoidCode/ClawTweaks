using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                Logger.Error("Can't get process id from invalid window handle.");
                return -1;
            }

            GetWindowThreadProcessId(windowHandle, out IntPtr windowProcessId);
            if (windowProcessId == IntPtr.Zero)
            {
                Logger.Error("Can't get window process id.");
                return -1;
            }

            return (int)windowProcessId;
        }

        public static string GetWindowTitle(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                Logger.Error("Can't get window title from invalid window handle.");
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
                    Logger.Warn($"Window doesn't have process id???");
                    return true; // Continue enumeration
                }

                var process = Process.GetProcessById(processId);
                windows[processId] = new ProcessWindow(processId, hWnd, windowTitle, process.ProcessName, process.MainModule.FileName, processId == foregroundWindowProcessId);
                return true; // Continue enumeration
            }, 0);
        }

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int CDS_TEST = 0x02;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DM_DISPLAYFREQUENCY = 0x400000;

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
    }
}
