using Microsoft.Win32;
using Shared.Enums;
using System;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Detects whether usbip-win2 is installed on the system.
    /// Required prerequisite for the VIIPER emulation backend.
    /// </summary>
    internal class UsbipInstalledProperty : HelperProperty<bool, SettingsManager>
    {
        // Real service names installed by usbip-win2 (verified on a live install).
        private static readonly string[] ServiceKeyNames = new[]
        {
            "usbip2_ude",      // primary — USB Device Emulation (driver)
            "usbip2_filter",   // upper filter driver
            "mausbip",         // MsUsbIp service
            // Historical / alternate names, kept for safety:
            "usbip2vhci",
            "VHCI",
        };

        private static readonly string[] BinaryPaths = new[]
        {
            @"C:\Program Files\USBip\usbip.exe",
            @"C:\Program Files (x86)\USBip\usbip.exe",
        };

        public UsbipInstalledProperty(SettingsManager inManager)
            : base(Detect(), null, Function.Viiper_UsbipInstalled, inManager)
        {
            Logger.Info($"usbip-win2 installed: {Value}");
        }

        /// <summary>
        /// Re-runs detection and pushes the new state. Call after a user-initiated install.
        /// </summary>
        public void Refresh()
        {
            var detected = Detect();
            if (detected != Value)
            {
                SetValue(detected);
                Logger.Info($"usbip-win2 detection refreshed: {detected}");
            }
        }

        // Driver binaries the usbip-win2 MSI drops into System32\drivers. Unlike the service
        // *registry keys* (which can linger after an uninstall, even across a reboot — the cause of
        // the "uninstalled but still detected" false positive), these files are removed by a clean
        // uninstall, so they are a far more reliable install/uninstall signal.
        private static readonly string[] DriverFileNames = new[]
        {
            "usbip2_ude.sys",
            "usbip2_filter.sys",
        };

        private static bool Detect()
        {
            // Strictness rationale: a registry key under Services\<name> proves the service is
            // *registered*, NOT installed-and-present — such keys survive an uninstall (sometimes
            // even past a reboot) and made detection report "installed" for a removed driver. So we
            // DO NOT treat a bare registry key as a positive. We trust only signals that a clean
            // uninstall actually clears:
            //   1. the CLI binary (Program Files\USBip\usbip.exe),
            //   2. the driver .sys files in System32\drivers,
            //   3. a service the SCM reports as actually *Running* (driver loaded right now).
            // Every probed signal is logged at Info so a future false positive/negative is
            // diagnosable straight from the production log.
            bool found = false;
            try
            {
                foreach (var path in BinaryPaths)
                {
                    bool exists = File.Exists(path);
                    Logger.Info($"usbip-win2 probe: binary '{path}' exists={exists}");
                    if (exists) found = true;
                }

                string driversDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
                foreach (var file in DriverFileNames)
                {
                    string full = Path.Combine(driversDir, file);
                    bool exists = File.Exists(full);
                    Logger.Info($"usbip-win2 probe: driver '{full}' exists={exists}");
                    if (exists) found = true;
                }

                foreach (var name in ServiceKeyNames)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                    {
                        if (key == null) continue;
                        string state = QueryServiceState(name);
                        // Registered key alone is NOT trusted (lingers post-uninstall). Count only
                        // when SCM reports the service is genuinely Running (driver loaded).
                        bool running = state != null && state.StartsWith("Running", StringComparison.OrdinalIgnoreCase);
                        Logger.Info($"usbip-win2 probe: service '{name}' state={state} counted={running}");
                        if (running) found = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"UsbipInstalled detection failed: {ex.Message}");
            }
            Logger.Info($"usbip-win2 detection result: installed={found}");
            return found;
        }

        /// <summary>
        /// Returns "Status/StartType" (e.g., "Running/Manual", "Stopped/Disabled") for the
        /// given driver service. "Stopped/Disabled" is the case we most want to surface: a
        /// service registered in the SCM but configured to never start, which silently
        /// breaks USBIP without any error from libviiper.
        ///
        /// SCM queries can block if the service control manager is unresponsive (rare,
        /// but observed during heavy boot contention). Run on a worker task with a
        /// short timeout so a hung SCM never delays helper startup — diagnostic logs
        /// are valuable, but never at the cost of pushing back the helper's main work.
        /// </summary>
        private static string QueryServiceState(string serviceName)
        {
            const int TimeoutMs = 1500;
            try
            {
                var task = Task.Run(() =>
                {
                    using (var sc = new ServiceController(serviceName))
                    {
                        return $"{sc.Status}/{sc.StartType}";
                    }
                });
                if (!task.Wait(TimeoutMs))
                {
                    return "scm-timeout";
                }
                return task.Result;
            }
            catch (AggregateException ae) when (ae.InnerException is InvalidOperationException)
            {
                // SCM doesn't know the service even though the registry key exists —
                // partial install or rename in flight.
                return "not-in-scm";
            }
            catch (Exception ex)
            {
                return $"query-error:{ex.GetType().Name}";
            }
        }
    }
}
