using Microsoft.Win32;
using NLog;
using System;
using System.Diagnostics;
using System.IO;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Generic + tool-specific uninstall helpers for the Setup tab.
    /// The helper process already runs elevated (scheduled task), so these run silently
    /// without an additional UAC prompt.
    /// </summary>
    internal static class ToolUninstaller
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Finds an Add/Remove-Programs entry whose DisplayName contains <paramref name="displayNamePattern"/>
        /// and runs its (Quiet)UninstallString, waiting for completion.
        /// Returns true if an uninstaller was launched.
        /// </summary>
        public static bool UninstallViaArp(string displayNamePattern, string extraSilentArgs = null)
        {
            (string display, string cmd) = FindArpUninstall(displayNamePattern);
            if (string.IsNullOrEmpty(cmd))
            {
                Logger.Warn($"Uninstall: no ARP entry found matching '{displayNamePattern}'");
                return false;
            }

            if (!string.IsNullOrEmpty(extraSilentArgs))
            {
                cmd = cmd + " " + extraSilentArgs;
            }

            Logger.Info($"Uninstall '{display}': {cmd}");
            return RunCommand(cmd);
        }

        private static (string display, string command) FindArpUninstall(string pattern)
        {
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };

            foreach (string root in roots)
            {
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(root);
                if (key == null) continue;

                foreach (string sub in key.GetSubKeyNames())
                {
                    using RegistryKey k = key.OpenSubKey(sub);
                    string name = k?.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(name) ||
                        name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string quiet = k.GetValue("QuietUninstallString") as string;
                    string normal = k.GetValue("UninstallString") as string;
                    return (name, !string.IsNullOrEmpty(quiet) ? quiet : normal);
                }
            }

            return (null, null);
        }

        private static bool RunCommand(string commandLine)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + commandLine,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using Process p = Process.Start(psi);
                if (p == null)
                {
                    Logger.Warn($"Uninstall command failed to start: {commandLine}");
                    return false;
                }

                if (!p.WaitForExit(300000))
                {
                    try { p.Kill(); } catch { }
                    Logger.Warn($"Uninstall command timed out: {commandLine}");
                    return false;
                }

                Logger.Info($"Uninstall command exited with {p.ExitCode}: {commandLine}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Uninstall command exception ({commandLine}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// HidHide may register no ARP entry (kernel-driver service only). Try ARP first,
        /// then fall back to removing the service + driver file directly.
        /// A reboot may be required to fully unload the driver.
        /// </summary>
        public static bool UninstallHidHide()
        {
            if (UninstallViaArp("HidHide"))
            {
                return true;
            }

            Logger.Info("HidHide: no ARP uninstaller — removing kernel service + driver directly");
            bool any = false;
            any |= RunCommand("sc stop HidHide");
            any |= RunCommand("sc config HidHide start= disabled");
            any |= RunCommand("sc delete HidHide");

            try
            {
                string sys = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "HidHide.sys");
                if (File.Exists(sys))
                {
                    File.Delete(sys);
                    Logger.Info("Deleted HidHide.sys");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"HidHide.sys delete skipped: {ex.Message}");
            }

            return any;
        }

        /// <summary>
        /// RTSS uninstall: RTSS keeps running (our own RTSSManager relaunches it via AutoStartRTSS)
        /// and its NSIS uninstaller aborts while the app is running. Suppress the relaunch, kill
        /// RTSS + its EncoderServer, then run the uninstaller silently (NSIS /S).
        /// </summary>
        public static bool UninstallRtss()
        {
            XboxGamingBarHelper.RTSS.RTSSManager.SuppressAutoStart = true;
            try
            {
                KillByName("RTSS");
                KillByName("EncoderServer");
                System.Threading.Thread.Sleep(400);

                (string display, string cmd) = FindArpUninstall("RivaTuner Statistics Server");
                if (string.IsNullOrEmpty(cmd))
                {
                    Logger.Warn("RTSS uninstall: no ARP entry found");
                    return false;
                }

                // RTSS uninstall.exe is an NSIS installer: /S = silent.
                string silent = cmd.Trim() + " /S";
                Logger.Info($"RTSS uninstall (silent): {silent}");
                bool ran = RunCommand(silent);

                System.Threading.Thread.Sleep(600);
                KillByName("RTSS");
                KillByName("EncoderServer");
                return ran;
            }
            catch (Exception ex)
            {
                Logger.Warn($"RTSS uninstall failed: {ex.Message}");
                return false;
            }
            finally
            {
                // After a successful uninstall RTSSHelper.IsInstalled() is false, so the loop
                // won't relaunch anyway; if it failed, restore normal auto-start behaviour.
                XboxGamingBarHelper.RTSS.RTSSManager.SuppressAutoStart = false;
            }
        }

        private static void KillByName(string processName)
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName(processName))
                {
                    try { p.Kill(); p.WaitForExit(3000); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Kill {processName} failed: {ex.Message}");
            }
        }
    }
}
