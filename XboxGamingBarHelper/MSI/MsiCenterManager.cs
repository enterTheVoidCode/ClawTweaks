using NLog;
using Shared.Enums;
using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.IPC;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Manages the MSI Center M OEM software.
    /// Polls every 4 seconds and exposes a toggle property that stops/starts
    /// the MSI Center M processes, scheduled tasks, and the Foundation Service.
    ///
    /// Ported from HandheldCompanion's ClawCenterWatcher + ISpaceWatcher.
    /// </summary>
    internal class MsiCenterManager : Manager
    {
        private static new readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // MSI Center M scheduled task names (Task Scheduler root)
        private static readonly string[] TaskNames = { "MSI_Center_M_Server", "MSI_Center_M_Updater" };

        // MSI Center M process executable names (without .exe)
        private static readonly string[] ProcessNames =
        {
            "MSI_Center_M_Server",
            "MSI Center M",
            "MCMOSDInfo",
            "MSI Center OSD Info",
        };

        // MSI Foundation Service (Windows service name)
        private const string ServiceName = "MSI Foundation Service";

        // Poll interval matches HC (4 seconds)
        private const int PollIntervalMs = 4000;

        private readonly Timer _pollTimer;
        private bool _lastKnownActive = false;

        public readonly MsiCenterActiveProperty MsiCenterActive;

        public MsiCenterManager(NamedPipeServer pipeServer)
        {
            MsiCenterActive = new MsiCenterActiveProperty(this);

            // Initial detection (synchronous, fast)
            _lastKnownActive = DetectActive();
            MsiCenterActive.SetValueSilent(_lastKnownActive);

            // Poll for changes
            _pollTimer = new Timer(PollCallback, null, PollIntervalMs, PollIntervalMs);

            Logger.Info($"[MsiCenterManager] Init. MSI Center M active={_lastKnownActive}");
        }

        private void PollCallback(object state)
        {
            try
            {
                bool active = DetectActive();
                if (active != _lastKnownActive)
                {
                    _lastKnownActive = active;
                    Logger.Info($"[MsiCenterManager] State changed: active={active}");
                    MsiCenterActive.SetValue(active);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiCenterManager] Poll error: {ex.Message}");
            }
        }

        // ── Detection ────────────────────────────────────────────────────

        public bool DetectActive()
        {
            return HasProcesses() || HasRunningService() || HasEnabledTasks();
        }

        private static bool HasProcesses()
        {
            foreach (string name in ProcessNames)
            {
                Process[] procs = Process.GetProcessesByName(name);
                bool found = procs.Length > 0;
                foreach (Process p in procs) p.Dispose();
                if (found) return true;
            }
            return false;
        }

        private static bool HasRunningService()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    return sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch (InvalidOperationException)
            {
                return false; // Service does not exist
            }
        }

        private static bool HasEnabledTasks()
        {
            foreach (string taskName in TaskNames)
            {
                // Use schtasks /Query to check if task exists and is enabled
                try
                {
                    var psi = new ProcessStartInfo("schtasks.exe",
                        $"/Query /TN \"{taskName}\" /FO CSV /NH")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };
                    using (var proc = Process.Start(psi))
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(3000);
                        // If the task exists and is not "Disabled", consider it enabled
                        if (output.Length > 0 && !output.Contains("Disabled") && !output.Contains("Deaktiviert"))
                            return true;
                    }
                }
                catch
                {
                    // schtasks not available or access denied — ignore
                }
            }
            return false;
        }

        // ── Toggle ───────────────────────────────────────────────────────

        /// <summary>
        /// Called by MsiCenterActiveProperty when the user toggles the tile.
        /// active=false → stop MSI Center M; active=true → start MSI Center M.
        /// </summary>
        public void ApplyActive(bool active)
        {
            if (active)
                EnableMsiCenter();
            else
                DisableMsiCenter();
        }

        private static void DisableMsiCenter()
        {
            Logger.Info("[MsiCenterManager] Disabling MSI Center M...");

            // 1. Kill running processes
            foreach (string name in ProcessNames)
            {
                try
                {
                    foreach (Process proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            if (!proc.HasExited) proc.Kill();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"[MsiCenterManager] Kill {name} failed: {ex.Message}");
                        }
                        finally { proc.Dispose(); }
                    }
                }
                catch { }
            }

            // 2. Stop + disable scheduled tasks
            foreach (string taskName in TaskNames)
            {
                RunSchtasks($"/End /TN \"{taskName}\"");
                RunSchtasks($"/Change /TN \"{taskName}\" /DISABLE");
            }

            // 3. Stop + disable the Foundation Service
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped &&
                        sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
                // Change start type to Disabled via sc.exe (ServiceController has no API for this)
                RunScExe($"config \"{ServiceName}\" start= disabled");
            }
            catch (InvalidOperationException)
            {
                // Service does not exist — nothing to do
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MsiCenterManager] Stop service failed: {ex.Message}");
            }

            Logger.Info("[MsiCenterManager] MSI Center M disabled.");
        }

        private static void EnableMsiCenter()
        {
            Logger.Info("[MsiCenterManager] Re-enabling MSI Center M...");

            // 1. Re-enable scheduled tasks (they will start their processes)
            foreach (string taskName in TaskNames)
            {
                RunSchtasks($"/Change /TN \"{taskName}\" /ENABLE");
                RunSchtasks($"/Run /TN \"{taskName}\"");
            }

            // 2. Re-enable + start the Foundation Service
            try
            {
                RunScExe($"config \"{ServiceName}\" start= auto");
                using (var sc = new ServiceController(ServiceName))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped)
                        sc.Start();
                }
            }
            catch (InvalidOperationException)
            {
                // Service does not exist
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MsiCenterManager] Start service failed: {ex.Message}");
            }

            Logger.Info("[MsiCenterManager] MSI Center M re-enabled.");
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static void RunSchtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute   = false,
                    CreateNoWindow    = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                    int code = proc.ExitCode;
                    if (code != 0)
                        Logger.Debug($"[MsiCenterManager] schtasks {args} → exit {code}");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiCenterManager] schtasks {args} failed: {ex.Message}");
            }
        }

        private static void RunScExe(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", args)
                {
                    UseShellExecute   = false,
                    CreateNoWindow    = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MsiCenterManager] sc.exe {args} failed: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pollTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
