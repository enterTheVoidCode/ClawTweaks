using NLog;
using Shared.Constants;
using Shared.Data;
using Shared.IPC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Input.Preview.Injection;
using XboxGamingBarHelper.AMD;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.LosslessScaling;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Systems;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.DefaultGameProfiles;
using XboxGamingBarHelper.Labs;
using Shared.Enums;

namespace XboxGamingBarHelper
{
    internal partial class Program
    {

        /// <summary>
        /// Initialize the package data folder path for uninstall detection.
        /// Called once during startup.
        /// We check the LocalState folder, not LocalCache, because:
        /// - LocalCache contains the running helper (can't be deleted while in use)
        /// - LocalState is the app's data folder that Windows removes during uninstall
        /// </summary>
        private static void InitializePackageDataFolder()
        {
            try
            {
                // Try to get the package's LocalState folder path
                try
                {
                    var localState = global::Windows.Storage.ApplicationData.Current.LocalFolder;
                    packageDataFolder = localState.Path;
                    Logger.Info($"Package LocalState folder for uninstall detection: {packageDataFolder}");
                }
                catch
                {
                    // Not running in package context - try to extract from exe path
                    // Helper runs from: C:\Users\<user>\AppData\Local\Packages\<PackageFamilyName>\LocalCache\GoTweaks\Helper\
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (exePath.Contains("Packages") && exePath.Contains("LocalCache"))
                    {
                        int packagesIdx = exePath.IndexOf("Packages", StringComparison.OrdinalIgnoreCase);
                        int localCacheIdx = exePath.IndexOf("LocalCache", StringComparison.OrdinalIgnoreCase);
                        if (packagesIdx > 0 && localCacheIdx > packagesIdx)
                        {
                            // Extract base: C:\Users\<user>\AppData\Local\Packages\<PackageFamilyName>
                            var packageBase = exePath.Substring(0, localCacheIdx).TrimEnd('\\');
                            // LocalState folder is at the same level as LocalCache
                            packageDataFolder = Path.Combine(packageBase, "LocalState");
                            Logger.Info($"Package LocalState folder (from exe path): {packageDataFolder}");
                        }
                    }
                }

                if (string.IsNullOrEmpty(packageDataFolder))
                {
                    Logger.Warn("Could not determine package LocalState folder for uninstall detection");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error initializing package data folder: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the MSIX package is still installed.
        /// We check if the LocalState folder exists - this is removed during uninstall.
        /// The LocalCache folder (where the helper runs from) may remain due to the running process.
        /// Returns true if installed, false if uninstalled.
        /// </summary>
        private static bool IsPackageInstalled()
        {
            if (string.IsNullOrEmpty(packageDataFolder))
                return true; // Can't detect, assume installed

            try
            {
                // Check if LocalState folder exists
                // When MSIX is uninstalled, LocalState is removed but LocalCache may remain
                // because files in use (like the running helper) can't be deleted
                return Directory.Exists(packageDataFolder);
            }
            catch
            {
                return true; // Can't check, assume installed
            }
        }

        /// <summary>
        /// Periodic check for package uninstallation.
        /// If uninstalled, cleans up the scheduled task and exits.
        /// </summary>
        private static void CheckForPackageUninstall()
        {
            if ((DateTime.Now - lastUninstallCheck).TotalMilliseconds < UninstallCheckIntervalMs)
                return;

            lastUninstallCheck = DateTime.Now;

            if (!IsPackageInstalled())
            {
                Logger.Info("=== Package uninstall detected! Cleaning up... ===");

                try
                {
                    // Remove the scheduled task
                    Logger.Info("Removing scheduled task...");
                    Services.ScheduledTaskService.RemoveTask();
                    Services.ScheduledTaskService.RemoveLegacyTaskIfExists();

                    // Delete heartbeat file
                    DeleteHeartbeatFile();

                    // Try to remove deployed files (may fail if in use, but that's OK)
                    try
                    {
                        Logger.Info("Attempting to remove deployed files...");
                        Services.HelperDeploymentService.RemoveDeployment();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Could not remove deployed files (may be in use): {ex.Message}");
                    }

                    // Launch cleanup script to delete the package folder after helper exits
                    LaunchCleanupScript();

                    Logger.Info("Cleanup complete. Exiting helper.");
                    LogManager.Flush();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error during uninstall cleanup: {ex.Message}");
                }

                // Exit the helper
                _isShuttingDown = true;

                // Release mutex before exiting to ensure clean restart
                try
                {
                    singleInstanceMutex?.ReleaseMutex();
                    singleInstanceMutex?.Dispose();
                }
                catch { /* Ignore mutex errors during shutdown */ }

                Environment.Exit(0);
            }
        }

        /// <summary>
        /// Launches a batch script from temp that waits for the helper to exit,
        /// then deletes the remaining package folder.
        /// Uses cmd.exe with rd /s /q which silently handles errors without dialogs.
        /// </summary>
        private static void LaunchCleanupScript()
        {
            try
            {
                // Get the package folder path (parent of LocalState)
                string packageFolder = Path.GetDirectoryName(packageDataFolder);
                if (string.IsNullOrEmpty(packageFolder) || !Directory.Exists(packageFolder))
                {
                    Logger.Warn("Cannot determine package folder for cleanup script");
                    return;
                }

                int helperPid = Process.GetCurrentProcess().Id;
                string scriptPath = Path.Combine(Path.GetTempPath(), $"GoTweaksCleanup_{helperPid}.cmd");

                // Batch script that:
                // 1. Waits for the helper process to exit (using tasklist polling)
                // 2. Waits a bit more for file handles to release
                // 3. Deletes the package folder with rd /s /q (silent, no error dialogs)
                // 4. Deletes itself
                string script = $@"@echo off
setlocal

:: GoTweaks Uninstall Cleanup Script
set HELPER_PID={helperPid}
set PACKAGE_FOLDER={packageFolder}

:: Wait for helper process to exit (poll every second, max 30 times)
set /a COUNT=0
:WAIT_LOOP
tasklist /FI ""PID eq %HELPER_PID%"" 2>nul | find /i ""%HELPER_PID%"" >nul
if errorlevel 1 goto PROCESS_EXITED
set /a COUNT+=1
if %COUNT% geq 30 goto PROCESS_EXITED
timeout /t 1 /nobreak >nul
goto WAIT_LOOP

:PROCESS_EXITED
:: Wait a bit more for file handles to release
timeout /t 3 /nobreak >nul

:: Delete the package folder (rd /s /q is silent and doesn't show error dialogs)
if exist ""%PACKAGE_FOLDER%"" (
    rd /s /q ""%PACKAGE_FOLDER%"" 2>nul
)

:: Delete this script
del /f /q ""%~f0"" 2>nul
";

                File.WriteAllText(scriptPath, script);
                Logger.Info($"Cleanup script created: {scriptPath}");

                // Launch cmd.exe hidden to run the cleanup script
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);
                Logger.Info("Cleanup script launched");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to launch cleanup script: {ex.Message}");
            }
        }

        /// <summary>
        /// Launches an upgrade script that waits for the helper to exit,
        /// copies new files from MSIX, and restarts the helper via scheduled task.
        /// This allows UAC-free upgrades since the current helper is already elevated.
        /// </summary>
        /// <param name="msixSourcePath">Path to the new helper files in the MSIX package</param>
        private static void LaunchUpgradeScript(string msixSourcePath)
        {
            try
            {
                string deployedFolder = Services.HelperDeploymentService.HelperFolder;
                string taskName = "GoTweaks\\GoTweaksHelper";
                int helperPid = Process.GetCurrentProcess().Id;
                string scriptPath = Path.Combine(Path.GetTempPath(), $"GoTweaksUpgrade_{helperPid}.cmd");

                Logger.Info($"Creating upgrade script: {msixSourcePath} -> {deployedFolder}");

                // Batch script that:
                // 1. Waits for the old helper to exit
                // 2. Copies all files from MSIX source to deployed location
                // 3. Writes version file
                // 4. Runs the scheduled task to start new helper
                // 5. Deletes itself
                string newVersion = Services.HelperDeploymentService.GetCurrentPackageVersion();
                string versionFile = Path.Combine(deployedFolder, ".version");

                string script = $@"@echo off
setlocal

:: GoTweaks Upgrade Script - UAC-free upgrade
set HELPER_PID={helperPid}
set SOURCE_PATH={msixSourcePath}
set DEPLOY_PATH={deployedFolder}
set TASK_NAME={taskName}
set VERSION_FILE={versionFile}
set NEW_VERSION={newVersion}

:: Wait for old helper process to exit (poll every second, max 30 times)
set /a COUNT=0
:WAIT_LOOP
tasklist /FI ""PID eq %HELPER_PID%"" 2>nul | find /i ""%HELPER_PID%"" >nul
if errorlevel 1 goto PROCESS_EXITED
set /a COUNT+=1
if %COUNT% geq 30 goto PROCESS_EXITED
timeout /t 1 /nobreak >nul
goto WAIT_LOOP

:PROCESS_EXITED
:: Wait a bit more for file handles to release
timeout /t 2 /nobreak >nul

:: Create deploy directory if needed
if not exist ""%DEPLOY_PATH%"" mkdir ""%DEPLOY_PATH%""

:: Copy all files from MSIX source to deployed location
xcopy /Y /Q ""%SOURCE_PATH%\*.*"" ""%DEPLOY_PATH%\"" >nul 2>&1

:: Write version file
echo %NEW_VERSION%> ""%VERSION_FILE%""

:: Run the scheduled task to start the new helper
schtasks /Run /TN ""%TASK_NAME%"" >nul 2>&1

:: Delete this script
timeout /t 1 /nobreak >nul
del /f /q ""%~f0"" 2>nul
";

                File.WriteAllText(scriptPath, script);
                Logger.Info($"Upgrade script created: {scriptPath}");

                // Launch cmd.exe hidden to run the upgrade script
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);
                Logger.Info("Upgrade script launched - helper will exit now");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to launch upgrade script: {ex.Message}");
            }
        }

    }
}
