using Microsoft.Win32.TaskScheduler;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.IPC;
using TaskScheduled = Microsoft.Win32.TaskScheduler.Task;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Manages the MSI Center M OEM software.
    /// Polls every 4 seconds and exposes a toggle property that stops/starts
    /// the MSI Center M processes, scheduled tasks, and the Foundation Service.
    ///
    /// Ported 1:1 from HandheldCompanion: ClawCenterWatcher + ISpaceWatcher.
    /// - Task management via Microsoft.Win32.TaskScheduler (searches all Task Scheduler folders)
    /// - Service startup mode via ServiceUtils.ChangeStartMode() (advapi32 P/Invoke)
    /// </summary>
    internal class MsiCenterManager : Manager
    {
        private static new readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // 1:1 from HC ClawCenterWatcher
        private static readonly List<string> taskNames      = new List<string> { "MSI_Center_M_Server", "MSI_Center_M_Updater" };
        private static readonly List<string> executableNames = new List<string> { "MSI_Center_M_Server", "MSI Center M", "MCMOSDInfo", "MSI Center OSD Info" };
        private static readonly List<string> serviceNames   = new List<string> { "MSI Foundation Service" };

        // MSI Quick Settings: Game Bar extension that restarts MSI services whenever Win+G opens Game Bar.
        //
        // Why Remove-AppxPackage (per-user) is insufficient:
        //   Game Bar reads extension registrations from HKLM (installed for all users by MSI Center M installer).
        //   Removing the per-user AppX entry leaves the HKLM extension entry intact → Game Bar still loads it.
        //
        // Correct approach: Disable-AppxPackage (elevated, affects all users).
        //   - Marks the package as disabled system-wide → Game Bar can no longer activate it.
        //   - Reversible with Enable-AppxPackage — no reinstall required.
        //   - Requires a one-time UAC prompt (same pattern as HC manage-gamebar.ps1).
        //
        // PackageName: resolved dynamically (version-independent)
        // PackageFamilyName: fixed suffix, does not change with version updates
        //
        // Processes started by the Game Bar extension that must also be killed immediately:
        //   Gamebar_Widget — MSI Quick Settings widget host (Game Bar side)
        //   mongMode       — MSI gaming-mode / performance daemon (started by the widget)
        private static readonly List<string> gameBarExtensionPackageNames = new List<string> { "9426MICRO-STARINTERNATION.MSIQuickSettings" };
        private static readonly List<string> gameBarExtensionProcessNames = new List<string> { "Gamebar_Widget", "mongMode" };

        public readonly MsiCenterActiveProperty MsiCenterActive;

        public MsiCenterManager(NamedPipeServer pipeServer)
        {
            MsiCenterActive = new MsiCenterActiveProperty(this);

            // Detect initial state once at startup — no polling needed.
            // MSI Center M tasks/services do not self-restart after being stopped,
            // so continuous polling wastes CPU without benefit.
            bool initialActive = DetectActive();
            MsiCenterActive.SetValueSilent(initialActive);

            Logger.Info($"[MsiCenterManager] Init. active={initialActive}");
        }

        // ── Detection — 1:1 from HC ISpaceWatcher ────────────────────────

        public bool DetectActive()
        {
            return HasProcesses() || HasRunningServices() || HasEnabledTasks();
        }

        // 1:1 from HC ISpaceWatcher.HasProcesses()
        public bool HasProcesses()
        {
            foreach (string name in executableNames)
            {
                Process[] matches = Process.GetProcessesByName(name);
                bool found = matches.Length > 0;
                foreach (Process p in matches)
                    p.Dispose();
                if (found) return true;
            }
            return false;
        }

        // 1:1 from HC ISpaceWatcher.HasRunningServices()
        public bool HasRunningServices()
        {
            foreach (string serviceName in serviceNames)
            {
                ServiceController sc = new ServiceController(serviceName);
                try
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                        return true;
                }
                catch (InvalidOperationException)
                {
                    // service does not exist
                }
                finally
                {
                    sc.Dispose();
                }
            }
            return false;
        }

        // 1:1 from HC ISpaceWatcher.HasEnabledTasks()
        public bool HasEnabledTasks()
        {
            return GetTasks().Any(task => task.Enabled);
        }

        // ── Game Bar Extension Packages ──────────────────────────────────────

        /// <summary>
        /// Disables the MSI Quick Settings Game Bar extension system-wide.
        ///
        /// Two-stage:
        ///   1. Kill running Game Bar extension processes immediately (Gamebar_Widget, mongMode).
        ///   2. Disable-AppxPackage (elevated) → marks the AppX package as disabled for all users.
        ///      Game Bar can no longer load/activate the extension on subsequent Win+G presses.
        ///
        /// Requires a one-time UAC prompt (same pattern as HC manage-gamebar.ps1).
        /// Reversible via EnableGameBarExtensionPackages() — no reinstall required.
        /// </summary>
        private static void DisableGameBarExtensionPackages()
        {
            // Stage 1 — kill running extension processes immediately
            foreach (string procName in gameBarExtensionProcessNames)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(procName))
                    {
                        try { if (!p.HasExited) p.Kill(); }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            }

            // Stage 2 — Remove-AppxPackage -AllUsers (elevated)
            //
            // Why Disable-AppxPackage is insufficient:
            //   Disable-AppxPackage only prevents automatic activation on Game Bar open.
            //   Game Bar still LISTS the widget in its navigation panel and re-activates it
            //   (starting MSI services) when the user manually navigates to it.
            //
            // Remove-AppxPackage -AllUsers removes the package from ALL user accounts and
            // cleans up the HKLM Game Bar extension registry entries entirely.
            // The widget disappears from Game Bar's navigation panel.
            //
            // The staged package in C:\Program Files\WindowsApps\ is preserved, so
            // EnableGameBarExtensionPackages() can restore via Add-AppxPackage -RegisterByFamilyName
            // without requiring a full MSI Center M reinstall.
            foreach (string name in gameBarExtensionPackageNames)
            {
                try
                {
                    string ps = $"$p = Get-AppxPackage -AllUsers -Name '{name}'; " +
                                $"if ($p) {{ Remove-AppxPackage -AllUsers -Package $p.PackageFullName }}";
                    RunElevatedPowerShell(ps);
                    Logger.Info($"[MsiCenterManager] DisableGameBarExtension '{name}' removed (Remove-AppxPackage -AllUsers).");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[MsiCenterManager] DisableGameBarExtension failed for '{name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Re-registers the MSI Quick Settings Game Bar extension for the current user.
        ///
        /// Uses Add-AppxPackage -RegisterByFamilyName which re-registers the package from
        /// the staged copy in C:\Program Files\WindowsApps\ (set by the MSI Center M installer).
        /// Does not require the original installer or Store access.
        ///
        /// Note: Re-registers for the CURRENT USER only (not all users).
        /// Other user accounts can re-register themselves by running MSI Center M, or by
        /// running: Add-AppxPackage -RegisterByFamilyName -MainPackage <familyName>
        ///
        /// If the staged package no longer exists (e.g. MSI Center M was uninstalled), this
        /// will fail silently. The user needs to reinstall MSI Center M in that case.
        /// </summary>
        private static void EnableGameBarExtensionPackages()
        {
            foreach (string name in gameBarExtensionPackageNames)
            {
                // Derive the PackageFamilyName from the known PackageName + family suffix.
                // This must match gameBarExtensionPackageFamilyNames — kept in sync manually.
                string familyName = name + "_kzh8wxbdkxb8p";
                try
                {
                    // Add-AppxPackage -RegisterByFamilyName re-registers from the staged
                    // WindowsApps copy — no admin required (per-user registration).
                    string ps = $"Add-AppxPackage -RegisterByFamilyName " +
                                $"-MainPackage '{familyName}' " +
                                $"-ErrorAction SilentlyContinue";
                    RunSilentPowerShell(ps);
                    Logger.Info($"[MsiCenterManager] EnableGameBarExtension '{name}' re-registered (RegisterByFamilyName).");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[MsiCenterManager] EnableGameBarExtension failed for '{name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Runs a PowerShell command in a hidden, non-elevated window.
        /// Uses -EncodedCommand (Base64 UTF-16LE) to avoid command-line escaping issues.
        /// Used for operations that do not require admin (e.g. per-user AppX re-registration).
        /// </summary>
        private static void RunSilentPowerShell(string command)
        {
            byte[] encoded = Encoding.Unicode.GetBytes(command);
            string b64 = Convert.ToBase64String(encoded);

            var psi = new ProcessStartInfo("powershell.exe",
                $"-NonInteractive -WindowStyle Hidden -EncodedCommand {b64}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                proc?.WaitForExit(15000);
            }
        }

        /// <summary>
        /// Runs a PowerShell command elevated (UAC) in a hidden window.
        /// Uses -EncodedCommand (Base64 UTF-16LE) to avoid command-line escaping issues.
        /// Waits up to 30 seconds (covers UAC interaction + command execution).
        ///
        /// Note: UseShellExecute = true is required for -Verb runas.
        ///       stdout/stderr cannot be redirected in elevated mode.
        /// </summary>
        private static void RunElevatedPowerShell(string command)
        {
            byte[] encoded = Encoding.Unicode.GetBytes(command);
            string b64 = Convert.ToBase64String(encoded);

            var psi = new ProcessStartInfo("powershell.exe",
                $"-NonInteractive -WindowStyle Hidden -EncodedCommand {b64}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var proc = Process.Start(psi))
            {
                proc?.WaitForExit(30000);
            }
        }

        // 1:1 from HC ISpaceWatcher.GetTasks() — searches ALL Task Scheduler folders
        private static IEnumerable<TaskScheduled> GetTasks()
        {
            using (TaskService taskService = new TaskService())
            {
                foreach (string taskName in taskNames)
                {
                    TaskScheduled taskScheduled = taskService.GetTask(taskName);
                    if (taskScheduled != null)
                        yield return taskScheduled;
                }
            }
        }

        // ── Tiny Center M coexistence sync ───────────────────────────────
        // Processes to bounce so MSI Center M re-reads profile.rec: the ControlMode server (holds the
        // controller model in RAM and reloads profile.rec on start — it respawns immediately) and the
        // UI window (the user reopens it via the left OEM front button). This is a LIGHT bounce, NOT the
        // full Disable() (which also kills tasks/services + removes the Game Bar extension).
        private static readonly string[] SyncBounceProcesses = { "MSI Center M", "MSI_Center_M_Server_ControlMode" };

        /// <summary>
        /// Kills the MSI Center M UI + ControlMode server so it reloads profile.rec (which we just
        /// wrote), making Center M's own state match what Tiny Center M set. ControlMode respawns on
        /// its own; the UI is reopened by the user. Used after a Tiny Center M "Apply".
        /// </summary>
        public static void RestartControlModeForSync()
        {
            foreach (string name in SyncBounceProcesses)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(name))
                    {
                        try { if (!p.HasExited) p.Kill(); }
                        catch (Exception ex) { Logger.Debug($"[MsiCenterManager] kill '{name}' failed: {ex.Message}"); }
                        finally { p.Dispose(); }
                    }
                }
                catch (Exception ex) { Logger.Debug($"[MsiCenterManager] enum '{name}' failed: {ex.Message}"); }
            }
            Logger.Info("[MsiCenterManager] ControlMode/UI bounced for Tiny Center M sync (ControlMode will respawn + reload profile.rec)");
        }

        /// <summary>True if the MSI Center M UI or ControlMode server is currently running.</summary>
        public static bool IsControlModeRunning()
        {
            foreach (string name in SyncBounceProcesses)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(name);
                    bool any = ps.Length > 0;
                    foreach (Process p in ps) p.Dispose();
                    if (any) return true;
                }
                catch { }
            }
            return false;
        }

        // ── Toggle ───────────────────────────────────────────────────────

        /// <summary>
        /// Called by MsiCenterActiveProperty when the user toggles the tile.
        /// active=true → enable MSI Center M; active=false → disable.
        /// </summary>
        public void ApplyActive(bool active)
        {
            if (active)
                Enable();
            else
                Disable();
        }

        // 1:1 from HC ISpaceWatcher.Disable()
        private void Disable()
        {
            Logger.Info("[MsiCenterManager] Disabling MSI Center M...");

            DisableTasks();
            DisableServices();
            KillProcesses();
            DisableGameBarExtensionPackages();

            Logger.Info("[MsiCenterManager] MSI Center M disabled.");
        }

        // 1:1 from HC ISpaceWatcher.Enable()
        private void Enable()
        {
            Logger.Info("[MsiCenterManager] Re-enabling MSI Center M...");

            EnableGameBarExtensionPackages();
            EnableTasks();
            EnableServices();

            Logger.Info("[MsiCenterManager] MSI Center M re-enabled.");
        }

        // ── Tasks — 1:1 from HC ISpaceWatcher ───────────────────────────

        // 1:1 from HC ISpaceWatcher.KillProcesses()
        private static void KillProcesses()
        {
            foreach (string name in executableNames)
            {
                try
                {
                    foreach (Process process in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            if (!process.HasExited)
                                process.Kill();
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch { }
            }
        }

        // 1:1 from HC ISpaceWatcher.DisableTasks()
        private static void DisableTasks()
        {
            foreach (TaskScheduled task in GetTasks())
            {
                if (task.Enabled)
                {
                    task.Stop();
                    task.Enabled = false;
                }
            }
        }

        // 1:1 from HC ISpaceWatcher.EnableTasks()
        private static void EnableTasks()
        {
            foreach (TaskScheduled task in GetTasks())
            {
                if (!task.Enabled)
                {
                    task.Enabled = true;
                    task.Run();
                }
            }
        }

        // ── Services — 1:1 from HC ISpaceWatcher ────────────────────────

        // 1:1 from HC ISpaceWatcher.GetServices()
        private static List<ServiceController> GetServices()
        {
            List<ServiceController> result = new List<ServiceController>();
            foreach (string serviceName in serviceNames)
            {
                ServiceController sc = new ServiceController(serviceName);
                try
                {
                    // Accessing Status will throw InvalidOperationException if the service doesn't exist
                    _ = sc.Status;
                    result.Add(sc);
                }
                catch (InvalidOperationException)
                {
                    sc.Dispose();
                }
            }
            return result;
        }

        // 1:1 from HC ISpaceWatcher.DisableServices()
        private static void DisableServices()
        {
            foreach (ServiceController service in GetServices())
            {
                try
                {
                    ServiceUtils.ChangeStartMode(service, ServiceStartMode.Disabled, out string error);

                    if (service.Status != ServiceControllerStatus.Stopped)
                        service.Stop();
                }
                finally
                {
                    service.Dispose();
                }
            }
        }

        // 1:1 from HC ISpaceWatcher.EnableServices()
        private static void EnableServices()
        {
            foreach (ServiceController service in GetServices())
            {
                try
                {
                    ServiceUtils.ChangeStartMode(service, ServiceStartMode.Automatic, out string error);

                    if (service.Status != ServiceControllerStatus.Running)
                        service.Start();
                }
                finally
                {
                    service.Dispose();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
