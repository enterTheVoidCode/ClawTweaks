using System;
using System.Diagnostics;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper
{
    internal partial class Program
    {
        /// <summary>
        /// Executes a forced / immediate system power action requested by the Power Quick-Settings
        /// tile (pipe key "PowerAction"). Reboot / power-off / reboot-to-firmware go through
        /// shutdown.exe with <c>/f /t 0</c> (force apps closed, no timeout — no graceful "please wait"
        /// delay). Sleep / hibernate use <see cref="PowrProf.SetSuspendState"/> with force=false (the
        /// same primitive + flags the Hibernate pipe handler already uses; force=true escalates sleep
        /// to hibernate on some systems). The helper runs elevated, so it holds the shutdown privilege.
        /// </summary>
        internal static void ExecutePowerAction(string action)
        {
            switch ((action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "sleep":
                    // bForce MUST be false: bForce=true (critical suspend) escalates to hibernate on
                    // some systems (incl. the MSI Claw), which is exactly the "sleep hibernated" bug.
                    // Sleep is instant anyway, so there is no downside. Mirrors the working Hibernate
                    // pipe handler's bForce=false.
                    Logger.Info("PowerAction: sleep");
                    PowrProf.SetSuspendState(false, false, false);
                    break;
                case "hibernate":
                    Logger.Info("PowerAction: hibernate");
                    PowrProf.SetSuspendState(true, false, false);
                    break;
                case "reboot":
                    RunShutdown("/r /f /t 0");
                    break;
                case "poweroff":
                    RunShutdown("/s /f /t 0");
                    break;
                case "bios":
                    // /fw boots straight into the firmware (UEFI/BIOS) setup; requires /r.
                    RunShutdown("/r /fw /f /t 0");
                    break;
                default:
                    Logger.Warn($"PowerAction: unknown action '{action}'");
                    break;
            }
        }

        private static void RunShutdown(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Logger.Info($"PowerAction: shutdown.exe {args}");
        }
    }
}
