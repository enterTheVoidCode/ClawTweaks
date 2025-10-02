using System;
using System.Runtime.InteropServices;

namespace XboxGamingBarHelper.Windows
{
    internal class Win32
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out IntPtr lpdwProcessId);
    }
}
