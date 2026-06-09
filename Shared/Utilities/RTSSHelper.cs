using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace Shared.Utilities
{
    public static partial class RTSSHelper
    {
        public static Process GetProcess()
        {
            var rtssProcessses = Process.GetProcessesByName("RTSS");
            if (rtssProcessses.Length == 0)
            {
                return null;
            }

            // Dispose all processes except the first one we return
            for (int i = 1; i < rtssProcessses.Length; i++)
            {
                rtssProcessses[i].Dispose();
            }

            return rtssProcessses[0];
        }

        public static bool IsRunning()
        {
            var rtssProcessses = Process.GetProcessesByName("RTSS");
            if (rtssProcessses.Length == 0)
            {
                return false;
            }

            try
            {
                // Check if the first process has been running for at least 2 seconds
                return (DateTime.Now - rtssProcessses[0].StartTime).TotalSeconds >= 2.0f;
            }
            finally
            {
                // Dispose all processes
                foreach (var proc in rtssProcessses)
                {
                    proc.Dispose();
                }
            }
        }

        public static bool IsInstalled()
        {
            // The Unwinder\RTSS registry key can linger after an NSIS uninstall, so verify the
            // actual RTSS.exe is present (matches the installer's prerequisite detection). This
            // avoids reporting RTSS as installed when only an orphaned registry key remains.
            string exe = ExecutablePath();
            if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
            {
                return true;
            }

            string[] defaultPaths =
            {
                @"C:\Program Files (x86)\RivaTuner Statistics Server\RTSS.exe",
                @"C:\Program Files\RivaTuner Statistics Server\RTSS.exe",
            };
            foreach (string path in defaultPaths)
            {
                if (System.IO.File.Exists(path))
                {
                    return true;
                }
            }

            return false;
        }

        public static string InstalledLocation()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Unwinder\RTSS"))
            {
                if (key == null)
                {
                    return string.Empty;
                }

                return (string)key.GetValue("InstallDir");
            }
        }

        public static string ExecutablePath()
        {
            var installLocation = InstalledLocation();
            if (string.IsNullOrEmpty(installLocation))
            {
                return string.Empty;
            }
            return System.IO.Path.Combine(installLocation, "RTSS.exe");
        }
    }
}
