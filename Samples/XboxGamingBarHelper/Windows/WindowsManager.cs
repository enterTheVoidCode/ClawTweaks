using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.Windows
{
    internal static class WindowsManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static Process GetForegroundProcess()
        {
            IntPtr foregroundWindowHandle = Win32.GetForegroundWindow();
            if (foregroundWindowHandle == IntPtr.Zero)
            {
                Logger.Error("Can't get foreground window.");
                return null;
            }
            IntPtr activeAppProcessId;
            Win32.GetWindowThreadProcessId(foregroundWindowHandle, out activeAppProcessId);
            if (activeAppProcessId == IntPtr.Zero)
            {
                Logger.Error("Can't get active process id.");
                return null;
            }

            Process currentAppProcess = Process.GetProcessById((int)activeAppProcessId);
            if (currentAppProcess == null)
            {
                Logger.Error($"Can't get process id {(int)activeAppProcessId}");
                return null;
            }

            return currentAppProcess;
        }
    }
}
