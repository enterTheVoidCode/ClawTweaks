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
using Windows.ApplicationModel.AppService;
using Windows.System;
using Windows.UI.Input.Preview.Injection;
using XboxGamingBarHelper.AMD;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Legion;
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
    internal class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static Mutex singleInstanceMutex;
        private static AppServiceConnection connection = null;
        private static CancellationToken _serviceCancellationToken;
        private static bool _isRunningAsService = false;
        private static volatile bool _isShuttingDown = false;

        // P/Invoke for SetDllDirectory - must be called BEFORE any native DLLs are loaded
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        // Managers
        private static PerformanceManager performanceManager;
        private static RTSSManager rtssManager;
        private static ProfileManager profileManager;
        private static SystemManager systemManager;
        private static PowerManager powerManager;
        private static AMDManager amdManager;
        private static LosslessScalingManager losslessScalingManager;
        private static SettingsManager settingsManager;
        private static LegionManager legionManager;
        private static AutoTDPManager autoTDPManager;
        private static DefaultGameProfileManager defaultGameProfileManager;
        private static List<IManager> Managers;
        private static AppServiceConnectionStatus appServiceConnectionStatus;

        public static OnScreenDisplayProperty onScreenDisplay;
        public static List<OnScreenDisplayManager> onScreenDisplayProviders;

        // Properties
        private static HelperProperties properties;

        /// <summary>
        /// Guard flag to prevent reentrant profile change handling.
        /// Prevents race conditions during rapid game switches.
        /// Also used by TDPBoostProperties to skip redundant TDP re-apply during profile application.
        /// </summary>
        internal static bool isApplyingProfile = false;

        /// <summary>
        /// Lock object to ensure atomic profile application.
        /// Prevents race conditions when rapid game switches cause interleaved settings.
        /// </summary>
        private static readonly object profileApplicationLock = new object();

        /// <summary>
        /// Input injector for sending keyboard shortcuts (works in widget context unlike SendInput)
        /// </summary>
        private static InputInjector inputInjector;

        /// <summary>
        /// Labs: Unified Legion button monitor (handles both L and R buttons + battery)
        /// </summary>
        private static LegionButtonMonitor legionButtonMonitor;

        /// <summary>
        /// Hotkey manager for global keyboard shortcuts (Ctrl+Shift+D for Desktop Controls)
        /// </summary>
        private static HotkeyManager hotkeyManager;

        /// <summary>
        /// Named Pipe server for IPC with the widget (works when elevated via scheduled task)
        /// </summary>
        private static IPC.NamedPipeServer pipeServer;

        /// <summary>
        /// Heartbeat file path for widget to detect if helper is running
        /// </summary>
        private static string heartbeatFilePath;
        private static DateTime lastHeartbeatWrite = DateTime.MinValue;
        private const int HeartbeatIntervalMs = 2000;

        /// <summary>
        /// Helper version string for widget to detect version mismatch after updates
        /// </summary>
        private static string helperVersion = "0.0.0.0";

        /// <summary>
        /// Debounce for Focus GoTweaks to prevent rapid button presses from flooding the system
        /// </summary>
        private static DateTime lastFocusWidgetTime = DateTime.MinValue;
        private const int FocusWidgetDebounceMs = 200;

        /// <summary>
        /// Package uninstall detection - path to the package's data folder
        /// </summary>
        private static string packageDataFolder;
        private static DateTime lastUninstallCheck = DateTime.MinValue;
        private const int UninstallCheckIntervalMs = 60000; // Check every 60 seconds

        /// <summary>
        /// Configures NLog to write logs to the package's LocalCache/Local folder.
        /// Must be called BEFORE any logging happens.
        /// </summary>
        private static void ConfigureLogDirectory()
        {
            try
            {
                string logDir = null;

                // Try to get the package's LocalCache path
                try
                {
                    var localCache = global::Windows.Storage.ApplicationData.Current.LocalCacheFolder;
                    logDir = Path.Combine(localCache.Path, "Local");
                }
                catch
                {
                    // Not running in package context - try to extract from exe path
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (exePath.Contains("LocalCache"))
                    {
                        int idx = exePath.IndexOf("LocalCache", StringComparison.OrdinalIgnoreCase);
                        if (idx > 0)
                        {
                            logDir = Path.Combine(exePath.Substring(0, idx + "LocalCache".Length), "Local");
                        }
                    }
                }

                // Fallback to user's LocalAppData if we couldn't determine package path
                if (string.IsNullOrEmpty(logDir))
                {
                    logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                }

                // Ensure the log directory exists
                Directory.CreateDirectory(logDir);

                // Set the NLog GDC variable
                NLog.GlobalDiagnosticsContext.Set("LogDirectory", logDir);
            }
            catch
            {
                // If all else fails, use LocalApplicationData
                var fallback = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                NLog.GlobalDiagnosticsContext.Set("LogDirectory", fallback);
            }
        }

        static async Task Main(string[] args)
        {
            // Set log directory BEFORE any logging happens
            // This ensures logs go to the package's LocalCache/Local folder even when running elevated
            ConfigureLogDirectory();

            // Check for setup mode FIRST (before anything else)
            // Setup mode: deploy files, create scheduled task, run task, then EXIT
            // The task launches the elevated helper which will connect to the widget.
            if (args.Contains("--setup"))
            {
                Logger.Info("=== Setup Mode ===");
                try
                {
                    bool success = ElevationBootstrapper.PerformSetup();
                    Logger.Info($"Setup completed with result: {success}");

                    if (success)
                    {
                        // Run the scheduled task to start the elevated helper
                        // This helper will connect to the widget
                        Logger.Info("Running scheduled task to start elevated helper...");
                        if (Services.ScheduledTaskService.RunTaskNow())
                        {
                            Logger.Info("Scheduled task started successfully");
                        }
                        else
                        {
                            Logger.Warn("Failed to run scheduled task - widget will need to relaunch");
                        }
                    }

                    Logger.Info("Exiting setup mode");
                    LogManager.Flush();
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Setup failed with exception");
                    LogManager.Flush();
                    return; // Exit on setup failure
                }
            }

            // Check if running as a Windows Service (MSIX Desktop Service)
            // Services are started by SCM and have no console/interactive session
            bool isService = !Environment.UserInteractive;

            if (isService)
            {
                // Running as Windows Service - let SCM handle the lifecycle
                Logger.Info("Starting as Windows Service");
                _isRunningAsService = true;
                ServiceBase.Run(new GoTweaksService());
                return;
            }

            // Running interactively (console/debug mode or via FullTrustProcessLauncher)
            Logger.Info("Starting in interactive mode");

            // Self-elevation bootstrap - only needed in interactive mode
            // Service runs as LocalSystem which is already elevated
            if (!ElevationBootstrapper.EnsureElevated(args))
            {
                return; // Relaunching elevated via scheduled task, exit this instance
            }

            // Ensure only one instance of the helper runs at a time
            const string mutexName = "Global\\XboxGamingBarHelper_SingleInstance";
            bool createdNew;

            try
            {
                singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create mutex: {ex.Message}");
                return;
            }

            if (!createdNew)
            {
                Logger.Warn("Another instance of XboxGamingBarHelper is already running. Exiting.");
                return;
            }

            Logger.Info("Single instance mutex acquired. Starting helper.");

            try
            {
                await Initialize();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "FATAL: Unhandled exception in Initialize()");
                LogManager.Flush();
                throw;
            }
            finally
            {
                singleInstanceMutex?.ReleaseMutex();
                singleInstanceMutex?.Dispose();
            }
        }

        /// <summary>
        /// Entry point when running as a Windows Service.
        /// Called by GoTweaksService.OnStart().
        /// </summary>
        public static async Task RunAsService(CancellationToken cancellationToken)
        {
            Logger.Info("RunAsService starting...");
            _serviceCancellationToken = cancellationToken;
            _isRunningAsService = true;

            // Ensure only one instance of the helper runs at a time
            const string mutexName = "Global\\XboxGamingBarHelper_SingleInstance";
            bool createdNew;

            try
            {
                singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create mutex: {ex.Message}");
                return;
            }

            if (!createdNew)
            {
                Logger.Warn("Another instance of XboxGamingBarHelper is already running. Service will wait.");
                // In service mode, we might want to wait for the other instance to exit
                // For now, just return - the service will be marked as started but won't do anything
                return;
            }

            Logger.Info("Single instance mutex acquired. Starting service helper.");

            try
            {
                await Initialize();
            }
            finally
            {
                singleInstanceMutex?.ReleaseMutex();
                singleInstanceMutex?.Dispose();
            }
        }

        /// <summary>
        /// Cleanup when service is stopping.
        /// Called by GoTweaksService.OnStop().
        /// </summary>
        public static void Shutdown()
        {
            Logger.Info("Shutdown called");
            _isShuttingDown = true;

            try
            {
                // Dispose managers
                if (Managers != null)
                {
                    foreach (var manager in Managers)
                    {
                        try
                        {
                            (manager as IDisposable)?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Error disposing manager {manager.GetType().Name}");
                        }
                    }
                }

                // Dispose connection
                connection?.Dispose();
                connection = null;

                // Dispose hotkey manager
                hotkeyManager?.Dispose();
                hotkeyManager = null;

                // Dispose Legion button monitor
                legionButtonMonitor?.Dispose();
                legionButtonMonitor = null;

                // Delete heartbeat file on shutdown
                DeleteHeartbeatFile();

                Logger.Info("Shutdown complete");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during shutdown");
            }
        }

        /// <summary>
        /// Write heartbeat file so widget can detect if helper is running.
        /// Called every HeartbeatIntervalMs in main loop.
        /// </summary>
        private static void WriteHeartbeat()
        {
            if ((DateTime.Now - lastHeartbeatWrite).TotalMilliseconds < HeartbeatIntervalMs)
                return;

            try
            {
                if (string.IsNullOrEmpty(heartbeatFilePath))
                {
                    // Initialize heartbeat file path on first write
                    string localStateFolder;
                    try
                    {
                        // Try to use Package.Current (works when running in package context)
                        localStateFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Packages",
                            Package.Current.Id.FamilyName,
                            "LocalState"
                        );
                    }
                    catch
                    {
                        // Fallback for elevated mode (no package identity)
                        // Use hardcoded package family name (same as LocalSettingsHelper)
                        localStateFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Packages",
                            "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg",
                            "LocalState"
                        );
                    }
                    heartbeatFilePath = Path.Combine(localStateFolder, "helper_heartbeat.json");
                }

                var heartbeat = new
                {
                    pid = Process.GetCurrentProcess().Id,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    connected = appServiceConnectionStatus == AppServiceConnectionStatus.Success || (pipeServer?.IsConnected ?? false),
                    elevated = ElevationBootstrapper.IsRunningAsAdmin(),
                    version = helperVersion
                };

                string json = $"{{\"pid\":{heartbeat.pid},\"timestamp\":{heartbeat.timestamp},\"connected\":{heartbeat.connected.ToString().ToLower()},\"elevated\":{heartbeat.elevated.ToString().ToLower()},\"version\":\"{heartbeat.version}\"}}";
                File.WriteAllText(heartbeatFilePath, json);
                lastHeartbeatWrite = DateTime.Now;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to write heartbeat: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete heartbeat file on shutdown so widget knows helper is not running.
        /// </summary>
        private static void DeleteHeartbeatFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(heartbeatFilePath) && File.Exists(heartbeatFilePath))
                {
                    File.Delete(heartbeatFilePath);
                    Logger.Info("Heartbeat file deleted");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to delete heartbeat file: {ex.Message}");
            }
        }

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

        /// <summary>
        /// Initialize the app service connection and named pipe server
        /// </summary>
        private static void InitializeConnection()
        {
            Logger.Info("Initialize connection...");

            // Start Named Pipe server (primary communication method - works when elevated)
            try
            {
                pipeServer = new IPC.NamedPipeServer();
                pipeServer.MessageReceived += PipeServer_MessageReceived;
                pipeServer.Connected += (s, e) => Logger.Info("Widget connected via Named Pipe");
                pipeServer.Disconnected += (s, e) => Logger.Info("Widget disconnected from Named Pipe");
                pipeServer.Start();
                Logger.Info($"Named Pipe server started: {IPC.NamedPipeServer.FullPipePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start Named Pipe server: {ex.Message}");
            }

            // Also initialize AppServiceConnection as fallback (for non-elevated scenarios)
            try
            {
                connection = new AppServiceConnection();
                connection.AppServiceName = "XboxGamingBarService";
                connection.PackageFamilyName = Package.Current.Id.FamilyName;
                connection.RequestReceived += Connection_RequestReceived;
                connection.ServiceClosed += Connection_ServiceClosed;
            }
            catch (Exception ex)
            {
                Logger.Warn($"AppServiceConnection setup failed (normal if running elevated): {ex.Message}");
                // Clear the connection so we don't try to use a broken AppServiceConnection
                connection?.Dispose();
                connection = null;
            }
        }

        /// <summary>
        /// Initialize DefaultGameProfileManager in background.
        /// Deferred because it's not needed for initial UI and takes ~600ms.
        /// DGP only activates when a game starts running.
        /// </summary>
        private static async void InitializeDefaultGameProfileManagerAsync()
        {
            await Task.Run(() =>
            {
                var dgpTimer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    defaultGameProfileManager = new DefaultGameProfileManager(connection, performanceManager, rtssManager, systemManager, profileManager, legionManager);

                    if (defaultGameProfileManager != null)
                    {
                        // Wait for Managers list to be initialized before trying to lock it
                        while (Managers == null)
                        {
                            Thread.Sleep(10);
                        }

                        // Add to Managers list for cleanup
                        lock (Managers)
                        {
                            Managers.Add(defaultGameProfileManager);
                        }

                        // Wait for properties to be initialized before adding DGP properties
                        // The properties object is created in Initialize() which runs before this
                        while (properties == null)
                        {
                            Thread.Sleep(10);
                        }

                        // Add properties dynamically using thread-safe Add method
                        properties.Add(defaultGameProfileManager.ProfileAvailable);
                        properties.Add(defaultGameProfileManager.ProfileData);
                        properties.Add(defaultGameProfileManager.ProfileEnabled);
                        properties.Add(defaultGameProfileManager.ForceProfile);

                        Logger.Info("DefaultGameProfileManager properties added to helper");
                    }

                    dgpTimer.Stop();
                    Logger.Info($"[TIMING] DefaultGameProfileManager (background): {dgpTimer.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to initialize DefaultGameProfileManager: {ex.Message}");
                    Logger.Error($"Stack trace: {ex.StackTrace}");
                    defaultGameProfileManager = null;
                }
            });
        }

        /// <summary>
        /// Connect to the widget with optional blocking retry
        /// </summary>
        private static async Task ConnectToWidget(bool blocking)
        {
            // If running elevated (no MSIX context), AppServiceConnection won't work
            // In this case, we rely entirely on Named Pipes for IPC
            if (connection == null)
            {
                Logger.Info("AppServiceConnection unavailable (running elevated). Using Named Pipes only.");
                appServiceConnectionStatus = AppServiceConnectionStatus.AppServiceUnavailable;

                // For blocking mode, wait for pipe connection instead
                if (blocking && pipeServer != null)
                {
                    Logger.Info("Waiting for widget to connect via Named Pipe...");
                    // Don't block forever - the main loop will handle reconnection
                    // Just mark as "connected" so the main loop starts
                    // The pipe server is already listening for connections
                }
                return;
            }

            if (blocking)
            {
                do
                {
                    Logger.Info("Start connecting to the widget.");
                    try
                    {
                        appServiceConnectionStatus = await connection.OpenAsync();
                    }
                    catch (Exception exception)
                    {
                        Logger.Error($"Exception occurred when connecting to the widget: {exception}");
                        appServiceConnectionStatus = AppServiceConnectionStatus.AppServiceUnavailable;
                    }

                    if (appServiceConnectionStatus != AppServiceConnectionStatus.Success)
                    {
                        Logger.Info("Can't connect to the widget. Try again in 1 second...");
                        await Task.Delay(1000);
                    }
                } while (appServiceConnectionStatus != AppServiceConnectionStatus.Success);
                Logger.Info("Connected to the widget.");
            }
            else
            {
                Logger.Info("Start trying to connect to the widget.");
                try
                {
                    appServiceConnectionStatus = await connection.OpenAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Exception occurred when trying to connect to the widget.");
                    appServiceConnectionStatus = AppServiceConnectionStatus.AppServiceUnavailable;
                }

                Logger.Info($"Try to connect to the widget: {appServiceConnectionStatus}.");
            }
        }

        /// <summary>
        /// Open connection to UWP app service
        /// </summary>
        private static async Task Initialize()
        {
            var initTimer = System.Diagnostics.Stopwatch.StartNew();

            // NOTE: InitializeConnection() (pipe server) is now called AFTER all managers are initialized
            // to prevent the widget from connecting before we're ready to handle BatchGet requests.

            // Initialize package data folder path for uninstall detection
            InitializePackageDataFolder();

            //while (!System.Diagnostics.Debugger.IsAttached)
            //{
            //    await Task.Delay(500);
            //}

            // Set DLL directory to exe location BEFORE loading any native DLLs (ADLX, etc.)
            // This is critical for elevated helper running from deployed location
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var exeDir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(exeDir))
                    {
                        SetDllDirectory(exeDir);
                        Logger.Info($"SetDllDirectory to: {exeDir}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not set DLL directory: {ex.Message}");
            }

            // PARALLEL MANAGER INITIALIZATION - Wave-based to respect dependencies
            var totalTimer = System.Diagnostics.Stopwatch.StartNew();
            Logger.Info("Initialize managers (parallel waves)...");

            // Wave 1: Independent managers (no dependencies) - run in parallel
            var wave1Timer = System.Diagnostics.Stopwatch.StartNew();
            Logger.Info("Wave 1: PerformanceManager, ProfileManager, AMDManager, LosslessScalingManager, SettingsManager, LegionManager");

            PerformanceManager tempPerfMgr = null;
            ProfileManager tempProfileMgr = null;
            AMDManager tempAmdMgr = null;
            LosslessScalingManager tempLosslessMgr = null;
            SettingsManager tempSettingsMgr = null;
            LegionManager tempLegionMgr = null;

            var wave1Tasks = new[]
            {
                Task.Run(() => {
                    try { tempPerfMgr = new PerformanceManager(connection); Logger.Info("Wave1: PerformanceManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: PerformanceManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempProfileMgr = new ProfileManager(connection); Logger.Info("Wave1: ProfileManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: ProfileManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempAmdMgr = new AMDManager(connection); Logger.Info("Wave1: AMDManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: AMDManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempLosslessMgr = new LosslessScalingManager(connection); Logger.Info("Wave1: LosslessScalingManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: LosslessScalingManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempSettingsMgr = SettingsManager.CreateInstance(connection); Logger.Info("Wave1: SettingsManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: SettingsManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempLegionMgr = new LegionManager(connection); Logger.Info("Wave1: LegionManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: LegionManager FAILED"); throw; }
                })
            };

            try
            {
                Task.WaitAll(wave1Tasks);
            }
            catch (AggregateException ae)
            {
                Logger.Error("Wave1 Task.WaitAll failed with AggregateException:");
                foreach (var ex in ae.InnerExceptions)
                {
                    Logger.Error(ex, $"  Inner exception: {ex.GetType().Name}");
                }
                throw;
            }

            performanceManager = tempPerfMgr;
            profileManager = tempProfileMgr;
            amdManager = tempAmdMgr;
            losslessScalingManager = tempLosslessMgr;
            settingsManager = tempSettingsMgr;
            legionManager = tempLegionMgr;

            wave1Timer.Stop();
            Logger.Info($"[TIMING] Wave 1 (parallel): {wave1Timer.ElapsedMilliseconds}ms");

            // Wave 2: Managers that depend on Wave 1 - run in parallel
            var wave2Timer = System.Diagnostics.Stopwatch.StartNew();
            Logger.Info("Wave 2: RTSSManager, SystemManager, PowerManager");

            RTSSManager tempRtssMgr = null;
            SystemManager tempSystemMgr = null;
            PowerManager tempPowerMgr = null;

            var wave2Tasks = new[]
            {
                Task.Run(() => { tempRtssMgr = new RTSSManager(performanceManager, connection); }),
                Task.Run(() => { tempSystemMgr = new SystemManager(connection, profileManager.GameProfiles); }),
                Task.Run(() => { tempPowerMgr = new PowerManager(connection, performanceManager.RyzenAdjHandle); })
            };
            Task.WaitAll(wave2Tasks);

            rtssManager = tempRtssMgr;
            systemManager = tempSystemMgr;
            powerManager = tempPowerMgr;

            wave2Timer.Stop();
            Logger.Info($"[TIMING] Wave 2 (parallel): {wave2Timer.ElapsedMilliseconds}ms");

            // Wave 3: Managers that depend on Wave 2
            var wave3Timer = System.Diagnostics.Stopwatch.StartNew();
            Logger.Info("Wave 3: AutoTDPManager");
            autoTDPManager = new AutoTDPManager(connection, performanceManager, systemManager);
            wave3Timer.Stop();
            Logger.Info($"[TIMING] Wave 3: {wave3Timer.ElapsedMilliseconds}ms");

            totalTimer.Stop();
            Logger.Info($"[TIMING] All managers total (parallel): {totalTimer.ElapsedMilliseconds}ms");

            // Initialize DefaultGameProfileManager in background - not needed for initial UI
            // Deferred to avoid blocking startup - DGP only kicks in when a game is running
            Logger.Info("Initialize Default Game Profile Manager (deferred to background).");
            InitializeDefaultGameProfileManagerAsync();

            // Initialize input injector for keyboard shortcuts (works in widget context unlike SendInput)
            inputInjector = InputInjector.TryCreate();
            if (inputInjector == null)
            {
                Logger.Warn("Failed to create InputInjector - keyboard shortcuts may not work in widget");
            }

            // Set LegionManager reference in PerformanceManager for WMI TDP support
            performanceManager.SetLegionManager(legionManager);

            // Set PerformanceManager reference in LegionManager for CPU temperature sensor access
            legionManager.SetPerformanceManager(performanceManager);

            // PawnIO/RyzenSMU initialization for anti-cheat compatible TDP control
            // Priority: Legion WMI > PawnIO/RyzenSMU > RyzenAdj (deprecated, WinRing0 not bundled)
            // Uses official signed module from release 0.2.1
            // Supported CPUs: StrixHalo (Ryzen AI Max 385/395), etc.
            performanceManager.InitializePawnIO();

            // Set LegionManager reference in RTSSManager for fan speed OSD support
            rtssManager.SetLegionManager(legionManager);

            // Set controller battery callbacks in RTSSManager for Controller Battery OSD item
            rtssManager.SetControllerBatteryCallbacks(
                () => legionManager.GetLeftControllerBattery(),
                () => legionManager.GetRightControllerBattery(),
                () => legionManager.IsLeftControllerCharging(),
                () => legionManager.IsRightControllerCharging()
            );

            // Start Legion button monitor for battery monitoring (even when button remap is disabled)
            // This allows controller battery to be monitored without requiring button remapping
            if (legionManager.LegionGoDetected.Value)
            {
                try
                {
                    // Load cached HID device path for faster startup
                    LegionButtonMonitor.LoadCachedDevicePathFromSettings();

                    legionButtonMonitor = new LegionButtonMonitor();
                    legionButtonMonitor.BatteryUpdated += (sender, e) =>
                    {
                        try
                        {
                            legionManager?.UpdateControllerBatteryFromButtonMonitor(
                                e.LeftBattery, e.LeftCharging, e.LeftConnected,
                                e.RightBattery, e.RightCharging, e.RightConnected);

                            // Also sync VID:PID on battery updates to ensure it gets sent once connection is ready
                            var vidPid = legionButtonMonitor?.DetectedVidPid;
                            if (!string.IsNullOrEmpty(vidPid))
                            {
                                legionManager?.UpdateControllerVidPid(vidPid);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"BatteryUpdated handler exception: {ex.Message}");
                        }
                    };

                    if (legionButtonMonitor.StartForBatteryMonitoring())
                    {
                        Logger.Info("Legion button monitor started for battery monitoring");
                        // Update VID:PID in LegionManager
                        var vidPid = legionButtonMonitor.DetectedVidPid;
                        Logger.Info($"Legion button monitor VID:PID after start: '{vidPid}'");
                        if (!string.IsNullOrEmpty(vidPid))
                        {
                            legionManager.UpdateControllerVidPid(vidPid);
                        }
                        else
                        {
                            Logger.Warn("Legion button monitor VID:PID is empty after start");
                        }
                    }
                    else
                    {
                        Logger.Warn("Failed to start Legion button monitor for battery monitoring");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error initializing Legion button monitor for battery: {ex.Message}");
                }
            }

            // Set AutoTDPManager reference in RTSSManager for AutoTDP OSD support
            rtssManager.SetAutoTDPManager(autoTDPManager);

            // Initialize Display/OSD config (position shift handled by RTSSManager, adaptive brightness by SystemManager)
            Logger.Info("Initialize DisplayOSD Config.");
            rtssManager.InitializeDisplayOSDConfig(systemManager.SetAdaptiveBrightness);

            // Initialize global hotkey manager (Ctrl+Shift+D to toggle Desktop Controls)
            InitializeHotkeyManager();

            Managers = new List<IManager>
            {
                performanceManager,
                rtssManager,
                profileManager,
                systemManager,
                powerManager,
                amdManager,
                losslessScalingManager,
                settingsManager,
                legionManager,
                autoTDPManager
            };
            // Note: defaultGameProfileManager is added in background task when ready

            Logger.Info("Initialize properties.");
            onScreenDisplay = new OnScreenDisplayProperty(0, null, rtssManager);
            onScreenDisplayProviders = new List<OnScreenDisplayManager>() { rtssManager, amdManager };
            //onScreenDisplay = new OnScreenDisplayProperty(0, null, amdManager);

            // Build properties list (conditionally add DefaultGameProfile if available)
            var propertyList = new List<FunctionalProperty>
            {
                systemManager.RunningGame,
                onScreenDisplay,
                performanceManager.TDP,
                performanceManager.CurrentTDP,
                profileManager.PerGameProfile,
                profileManager.DeleteGameProfile,
                powerManager.CPUBoost,
                powerManager.CPUEPP,
                powerManager.MaxCPUState,
                powerManager.MinCPUState,
                powerManager.OSPowerMode,
                // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
                //powerManager.LimitGPUClock,
                //powerManager.GPUClockMin,
                //powerManager.GPUClockMax,
                systemManager.RefreshRates,
                systemManager.RefreshRate,
                systemManager.Resolutions,
                systemManager.Resolution,
                systemManager.HDRSupported,
                systemManager.HDREnabled,
                systemManager.CPUCoreConfig,
                systemManager.CPUCoreActiveConfig,
                systemManager.CoreParkingPercent,
                systemManager.TrackedGame,
                rtssManager.RTSSInstalled,
                rtssManager.OSDConfig,
                rtssManager.FPSLimit,
                rtssManager.DisplayOSDConfig,
                settingsManager.IsForeground,
                amdManager.AMDRadeonSuperResolutionEnabled,
                amdManager.AMDRadeonSuperResolutionSupported,
                amdManager.AMDRadeonSuperResolutionSharpness,
                amdManager.AMDFluidMotionFrameEnabled,
                amdManager.AMDFluidMotionFrameSupported,
                amdManager.AMDRadeonAntiLagEnabled,
                amdManager.AMDRadeonAntiLagSupported,
                amdManager.AMDRadeonBoostEnabled,
                amdManager.AMDRadeonBoostSupported,
                amdManager.AMDRadeonBoostResolution,
                amdManager.AMDRadeonChillEnabled,
                amdManager.AMDRadeonChillSupported,
                amdManager.AMDRadeonChillMinFPS,
                amdManager.AMDRadeonChillMaxFPS,
                amdManager.AMDImageSharpeningEnabled,
                amdManager.AMDImageSharpeningSupported,
                amdManager.AMDImageSharpeningSharpness,
                amdManager.AMDDisplayBrightnessSupported,
                amdManager.AMDDisplayBrightness,
                amdManager.AMDDisplayContrastSupported,
                amdManager.AMDDisplayContrast,
                amdManager.AMDDisplaySaturationSupported,
                amdManager.AMDDisplaySaturation,
                amdManager.AMDDisplayTemperatureSupported,
                amdManager.AMDDisplayTemperature,
                losslessScalingManager.LosslessScalingInstalled,
                losslessScalingManager.LosslessScalingRunning,
                losslessScalingManager.LosslessScalingEnabled,
                losslessScalingManager.LosslessScalingCurrentProfile,
                losslessScalingManager.LosslessScalingScalingType,
                losslessScalingManager.LosslessScalingFrameGenType,
                losslessScalingManager.LosslessScalingLSFG3Mode,
                losslessScalingManager.LosslessScalingLSFG3Multiplier,
                losslessScalingManager.LosslessScalingLSFG3Target,
                losslessScalingManager.LosslessScalingLSFG2Mode,
                losslessScalingManager.LosslessScalingFlowScale,
                losslessScalingManager.LosslessScalingSize,
                losslessScalingManager.LosslessScalingAutoScale,
                losslessScalingManager.LosslessScalingAutoScaleDelay,
                losslessScalingManager.LosslessScalingSaveAndRestart,
                losslessScalingManager.LosslessScalingCreateProfile,
                losslessScalingManager.LosslessScalingBringToForeground,
                losslessScalingManager.LosslessScalingLaunch,
                settingsManager.AutoStartRTSS,
                settingsManager.OnScreenDisplayProvider,
                settingsManager.UseManufacturerWMI,
                settingsManager.TdpMethod,
                // Profile Detection Settings
                settingsManager.ProfileMatchByExe,
                settingsManager.ProfileCustomGamePath,
                settingsManager.ProfileGamesOnly,
                settingsManager.ProfileBlacklistPaths,
                systemManager.ForegroundApp,
                legionManager.LegionGoDetected,
                legionManager.LegionTouchpadEnabled,
                legionManager.LegionLightMode,
                legionManager.LegionLightColor,
                legionManager.LegionLightBrightness,
                legionManager.LegionLightSpeed,
                legionManager.LegionPerformanceMode,
                legionManager.LegionCustomTDPSlow,
                legionManager.LegionCustomTDPFast,
                legionManager.LegionCustomTDPPeak,
                legionManager.LegionFanFullSpeed,
                legionManager.LegionFanCurveData,
                legionManager.LegionCPUCurrentTemp,
                legionManager.LegionFanSensorTemp,
                legionManager.LegionCPUFanRPM,
                legionManager.LegionFanCurveVisible,
                legionManager.LegionGyroEnabled,
                legionManager.LegionVibration,
                legionManager.LegionPowerLight,
                legionManager.LegionChargeLimit,
                legionManager.LegionButtonY1,
                legionManager.LegionButtonY2,
                legionManager.LegionButtonY3,
                legionManager.LegionButtonM1,
                legionManager.LegionButtonM2,
                legionManager.LegionButtonM3,
                legionManager.LegionButtonDesktop,
                legionManager.LegionButtonPage,
                legionManager.LegionNintendoLayout,
                legionManager.LegionVibrationMode,
                legionManager.LegionControllerProfileEnabled,
                // Gyro properties
                legionManager.LegionGyroTarget,
                legionManager.LegionGyroSensitivityX,
                legionManager.LegionGyroSensitivityY,
                legionManager.LegionGyroInvertX,
                legionManager.LegionGyroInvertY,
                legionManager.LegionGyroMappingType,
                legionManager.LegionGyroActivationMode,
                legionManager.LegionGyroActivationButton,
                // Gyro deadzone property
                legionManager.LegionGyroDeadzone,
                // Stick deadzone properties
                legionManager.LegionLeftStickDeadzone,
                legionManager.LegionRightStickDeadzone,
                // Trigger travel properties
                legionManager.LegionLeftTriggerStart,
                legionManager.LegionLeftTriggerEnd,
                legionManager.LegionRightTriggerStart,
                legionManager.LegionRightTriggerEnd,
                legionManager.LegionHairTriggers,
                // Touchpad vibration (GLOBAL setting)
                legionManager.LegionTouchpadVibration,
                // Joystick as mouse properties
                legionManager.LegionJoystickAsMouseMode,
                legionManager.LegionJoystickMouseSens,
                // Gamepad button mapping
                legionManager.LegionGamepadMapping,
                // Desktop controls preset (state tracking for UI sync)
                legionManager.LegionDesktopControls,
                // Controller battery properties (read-only, from HID)
                legionManager.ControllerBatteryLeft,
                legionManager.ControllerBatteryRight,
                legionManager.ControllerChargingLeft,
                legionManager.ControllerChargingRight,
                legionManager.ControllerConnectedLeft,
                legionManager.ControllerConnectedRight,
                legionManager.ControllerVidPid,
                autoTDPManager.Enabled,
                autoTDPManager.TargetFPS,
                autoTDPManager.CurrentFPS,
                autoTDPManager.MinTDP,
                autoTDPManager.MaxTDP,
                autoTDPManager.TDPLimits,
                systemManager.ForceParkMode,
                performanceManager.TDPBoostEnabled,
                performanceManager.TDPBoostSPPT,
                performanceManager.TDPBoostFPPT,
                // performanceManager.WinRing0AvailableProperty, // WinRing0 removed - deprecated
                performanceManager.PawnIOAvailableProperty,
                performanceManager.PawnIOInstalledProperty,
                performanceManager.InstallPawnIOProperty
            };

            // Note: DefaultGameProfileManager properties are added dynamically in background task

            // Initialize properties
            properties = new HelperProperties(propertyList.ToArray());

            Logger.Info("Initialize callbacks.");
            systemManager.RunningGame.PropertyChanged += RunningGame_PropertyChanged;
            systemManager.ResumeFromSleep += SystemManager_ResumeFromSleep;
            profileManager.PerGameProfile.PropertyChanged += PerGameProfile_PropertyChanged;
            performanceManager.TDP.PropertyChanged += TDP_PropertyChanged;
            performanceManager.TDPBoostEnabled.PropertyChanged += TDPBoostEnabled_PropertyChanged;
            powerManager.CPUBoost.PropertyChanged += CPUBoost_PropertyChanged;
            powerManager.CPUEPP.PropertyChanged += CPUEPP_PropertyChanged;
            powerManager.MaxCPUState.PropertyChanged += CPUState_PropertyChanged;
            powerManager.MinCPUState.PropertyChanged += CPUState_PropertyChanged;
            // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
            //powerManager.LimitGPUClock.PropertyChanged += GPUClock_PropertyChanged;
            //powerManager.GPUClockMin.PropertyChanged += GPUClock_PropertyChanged;
            //powerManager.GPUClockMax.PropertyChanged += GPUClock_PropertyChanged;
            profileManager.CurrentProfile.PropertyChanged += CurrentProfile_PropertyChanged;

            // Subscribe to Legion controller property changes to save to profile
            if (legionManager != null)
            {
                // Button mappings
                legionManager.LegionButtonY1.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionButtonY2.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionButtonY3.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionButtonM1.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionButtonM2.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionButtonM3.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionButtonDesktop.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionButtonPage.PropertyChanged += LegionControllerSetting_PropertyChanged;
                // Gyro settings
                legionManager.LegionGyroActivationButton.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionGyroTarget.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionGyroSensitivityX.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionGyroSensitivityY.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionGyroInvertX.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionGyroInvertY.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionGyroMappingType.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionGyroActivationMode.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionGyroDeadzone.PropertyChanged += LegionControllerSetting_PropertyChanged;
                // Stick deadzones
                legionManager.LegionLeftStickDeadzone.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionRightStickDeadzone.PropertyChanged += LegionControllerSetting_PropertyChanged;
                // Trigger travel
                legionManager.LegionLeftTriggerStart.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionLeftTriggerEnd.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionRightTriggerStart.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionRightTriggerEnd.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionHairTriggers.PropertyChanged += LegionControllerSetting_PropertyChanged;
                // Joystick as mouse
                legionManager.LegionJoystickAsMouseMode.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionJoystickMouseSens.PropertyChanged += LegionControllerSetting_PropertyChanged;
                // Gamepad mapping (24 buttons JSON)
                legionManager.LegionGamepadMapping.PropertyChanged += LegionControllerSetting_PropertyChanged;
                // Other controller settings
                legionManager.LegionNintendoLayout.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionVibration.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionVibrationMode.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionControllerProfileEnabled.PropertyChanged += LegionControllerSetting_PropertyChanged;
                // Performance mode (for per-game TDP mode)
                legionManager.LegionPerformanceMode.PropertyChanged += LegionControllerSetting_PropertyChanged;
                // Lighting settings (per-game lighting profiles)
                legionManager.LegionLightMode.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionLightColor.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionLightBrightness.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionLightSpeed.PropertyChanged += LegionControllerSetting_PropertyChanged;
                legionManager.LegionPowerLight.PropertyChanged += LegionControllerSetting_PropertyChanged;
            }

            // AutoTDP settings (per-game AutoTDP profiles)
            if (autoTDPManager != null)
            {
                autoTDPManager.Enabled.PropertyChanged += AutoTDPSetting_PropertyChanged;
                autoTDPManager.TargetFPS.PropertyChanged += AutoTDPSetting_PropertyChanged;
                autoTDPManager.MinTDP.PropertyChanged += AutoTDPSetting_PropertyChanged;
                autoTDPManager.MaxTDP.PropertyChanged += AutoTDPSetting_PropertyChanged;
            }

            initTimer.Stop();
            Logger.Info($"[TIMING] Helper initialization (before connect): {initTimer.ElapsedMilliseconds}ms");

            // Start pipe server AFTER all managers are initialized
            // This ensures BatchGet requests can be handled immediately when widget connects
            InitializeConnection();
            Logger.Info("Pipe server started - helper ready for widget connections");

            // Initial blocking connection to widget
            var connectTimer = System.Diagnostics.Stopwatch.StartNew();
            await ConnectToWidget(true);
            connectTimer.Stop();
            Logger.Info($"[TIMING] Widget connection: {connectTimer.ElapsedMilliseconds}ms");

            Logger.Info($"Widget connection status: {appServiceConnectionStatus}");

            // Start battery monitoring after widget connection is established
            // (Starting before connection can cause issues with the AppService)
            if (legionManager != null)
            {
                legionManager.StartBatteryMonitoringIfConnected();
            }

            // Load and apply Legion button remap settings from LocalSettings
            LoadLegionButtonRemapSettings();

            // Load and apply Legion scroll wheel remap settings from LocalSettings
            LoadLegionScrollRemapSettings();

            // Apply AutoTDP settings from current profile after widget sync
            // This ensures profile values override any stale LocalSettings sent by widget during initial connection
            if (profileManager?.CurrentProfile != null)
            {
                Logger.Info($"Applying AutoTDP settings from profile on startup: {profileManager.CurrentProfile.GameId.Name}");
                ApplyAutoTDPSettingsFromProfile();
            }

            Logger.Info($"[TIMING] Helper fully initialized and ready");

            // Log version number for easier debugging and set helperVersion for heartbeat
            try
            {
                var packageVersion = Package.Current.Id.Version;
                helperVersion = $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";
                Logger.Info($"GoTweaks Helper v{helperVersion}");
            }
            catch (Exception ex)
            {
                // When running elevated (no package identity), try to get version from deployed .version file
                try
                {
                    var deployedVersion = Services.HelperDeploymentService.GetDeployedVersion();
                    if (!string.IsNullOrEmpty(deployedVersion))
                    {
                        helperVersion = deployedVersion;
                        Logger.Info($"GoTweaks Helper v{helperVersion} (from deployed .version file)");
                    }
                    else
                    {
                        Logger.Debug($"Could not get package version: {ex.Message}");
                    }
                }
                catch
                {
                    Logger.Debug($"Could not get deployed version: {ex.Message}");
                }
            }

            // Main loop - helper runs until cancelled (service stop) or shutdown
            while (!_isShuttingDown)
            {
                // Check for service cancellation
                if (_isRunningAsService && _serviceCancellationToken.IsCancellationRequested)
                {
                    Logger.Info("Service cancellation requested, exiting main loop");
                    break;
                }

                // Check for shutdown (from ExitHelper request)
                if (_isShuttingDown)
                {
                    Logger.Info("Shutdown flag set, exiting main loop for version update");
                    break;
                }

                // Only try to reconnect AppServiceConnection if it's available (not running elevated)
                // When running elevated via scheduled task, we use Named Pipes only
                if (connection != null && appServiceConnectionStatus != AppServiceConnectionStatus.Success)
                {
                    Logger.Info("Try to reconnect to the widget via AppService.");
                    await ConnectToWidget(false);

                    // Force-sync RunningGame to widget after reconnection
                    // This ensures the widget receives the current game even if it hasn't changed
                    if (appServiceConnectionStatus == AppServiceConnectionStatus.Success && systemManager?.RunningGame != null)
                    {
                        Logger.Info("Widget reconnected - force-syncing RunningGame to widget");
                        systemManager.RunningGame.ForceSetValue(systemManager.RunningGame.Value);
                    }
                }

                await Task.Delay(1000);

                // Write heartbeat file so widget can detect if helper is running
                WriteHeartbeat();

                // Check if MSIX package has been uninstalled - if so, clean up and exit
                CheckForPackageUninstall();

                foreach (var manager in Managers)
                {
                    manager.Update();
                }
            }

            // Clean up heartbeat file before exiting
            DeleteHeartbeatFile();

            Logger.Info("Main loop exited");
        }

        private static void CPUState_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUState_PropertyChanged - already applying profile");
                return;
            }

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU State to Max={powerManager.MaxCPUState.Value}%, Min={powerManager.MinCPUState.Value}%.");
            profileManager.CurrentProfile.MaxCPUState = powerManager.MaxCPUState.Value;
            profileManager.CurrentProfile.MinCPUState = powerManager.MinCPUState.Value;
        }

        private static void SystemManager_ResumeFromSleep(object sender)
        {
            Logger.Info("System resumed from sleep/hibernation, refreshing hardware sensors and re-applying profile.");

            // Force refresh hardware sensors (battery values can be stale after hibernation)
            performanceManager?.ForceRefreshHardware();

            // Re-apply current profile settings (TDP, CPU boost, EPP, CPU state)
            CurrentProfile_PropertyChanged(sender, null);
        }

        // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
        //private static void GPUClock_PropertyChanged(object sender, PropertyChangedEventArgs e)
        //{
        //    // GPU Clock is saved per-profile
        //    // Note: Profiles would need GPUClockMin/Max properties added to support per-game GPU clocks
        //    Logger.Info($"GPU Clock settings changed: Enabled={powerManager.LimitGPUClock}, Min={powerManager.GPUClockMin}, Max={powerManager.GPUClockMax}");
        //}

        private static void CPUBoost_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUBoost_PropertyChanged - already applying profile");
                return;
            }

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU Boost from {profileManager.CurrentProfile.CPUBoost} to {powerManager.CPUBoost}.");
            profileManager.CurrentProfile.CPUBoost = powerManager.CPUBoost;
        }

        private static void CPUEPP_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUEPP_PropertyChanged - already applying profile");
                return;
            }

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU EPP from {profileManager.CurrentProfile.CPUEPP} to {powerManager.CPUEPP}.");
            profileManager.CurrentProfile.CPUEPP = powerManager.CPUEPP;
        }

        private static void LegionControllerSetting_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug("Skipping LegionControllerSetting_PropertyChanged - already applying profile");
                return;
            }

            var profileName = profileManager.CurrentProfile.GameId.Name;

            // Save the controller setting to the current profile (global or per-game)
            // Button mappings
            if (sender == legionManager?.LegionButtonY1)
            {
                Logger.Info($"Saving LegionButtonY1 to profile {profileName}");
                profileManager.CurrentProfile.LegionButtonY1 = legionManager.LegionButtonY1.Value;
            }
            else if (sender == legionManager?.LegionButtonY2)
            {
                Logger.Info($"Saving LegionButtonY2 to profile {profileName}");
                profileManager.CurrentProfile.LegionButtonY2 = legionManager.LegionButtonY2.Value;
            }
            else if (sender == legionManager?.LegionButtonY3)
            {
                Logger.Info($"Saving LegionButtonY3 to profile {profileName}");
                profileManager.CurrentProfile.LegionButtonY3 = legionManager.LegionButtonY3.Value;
            }
            else if (sender == legionManager?.LegionButtonM1)
            {
                Logger.Info($"Saving LegionButtonM1 to profile {profileName}");
                profileManager.CurrentProfile.LegionButtonM1 = legionManager.LegionButtonM1.Value;
            }
            else if (sender == legionManager?.LegionButtonM2)
            {
                Logger.Info($"Saving LegionButtonM2 to profile {profileName}");
                profileManager.CurrentProfile.LegionButtonM2 = legionManager.LegionButtonM2.Value;
            }
            else if (sender == legionManager?.LegionButtonM3)
            {
                Logger.Info($"Saving LegionButtonM3 to profile {profileName}");
                profileManager.CurrentProfile.LegionButtonM3 = legionManager.LegionButtonM3.Value;
            }
            else if (sender == legionManager?.LegionButtonDesktop)
            {
                Logger.Info($"Saving LegionButtonDesktop to profile {profileName}");
                profileManager.CurrentProfile.LegionButtonDesktop = legionManager.LegionButtonDesktop.Value;
            }
            else if (sender == legionManager?.LegionButtonPage)
            {
                Logger.Info($"Saving LegionButtonPage to profile {profileName}");
                profileManager.CurrentProfile.LegionButtonPage = legionManager.LegionButtonPage.Value;
            }
            // Gyro settings
            else if (sender == legionManager?.LegionGyroActivationButton)
            {
                Logger.Info($"Saving LegionGyroButton to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroButton = legionManager.LegionGyroActivationButton.Value;
            }
            else if (sender == legionManager?.LegionGyroTarget)
            {
                Logger.Info($"Saving LegionGyroTarget to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroTarget = legionManager.LegionGyroTarget.Value;
            }
            else if (sender == legionManager?.LegionGyroSensitivityX)
            {
                Logger.Info($"Saving LegionGyroSensitivityX to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroSensitivityX = legionManager.LegionGyroSensitivityX.Value;
            }
            else if (sender == legionManager?.LegionGyroSensitivityY)
            {
                Logger.Info($"Saving LegionGyroSensitivityY to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroSensitivityY = legionManager.LegionGyroSensitivityY.Value;
            }
            else if (sender == legionManager?.LegionGyroInvertX)
            {
                Logger.Info($"Saving LegionGyroInvertX to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroInvertX = legionManager.LegionGyroInvertX.Value;
            }
            else if (sender == legionManager?.LegionGyroInvertY)
            {
                Logger.Info($"Saving LegionGyroInvertY to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroInvertY = legionManager.LegionGyroInvertY.Value;
            }
            else if (sender == legionManager?.LegionGyroMappingType)
            {
                Logger.Info($"Saving LegionGyroMappingType to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroMappingType = legionManager.LegionGyroMappingType.Value;
            }
            else if (sender == legionManager?.LegionGyroActivationMode)
            {
                Logger.Info($"Saving LegionGyroActivationMode to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroActivationMode = legionManager.LegionGyroActivationMode.Value;
            }
            else if (sender == legionManager?.LegionGyroDeadzone)
            {
                Logger.Info($"Saving LegionGyroDeadzone to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroDeadzone = legionManager.LegionGyroDeadzone.Value;
            }
            // Stick deadzones
            else if (sender == legionManager?.LegionLeftStickDeadzone)
            {
                Logger.Info($"Saving LegionLeftStickDeadzone to profile {profileName}");
                profileManager.CurrentProfile.LegionLeftStickDeadzone = legionManager.LegionLeftStickDeadzone.Value;
            }
            else if (sender == legionManager?.LegionRightStickDeadzone)
            {
                Logger.Info($"Saving LegionRightStickDeadzone to profile {profileName}");
                profileManager.CurrentProfile.LegionRightStickDeadzone = legionManager.LegionRightStickDeadzone.Value;
            }
            // Trigger travel
            else if (sender == legionManager?.LegionLeftTriggerStart)
            {
                Logger.Info($"Saving LegionLeftTriggerStart to profile {profileName}");
                profileManager.CurrentProfile.LegionLeftTriggerStart = legionManager.LegionLeftTriggerStart.Value;
            }
            else if (sender == legionManager?.LegionLeftTriggerEnd)
            {
                Logger.Info($"Saving LegionLeftTriggerEnd to profile {profileName}");
                profileManager.CurrentProfile.LegionLeftTriggerEnd = legionManager.LegionLeftTriggerEnd.Value;
            }
            else if (sender == legionManager?.LegionRightTriggerStart)
            {
                Logger.Info($"Saving LegionRightTriggerStart to profile {profileName}");
                profileManager.CurrentProfile.LegionRightTriggerStart = legionManager.LegionRightTriggerStart.Value;
            }
            else if (sender == legionManager?.LegionRightTriggerEnd)
            {
                Logger.Info($"Saving LegionRightTriggerEnd to profile {profileName}");
                profileManager.CurrentProfile.LegionRightTriggerEnd = legionManager.LegionRightTriggerEnd.Value;
            }
            else if (sender == legionManager?.LegionHairTriggers)
            {
                Logger.Info($"Saving LegionHairTriggers to profile {profileName}");
                profileManager.CurrentProfile.LegionHairTriggers = legionManager.LegionHairTriggers.Value;
            }
            // Joystick as mouse
            else if (sender == legionManager?.LegionJoystickAsMouseMode)
            {
                Logger.Info($"Saving LegionJoystickAsMouseMode to profile {profileName}");
                profileManager.CurrentProfile.LegionJoystickAsMouseMode = legionManager.LegionJoystickAsMouseMode.Value;
            }
            else if (sender == legionManager?.LegionJoystickMouseSens)
            {
                Logger.Info($"Saving LegionJoystickMouseSens to profile {profileName}");
                profileManager.CurrentProfile.LegionJoystickMouseSens = legionManager.LegionJoystickMouseSens.Value;
            }
            // Gamepad mapping
            else if (sender == legionManager?.LegionGamepadMapping)
            {
                Logger.Info($"Saving LegionGamepadMapping to profile {profileName}");
                profileManager.CurrentProfile.LegionGamepadMapping = legionManager.LegionGamepadMapping.Value;
            }
            // Other controller settings
            else if (sender == legionManager?.LegionNintendoLayout)
            {
                Logger.Info($"Saving LegionNintendoLayout to profile {profileName}");
                profileManager.CurrentProfile.LegionNintendoLayout = legionManager.LegionNintendoLayout.Value;
            }
            else if (sender == legionManager?.LegionVibration)
            {
                Logger.Info($"Saving LegionVibration to profile {profileName}");
                profileManager.CurrentProfile.LegionVibration = legionManager.LegionVibration.Value;
            }
            else if (sender == legionManager?.LegionVibrationMode)
            {
                Logger.Info($"Saving LegionVibrationMode to profile {profileName}");
                profileManager.CurrentProfile.LegionVibrationMode = legionManager.LegionVibrationMode.Value;
            }
            else if (sender == legionManager?.LegionControllerProfileEnabled)
            {
                Logger.Info($"Saving LegionControllerProfileEnabled to profile {profileName}");
                profileManager.CurrentProfile.LegionControllerProfileEnabled = legionManager.LegionControllerProfileEnabled.Value;
            }
            // Performance mode (for per-game TDP mode switching)
            else if (sender == legionManager?.LegionPerformanceMode)
            {
                Logger.Info($"Saving LegionPerformanceMode to profile {profileName}");
                profileManager.CurrentProfile.LegionPerformanceMode = legionManager.LegionPerformanceMode.Value;
            }
            // Lighting settings
            else if (sender == legionManager?.LegionLightMode)
            {
                Logger.Info($"Saving LegionLightMode to profile {profileName}");
                profileManager.CurrentProfile.LegionLightMode = legionManager.LegionLightMode.Value;
            }
            else if (sender == legionManager?.LegionLightColor)
            {
                Logger.Info($"Saving LegionLightColor to profile {profileName}");
                profileManager.CurrentProfile.LegionLightColor = legionManager.LegionLightColor.Value;
            }
            else if (sender == legionManager?.LegionLightBrightness)
            {
                Logger.Info($"Saving LegionLightBrightness to profile {profileName}");
                profileManager.CurrentProfile.LegionLightBrightness = legionManager.LegionLightBrightness.Value;
            }
            else if (sender == legionManager?.LegionLightSpeed)
            {
                Logger.Info($"Saving LegionLightSpeed to profile {profileName}");
                profileManager.CurrentProfile.LegionLightSpeed = legionManager.LegionLightSpeed.Value;
            }
            else if (sender == legionManager?.LegionPowerLight)
            {
                Logger.Info($"Saving LegionPowerLight to profile {profileName}");
                profileManager.CurrentProfile.LegionPowerLight = legionManager.LegionPowerLight.Value;
            }
        }

        private static void AutoTDPSetting_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug("Skipping AutoTDPSetting_PropertyChanged - already applying profile");
                return;
            }

            if (profileManager?.CurrentProfile == null || autoTDPManager == null)
                return;

            var profileName = profileManager.CurrentProfile.GameId.Name;

            // Save the AutoTDP setting to the current profile (global or per-game)
            if (sender == autoTDPManager.Enabled)
            {
                Logger.Info($"Saving AutoTDPEnabled to profile {profileName}");
                profileManager.CurrentProfile.AutoTDPEnabled = autoTDPManager.Enabled.Value;
            }
            else if (sender == autoTDPManager.TargetFPS)
            {
                Logger.Info($"Saving AutoTDPTargetFPS to profile {profileName}");
                profileManager.CurrentProfile.AutoTDPTargetFPS = autoTDPManager.TargetFPS.Value;
            }
            else if (sender == autoTDPManager.MinTDP)
            {
                Logger.Info($"Saving AutoTDPMinTDP to profile {profileName}");
                profileManager.CurrentProfile.AutoTDPMinTDP = autoTDPManager.MinTDP.Value;
            }
            else if (sender == autoTDPManager.MaxTDP)
            {
                Logger.Info($"Saving AutoTDPMaxTDP to profile {profileName}");
                profileManager.CurrentProfile.AutoTDPMaxTDP = autoTDPManager.MaxTDP.Value;
            }
        }

        private static void ApplyLegionControllerSettingsFromProfile()
        {
            var profile = profileManager.CurrentProfile;
            var profileName = profile.GameId.Name;

            Logger.Info($"Applying Legion controller settings from profile: {profileName}");

            // Button mappings - skip default/empty mappings to avoid clearing existing button mappings
            // A mapping like {"Type":0,"GamepadAction":0,...} represents "no mapping" and would clear the button
            Logger.Info($"Button Y1 value: '{profile.LegionButtonY1}', IsDefault: {ButtonMappingParser.IsDefaultMapping(profile.LegionButtonY1)}");
            if (!ButtonMappingParser.IsDefaultMapping(profile.LegionButtonY1))
            {
                Logger.Debug($"Applying LegionButtonY1: {profile.LegionButtonY1}");
                legionManager.LegionButtonY1.SetValue(profile.LegionButtonY1);
            }
            if (!ButtonMappingParser.IsDefaultMapping(profile.LegionButtonY2))
            {
                Logger.Debug($"Applying LegionButtonY2: {profile.LegionButtonY2}");
                legionManager.LegionButtonY2.SetValue(profile.LegionButtonY2);
            }
            if (!ButtonMappingParser.IsDefaultMapping(profile.LegionButtonY3))
            {
                Logger.Debug($"Applying LegionButtonY3: {profile.LegionButtonY3}");
                legionManager.LegionButtonY3.SetValue(profile.LegionButtonY3);
            }
            if (!ButtonMappingParser.IsDefaultMapping(profile.LegionButtonM1))
            {
                Logger.Debug($"Applying LegionButtonM1: {profile.LegionButtonM1}");
                legionManager.LegionButtonM1.SetValue(profile.LegionButtonM1);
            }
            if (!ButtonMappingParser.IsDefaultMapping(profile.LegionButtonM2))
            {
                Logger.Debug($"Applying LegionButtonM2: {profile.LegionButtonM2}");
                legionManager.LegionButtonM2.SetValue(profile.LegionButtonM2);
            }
            if (!ButtonMappingParser.IsDefaultMapping(profile.LegionButtonM3))
            {
                Logger.Debug($"Applying LegionButtonM3: {profile.LegionButtonM3}");
                legionManager.LegionButtonM3.SetValue(profile.LegionButtonM3);
            }
            if (!ButtonMappingParser.IsDefaultMapping(profile.LegionButtonDesktop))
            {
                Logger.Debug($"Applying LegionButtonDesktop: {profile.LegionButtonDesktop}");
                legionManager.LegionButtonDesktop.SetValue(profile.LegionButtonDesktop);
            }
            if (!ButtonMappingParser.IsDefaultMapping(profile.LegionButtonPage))
            {
                Logger.Debug($"Applying LegionButtonPage: {profile.LegionButtonPage}");
                legionManager.LegionButtonPage.SetValue(profile.LegionButtonPage);
            }

            // Gyro settings
            if (profile.LegionGyroButton.HasValue)
            {
                Logger.Debug($"Applying LegionGyroButton: {profile.LegionGyroButton.Value}");
                legionManager.LegionGyroActivationButton.SetValue(profile.LegionGyroButton.Value);
            }
            if (profile.LegionGyroTarget.HasValue)
            {
                Logger.Debug($"Applying LegionGyroTarget: {profile.LegionGyroTarget.Value}");
                legionManager.LegionGyroTarget.SetValue(profile.LegionGyroTarget.Value);
            }
            if (profile.LegionGyroSensitivityX.HasValue)
            {
                Logger.Debug($"Applying LegionGyroSensitivityX: {profile.LegionGyroSensitivityX.Value}");
                legionManager.LegionGyroSensitivityX.SetValue(profile.LegionGyroSensitivityX.Value);
            }
            if (profile.LegionGyroSensitivityY.HasValue)
            {
                Logger.Debug($"Applying LegionGyroSensitivityY: {profile.LegionGyroSensitivityY.Value}");
                legionManager.LegionGyroSensitivityY.SetValue(profile.LegionGyroSensitivityY.Value);
            }
            if (profile.LegionGyroInvertX.HasValue)
            {
                Logger.Debug($"Applying LegionGyroInvertX: {profile.LegionGyroInvertX.Value}");
                legionManager.LegionGyroInvertX.SetValue(profile.LegionGyroInvertX.Value);
            }
            if (profile.LegionGyroInvertY.HasValue)
            {
                Logger.Debug($"Applying LegionGyroInvertY: {profile.LegionGyroInvertY.Value}");
                legionManager.LegionGyroInvertY.SetValue(profile.LegionGyroInvertY.Value);
            }
            if (profile.LegionGyroMappingType.HasValue)
            {
                Logger.Debug($"Applying LegionGyroMappingType: {profile.LegionGyroMappingType.Value}");
                legionManager.LegionGyroMappingType.SetValue(profile.LegionGyroMappingType.Value);
            }
            if (profile.LegionGyroActivationMode.HasValue)
            {
                Logger.Debug($"Applying LegionGyroActivationMode: {profile.LegionGyroActivationMode.Value}");
                legionManager.LegionGyroActivationMode.SetValue(profile.LegionGyroActivationMode.Value);
            }
            if (profile.LegionGyroDeadzone.HasValue)
            {
                Logger.Debug($"Applying LegionGyroDeadzone: {profile.LegionGyroDeadzone.Value}");
                legionManager.LegionGyroDeadzone.SetValue(profile.LegionGyroDeadzone.Value);
            }

            // Stick deadzones
            if (profile.LegionLeftStickDeadzone.HasValue)
            {
                Logger.Debug($"Applying LegionLeftStickDeadzone: {profile.LegionLeftStickDeadzone.Value}");
                legionManager.LegionLeftStickDeadzone.SetValue(profile.LegionLeftStickDeadzone.Value);
            }
            if (profile.LegionRightStickDeadzone.HasValue)
            {
                Logger.Debug($"Applying LegionRightStickDeadzone: {profile.LegionRightStickDeadzone.Value}");
                legionManager.LegionRightStickDeadzone.SetValue(profile.LegionRightStickDeadzone.Value);
            }

            // Trigger travel
            if (profile.LegionLeftTriggerStart.HasValue)
            {
                Logger.Debug($"Applying LegionLeftTriggerStart: {profile.LegionLeftTriggerStart.Value}");
                legionManager.LegionLeftTriggerStart.SetValue(profile.LegionLeftTriggerStart.Value);
            }
            if (profile.LegionLeftTriggerEnd.HasValue)
            {
                Logger.Debug($"Applying LegionLeftTriggerEnd: {profile.LegionLeftTriggerEnd.Value}");
                legionManager.LegionLeftTriggerEnd.SetValue(profile.LegionLeftTriggerEnd.Value);
            }
            if (profile.LegionRightTriggerStart.HasValue)
            {
                Logger.Debug($"Applying LegionRightTriggerStart: {profile.LegionRightTriggerStart.Value}");
                legionManager.LegionRightTriggerStart.SetValue(profile.LegionRightTriggerStart.Value);
            }
            if (profile.LegionRightTriggerEnd.HasValue)
            {
                Logger.Debug($"Applying LegionRightTriggerEnd: {profile.LegionRightTriggerEnd.Value}");
                legionManager.LegionRightTriggerEnd.SetValue(profile.LegionRightTriggerEnd.Value);
            }
            if (profile.LegionHairTriggers.HasValue)
            {
                Logger.Debug($"Applying LegionHairTriggers: {profile.LegionHairTriggers.Value}");
                legionManager.LegionHairTriggers.SetValue(profile.LegionHairTriggers.Value);
            }

            // Joystick as mouse
            if (profile.LegionJoystickAsMouseMode.HasValue)
            {
                Logger.Debug($"Applying LegionJoystickAsMouseMode: {profile.LegionJoystickAsMouseMode.Value}");
                legionManager.LegionJoystickAsMouseMode.SetValue(profile.LegionJoystickAsMouseMode.Value);
            }
            if (profile.LegionJoystickMouseSens.HasValue)
            {
                Logger.Debug($"Applying LegionJoystickMouseSens: {profile.LegionJoystickMouseSens.Value}");
                legionManager.LegionJoystickMouseSens.SetValue(profile.LegionJoystickMouseSens.Value);
            }

            // Gamepad mapping
            if (!string.IsNullOrEmpty(profile.LegionGamepadMapping))
            {
                Logger.Debug($"Applying LegionGamepadMapping from profile");
                legionManager.LegionGamepadMapping.SetValue(profile.LegionGamepadMapping);
            }

            // Other controller settings
            if (profile.LegionNintendoLayout.HasValue)
            {
                Logger.Debug($"Applying LegionNintendoLayout: {profile.LegionNintendoLayout.Value}");
                legionManager.LegionNintendoLayout.SetValue(profile.LegionNintendoLayout.Value);
            }
            if (profile.LegionVibration.HasValue)
            {
                Logger.Debug($"Applying LegionVibration: {profile.LegionVibration.Value}");
                legionManager.LegionVibration.SetValue(profile.LegionVibration.Value);
            }
            if (profile.LegionVibrationMode.HasValue)
            {
                Logger.Debug($"Applying LegionVibrationMode: {profile.LegionVibrationMode.Value}");
                legionManager.LegionVibrationMode.SetValue(profile.LegionVibrationMode.Value);
            }

            // Lighting settings
            if (profile.LegionLightMode.HasValue)
            {
                Logger.Debug($"Applying LegionLightMode: {profile.LegionLightMode.Value}");
                legionManager.LegionLightMode.SetValue(profile.LegionLightMode.Value);
            }
            if (!string.IsNullOrEmpty(profile.LegionLightColor))
            {
                Logger.Debug($"Applying LegionLightColor: {profile.LegionLightColor}");
                legionManager.LegionLightColor.SetValue(profile.LegionLightColor);
            }
            if (profile.LegionLightBrightness.HasValue)
            {
                Logger.Debug($"Applying LegionLightBrightness: {profile.LegionLightBrightness.Value}");
                legionManager.LegionLightBrightness.SetValue(profile.LegionLightBrightness.Value);
            }
            if (profile.LegionLightSpeed.HasValue)
            {
                Logger.Debug($"Applying LegionLightSpeed: {profile.LegionLightSpeed.Value}");
                legionManager.LegionLightSpeed.SetValue(profile.LegionLightSpeed.Value);
            }
            if (profile.LegionPowerLight.HasValue)
            {
                Logger.Debug($"Applying LegionPowerLight: {profile.LegionPowerLight.Value}");
                legionManager.LegionPowerLight.SetValue(profile.LegionPowerLight.Value);
            }
        }

        private static void ApplyAutoTDPSettingsFromProfile()
        {
            if (profileManager?.CurrentProfile == null || autoTDPManager == null)
                return;

            var profile = profileManager.CurrentProfile;
            var profileName = profile.GameId.Name;

            Logger.Info($"Applying AutoTDP settings from profile: {profileName}");

            // Apply AutoTDP settings from profile
            Logger.Debug($"Applying AutoTDPEnabled: {profile.AutoTDPEnabled}");
            autoTDPManager.Enabled.SetValue(profile.AutoTDPEnabled);

            Logger.Debug($"Applying AutoTDPTargetFPS: {profile.AutoTDPTargetFPS}");
            autoTDPManager.TargetFPS.SetValue(profile.AutoTDPTargetFPS);

            Logger.Debug($"Applying AutoTDPMinTDP: {profile.AutoTDPMinTDP}");
            autoTDPManager.MinTDP.SetValue(profile.AutoTDPMinTDP);

            Logger.Debug($"Applying AutoTDPMaxTDP: {profile.AutoTDPMaxTDP}");
            autoTDPManager.MaxTDP.SetValue(profile.AutoTDPMaxTDP);
        }

        private static void CurrentProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Use lock to ensure atomic profile application and prevent interleaved settings
            // from rapid game switches (Game A → Game B → Game A)
            lock (profileApplicationLock)
            {
                // Prevent reentrant profile handling that can cause race conditions
                if (isApplyingProfile)
                {
                    Logger.Debug("Skipping CurrentProfile_PropertyChanged - already applying profile");
                    return;
                }

                if (profileManager.CurrentProfile.Use || profileManager.CurrentProfile.IsGlobalProfile)
                {
                    try
                    {
                        isApplyingProfile = true;
                        Logger.Info($"Profile changed to {profileManager.CurrentProfile.GameId.Name}, apply it.");

                        // For per-game profiles, apply the saved LegionPerformanceMode if set
                        // This ensures the correct TDP mode is applied when the game is detected
                        if (profileManager.CurrentProfile.Use && legionManager != null)
                        {
                            int? savedMode = profileManager.CurrentProfile.LegionPerformanceMode;
                            if (savedMode.HasValue)
                            {
                                int currentMode = legionManager.LegionPerformanceMode.Value;
                                if (currentMode != savedMode.Value)
                                {
                                    Logger.Info($"Switching to saved performance mode ({savedMode.Value}) for per-game profile (was {currentMode})");
                                    legionManager.LegionPerformanceMode.SetValue(savedMode.Value);
                                }
                            }
                            else
                            {
                                // Profile has no saved LegionPerformanceMode - auto-switch to Custom mode (255)
                                // if not already in Custom mode, so that custom TDP values can be applied
                                int currentMode = legionManager.LegionPerformanceMode.Value;
                                if (currentMode != 255)
                                {
                                    Logger.Info($"Per-game profile has no saved LegionPerformanceMode, auto-switching to Custom mode (was {currentMode}) to enable TDP control");
                                    legionManager.LegionPerformanceMode.SetValue(255);
                                }
                                else
                                {
                                    Logger.Debug($"Per-game profile has no saved LegionPerformanceMode, already in Custom mode");
                                }
                            }
                        }

                        // Use SetProfileValue to ensure profile TDP takes precedence over in-flight widget messages
                        // All settings applied atomically under lock to prevent cross-contamination
                        performanceManager.TDP.SetProfileValue(profileManager.CurrentProfile.TDP);
                        performanceManager.TDPBoostEnabled.SetValue(profileManager.CurrentProfile.TDPBoostEnabled);
                        powerManager.CPUBoost.SetValue(profileManager.CurrentProfile.CPUBoost);
                        powerManager.CPUEPP.SetValue(profileManager.CurrentProfile.CPUEPP);
                        powerManager.MaxCPUState.SetValue(profileManager.CurrentProfile.MaxCPUState);
                        powerManager.MinCPUState.SetValue(profileManager.CurrentProfile.MinCPUState);
                        profileManager.PerGameProfile.SetValue(profileManager.CurrentProfile.Use);

                        // Apply Legion controller settings from profile (both global and per-game)
                        if (legionManager != null)
                        {
                            ApplyLegionControllerSettingsFromProfile();
                        }

                        // Apply AutoTDP settings from profile
                        ApplyAutoTDPSettingsFromProfile();
                    }
                    finally
                    {
                        isApplyingProfile = false;
                    }
                }
                else
                {
                    Logger.Info($"Profile changed to {profileManager.CurrentProfile.GameId.Name} is not used.");
                }
            }
        }

        private static void PerGameProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Prevent reentrant profile handling
            if (isApplyingProfile)
            {
                Logger.Debug("Skipping PerGameProfile_PropertyChanged - already applying profile");
                return;
            }

            try
            {
                isApplyingProfile = true;
                GameProfile gameProfile;
                if (profileManager.PerGameProfile)
                {
                    if (!profileManager.TryGetProfile(systemManager.RunningGame.Value.GameId, out gameProfile))
                    {
                        gameProfile = profileManager.AddNewProfile(systemManager.RunningGame.Value.GameId);
                    }
                    Logger.Info($"Enable per-game profile for {systemManager.RunningGame.Value.GameId}");
                    gameProfile.Use = true;

                    // Disable DefaultGameProfile when per-game profile is enabled
                    if (defaultGameProfileManager != null && defaultGameProfileManager.ProfileEnabled.Value)
                    {
                        Logger.Info("Disabling DefaultGameProfile since per-game profile is now enabled");
                        defaultGameProfileManager.ProfileEnabled.SetValue(false);
                    }

                    // Auto-switch to Custom TDP mode (255) when enabling per-game profile
                    // This allows the user to customize TDP for this game
                    if (legionManager != null && legionManager.LegionPerformanceMode.Value != 255)
                    {
                        Logger.Info("Switching to Custom TDP mode for per-game profile editing");
                        legionManager.LegionPerformanceMode.SetValue(255);
                    }
                }
                else
                {
                    if (profileManager.TryGetProfile(systemManager.RunningGame.Value.GameId, out gameProfile))
                    {
                        gameProfile.Use = false;
                    }
                    gameProfile = profileManager.GlobalProfile;
                }
                profileManager.CurrentProfile.SetValue(gameProfile);
            }
            finally
            {
                isApplyingProfile = false;
            }
        }

        private static void TDP_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            // (e.g., writing game profile TDP to global profile during switch)
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping TDP_PropertyChanged - already applying profile (TDP={performanceManager.TDP})");
                return;
            }

            // Skip when default game profile is active - don't overwrite user's saved profile
            if (defaultGameProfileManager != null && defaultGameProfileManager.ProfileEnabled.Value)
            {
                Logger.Debug($"Skipping TDP_PropertyChanged - Default Game Profile is active (TDP={performanceManager.TDP})");
                return;
            }

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s TDP from {profileManager.CurrentProfile.TDP} to {performanceManager.TDP}.");
            profileManager.CurrentProfile.TDP = performanceManager.TDP;
        }

        private static void TDPBoostEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping TDPBoostEnabled_PropertyChanged - already applying profile");
                return;
            }

            // Skip when default game profile is active - don't overwrite user's saved profile
            if (defaultGameProfileManager != null && defaultGameProfileManager.ProfileEnabled.Value)
            {
                Logger.Debug($"Skipping TDPBoostEnabled_PropertyChanged - Default Game Profile is active");
                return;
            }

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s TDPBoostEnabled from {profileManager.CurrentProfile.TDPBoostEnabled} to {performanceManager.TDPBoostEnabled.Value}.");
            profileManager.CurrentProfile.TDPBoostEnabled = performanceManager.TDPBoostEnabled.Value;
        }

        private static void RunningGame_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Prevent reentrant profile handling
            if (isApplyingProfile)
            {
                Logger.Debug("Skipping RunningGame_PropertyChanged - already applying profile");
                return;
            }

            try
            {
                isApplyingProfile = true;
                if (systemManager.RunningGame.Value.IsValid())
                {
                    if (profileManager.TryGetProfile(systemManager.RunningGame.Value.GameId, out var runningGameProfile))
                    {
                        if (runningGameProfile.Use)
                        {
                            Logger.Info($"Game {systemManager.RunningGame.GameId} has per-game profile in use.");
                            profileManager.CurrentProfile.SetValue(runningGameProfile);
                        }
                        else
                        {
                            Logger.Info($"Game {systemManager.RunningGame.GameId} has per-game profile but not in use.");
                        }
                    }
                    else
                    {
                        Logger.Info($"Game {systemManager.RunningGame.GameId} doesn't have per-game profile.");
                    }

                    // Apply CPU core affinity to the new game
                    systemManager.ApplyAffinityToRunningGame();

                    // Switch Lossless Scaling profile for the detected game
                    if (losslessScalingManager.LosslessScalingInstalled.Value)
                    {
                        var gameName = systemManager.RunningGame.Value.GameId.Name;
                        var gamePath = systemManager.RunningGame.Value.GameId.Path;
                        losslessScalingManager.SetCurrentGame(gameName, gamePath);
                    }
                }
                else
                {
                    Logger.Info($"Stopped playing game, use global profile instead.");
                    profileManager.CurrentProfile.SetValue(profileManager.GlobalProfile);

                    // Apply global profile settings directly (handler is skipped because isApplyingProfile=true)
                    Logger.Info($"Applying global profile settings: TDP={profileManager.GlobalProfile.TDP}, CPUBoost={profileManager.GlobalProfile.CPUBoost}, EPP={profileManager.GlobalProfile.CPUEPP}");

                    // Restore LegionPerformanceMode from global profile if set
                    if (legionManager != null)
                    {
                        int? savedMode = profileManager.GlobalProfile.LegionPerformanceMode;
                        if (savedMode.HasValue)
                        {
                            int currentMode = legionManager.LegionPerformanceMode.Value;
                            if (currentMode != savedMode.Value)
                            {
                                Logger.Info($"Restoring global profile performance mode ({savedMode.Value}) (was {currentMode})");
                                legionManager.LegionPerformanceMode.SetValue(savedMode.Value);
                            }
                        }
                    }

                    performanceManager.TDP.SetProfileValue(profileManager.GlobalProfile.TDP);
                    performanceManager.TDPBoostEnabled.SetValue(profileManager.GlobalProfile.TDPBoostEnabled);
                    powerManager.CPUBoost.SetValue(profileManager.GlobalProfile.CPUBoost);
                    powerManager.CPUEPP.SetValue(profileManager.GlobalProfile.CPUEPP);
                    powerManager.MaxCPUState.SetValue(profileManager.GlobalProfile.MaxCPUState);
                    powerManager.MinCPUState.SetValue(profileManager.GlobalProfile.MinCPUState);
                    profileManager.PerGameProfile.SetValue(false);

                    // Apply Legion controller settings from global profile
                    if (legionManager != null)
                    {
                        ApplyLegionControllerSettingsFromProfile();
                    }

                    // Apply AutoTDP settings from global profile
                    ApplyAutoTDPSettingsFromProfile();

                    // Reset Lossless Scaling to Default profile when game stops
                    if (losslessScalingManager.LosslessScalingInstalled.Value)
                    {
                        losslessScalingManager.SetCurrentGame("Default", "");
                    }
                }
            }
            finally
            {
                isApplyingProfile = false;
            }
        }

        /// <summary>
        /// Handles the event when the desktop process receives a request from the UWP app
        /// </summary>
        private static async void Connection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            try
            {
                Logger.Info($"Helper received message {args.Request.Message.ToDebugString()} from widget.");

                // Handle power plan change request
                if (args.Request.Message.TryGetValue("PowerPlan", out object powerPlanValue) && powerPlanValue is string guidStr)
                {
                    if (Guid.TryParse(guidStr, out Guid planGuid))
                    {
                        Logger.Info($"Setting power plan to: {planGuid}");
                        Power.PowerManager.SetActivePowerPlan(planGuid);
                    }
                    return;
                }

                // Handle keyboard shortcut request (uses InputInjector for widget compatibility)
                if (args.Request.Message.TryGetValue("SendKeyboardShortcut", out object shortcutValue) && shortcutValue is string shortcutStr)
                {
                    Logger.Info($"Sending keyboard shortcut via InputInjector: {shortcutStr}");
                    SendKeyboardShortcutViaInputInjector(shortcutStr);
                    return;
                }

                // Handle close game request - closes foreground window (not Game Bar)
                if (args.Request.Message.TryGetValue("CloseGame", out object _))
                {
                    Logger.Info("CloseGame request received - attempting to close foreground window");
                    bool success = Windows.User32.CloseForegroundWindow();
                    Logger.Info($"CloseGame result: {success}");
                    return;
                }

                // Handle launch process request (for TabTip.exe touch keyboard etc.)
                if (args.Request.Message.TryGetValue("LaunchProcess", out object processValue) && processValue is string processPath)
                {
                    try
                    {
                        Logger.Info($"LaunchProcess request received: {processPath}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = processPath,
                            UseShellExecute = true
                        });
                        Logger.Info($"Process launched: {processPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to launch process {processPath}: {ex.Message}");
                    }
                    return;
                }

                // Handle hibernate request
                if (args.Request.Message.TryGetValue("Hibernate", out object _hibernate))
                {
                    try
                    {
                        Logger.Info("Hibernate request received - putting system to sleep");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            Arguments = "/h",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        Logger.Info("Hibernate command executed");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to hibernate: {ex.Message}");
                    }
                    return;
                }

                // Handle launch URL request (for donate button etc. - Game Bar blocks direct URL launching)
                if (args.Request.Message.TryGetValue("LaunchUrl", out object urlValue) && urlValue is string url)
                {
                    try
                    {
                        Logger.Info($"LaunchUrl request received: {url}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                        Logger.Info($"URL launched: {url}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to launch URL {url}: {ex.Message}");
                    }
                    return;
                }

                // Handle Energy Saver toggle request
                if (args.Request.Message.TryGetValue("ToggleEnergySaver", out object _toggleEs))
                {
                    bool success = Power.PowerManager.ToggleEnergySaver();
                    bool newState = Power.PowerManager.GetEnergySaverEnabled();
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("EnergySaverEnabled", newState);
                    response.Add("Success", success);
                    await args.Request.SendResponseAsync(response);
                    return;
                }

                // Handle Get Energy Saver status request
                if (args.Request.Message.TryGetValue("GetEnergySaver", out object _getEs))
                {
                    bool enabled = Power.PowerManager.GetEnergySaverEnabled();
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("EnergySaverEnabled", enabled);
                    await args.Request.SendResponseAsync(response);
                    return;
                }

                // Handle get power plans request
                if (args.Request.Message.TryGetValue("GetPowerPlans", out object _))
                {
                    var plans = Power.PowerManager.GetPowerPlans();
                    var activePlan = Power.PowerManager.GetActivePowerPlan();

                    // Build response: "GUID1|Name1;GUID2|Name2;..." and active plan GUID
                    var planStrings = new System.Collections.Generic.List<string>();
                    foreach (var plan in plans)
                    {
                        planStrings.Add($"{plan.Guid}|{plan.Name}");
                    }

                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("PowerPlans", string.Join(";", planStrings));
                    response.Add("ActivePowerPlan", activePlan.ToString());

                    await args.Request.SendResponseAsync(response);
                    Logger.Info($"Sent {plans.Count} power plans to widget");
                    return;
                }

                // Handle refresh display settings request (called when game closes to update resolution tile)
                if (args.Request.Message.TryGetValue("RefreshDisplaySettings", out object _))
                {
                    Logger.Info("RefreshDisplaySettings request received - refreshing display settings");
                    // Use a small delay to allow Windows to fully update display configuration
                    await System.Threading.Tasks.Task.Delay(500);
                    systemManager?.RefreshDisplaySettings();
                    return;
                }

                // Handle exit helper request
                if (args.Request.Message.TryGetValue("ExitHelper", out object _exitHelper))
                {
                    Logger.Info("ExitHelper request received - shutting down helper");
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Success", true);
                    await args.Request.SendResponseAsync(response);

                    // Give time for response to be sent, then exit
                    await System.Threading.Tasks.Task.Delay(500);
                    Environment.Exit(0);
                    return;
                }

                // Handle export logs request
                if (args.Request.Message.TryGetValue("ExportLogs", out object _exportLogs))
                {
                    Logger.Info("ExportLogs request received");
                    try
                    {
                        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var exportFolder = Path.Combine(desktopPath, $"GoTweaks_Logs_{timestamp}");

                        // Create export folder
                        Directory.CreateDirectory(exportFolder);
                        var helperFolder = Path.Combine(exportFolder, "Helper");
                        var widgetFolder = Path.Combine(exportFolder, "Widget");
                        Directory.CreateDirectory(helperFolder);
                        Directory.CreateDirectory(widgetFolder);

                        // Get log paths from app package location
                        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var packageFolder = Path.Combine(localAppData, "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg");
                        var helperLogPath = Path.Combine(packageFolder, "LocalCache", "Local");
                        var widgetLogPath = Path.Combine(packageFolder, "LocalState");

                        // Copy helper logs (last 2)
                        if (Directory.Exists(helperLogPath))
                        {
                            var helperLogs = Directory.GetFiles(helperLogPath, "helper_*.log")
                                .OrderByDescending(f => File.GetLastWriteTime(f))
                                .Take(2);

                            foreach (var log in helperLogs)
                            {
                                var destPath = Path.Combine(helperFolder, Path.GetFileName(log));
                                File.Copy(log, destPath, true);
                                Logger.Info($"Copied: {Path.GetFileName(log)}");
                            }
                        }

                        // Copy widget logs (last 2)
                        if (Directory.Exists(widgetLogPath))
                        {
                            var widgetLogs = Directory.GetFiles(widgetLogPath, "widget_*.log")
                                .OrderByDescending(f => File.GetLastWriteTime(f))
                                .Take(2);

                            foreach (var log in widgetLogs)
                            {
                                var destPath = Path.Combine(widgetFolder, Path.GetFileName(log));
                                File.Copy(log, destPath, true);
                                Logger.Info($"Copied: {Path.GetFileName(log)}");
                            }
                        }

                        Logger.Info($"Logs exported to: {exportFolder}");
                        var response = new global::Windows.Foundation.Collections.ValueSet();
                        response.Add("Success", true);
                        response.Add("Path", exportFolder);
                        await args.Request.SendResponseAsync(response);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to export logs: {ex.Message}");
                        var response = new global::Windows.Foundation.Collections.ValueSet();
                        response.Add("Success", false);
                        response.Add("Error", ex.Message);
                        await args.Request.SendResponseAsync(response);
                    }
                    return;
                }

                // Handle check for local update (debug) request
                if (args.Request.Message.TryGetValue("CheckLocalUpdate", out object _))
                {
                    Logger.Info("CheckLocalUpdate request received");
                    var response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        const string appPackagesPath = @"C:\Users\diego\OneDrive\Desktop\Diego\projects\XboxGamingBar\Samples\XboxGamingBarPackage\AppPackages";

                        if (!Directory.Exists(appPackagesPath))
                        {
                            response.Add("Error", $"AppPackages folder not found:\n{appPackagesPath}");
                            await args.Request.SendResponseAsync(response);
                            return;
                        }

                        // Get all package folders and find the latest version
                        var packageFolders = Directory.GetDirectories(appPackagesPath)
                            .Where(d => Path.GetFileName(d).StartsWith("XboxGamingBarPackage_"))
                            .ToList();

                        if (packageFolders.Count == 0)
                        {
                            response.Add("Error", "No package folders found in AppPackages");
                            await args.Request.SendResponseAsync(response);
                            return;
                        }

                        // Parse versions from folder names (e.g., XboxGamingBarPackage_0.3.98.0_Debug_Test)
                        string latestFolder = null;
                        string latestVersionStr = null;
                        Version latestVersion = null;

                        foreach (var folder in packageFolders)
                        {
                            var folderName = Path.GetFileName(folder);
                            var parts = folderName.Split('_');
                            if (parts.Length >= 2)
                            {
                                var versionStr = parts[1];
                                if (Version.TryParse(versionStr, out var version))
                                {
                                    if (latestVersion == null || version > latestVersion)
                                    {
                                        latestVersion = version;
                                        latestVersionStr = versionStr;
                                        latestFolder = folder;
                                    }
                                }
                            }
                        }

                        if (latestFolder == null)
                        {
                            response.Add("Error", "Could not parse version from folder names");
                            await args.Request.SendResponseAsync(response);
                            return;
                        }

                        // Find .msixbundle in the folder
                        var msixbundleFiles = Directory.GetFiles(latestFolder, "*.msixbundle", SearchOption.AllDirectories);
                        if (msixbundleFiles.Length == 0)
                        {
                            response.Add("Error", $"No .msixbundle found in:\n{Path.GetFileName(latestFolder)}");
                            await args.Request.SendResponseAsync(response);
                            return;
                        }

                        var msixbundlePath = msixbundleFiles[0];
                        Logger.Info($"Found local update: version={latestVersionStr}, path={msixbundlePath}");

                        response.Add("LatestVersion", latestVersionStr);
                        response.Add("MsixbundlePath", msixbundlePath);
                        response.Add("FolderName", Path.GetFileName(latestFolder));
                        await args.Request.SendResponseAsync(response);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to check for local update: {ex.Message}");
                        response.Add("Error", $"Failed: {ex.Message}");
                        await args.Request.SendResponseAsync(response);
                    }
                    return;
                }

                // Handle download and install update request
                if (args.Request.Message.TryGetValue("DownloadAndInstallUpdate", out object zipUrlObj))
                {
                    var zipUrl = zipUrlObj?.ToString();
                    Logger.Info($"DownloadAndInstallUpdate request received: {zipUrl}");

                    var response = new global::Windows.Foundation.Collections.ValueSet();

                    if (string.IsNullOrEmpty(zipUrl))
                    {
                        response.Add("UpdateStatus", "Error: No URL provided");
                        await args.Request.SendResponseAsync(response);
                        return;
                    }

                    try
                    {
                        string msixbundlePath;

                        // Check if this is a local msixbundle path (debug mode)
                        if (zipUrl.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) && File.Exists(zipUrl))
                        {
                            // Direct path to local msixbundle - skip download/extract
                            Logger.Info($"[DEBUG] Using local msixbundle: {zipUrl}");
                            msixbundlePath = zipUrl;
                        }
                        else
                        {
                            // Download and extract from URL
                            var tempFolder = Path.Combine(Path.GetTempPath(), "GoTweaks_Update");
                            var zipPath = Path.Combine(tempFolder, "update.zip");

                            // Clean up and create temp folder
                            if (Directory.Exists(tempFolder))
                                Directory.Delete(tempFolder, true);
                            Directory.CreateDirectory(tempFolder);

                            // Download the zip file
                            Logger.Info($"Downloading update from {zipUrl}...");
                            using (var client = new WebClient())
                            {
                                client.Headers.Add("User-Agent", "GoTweaks/1.0");
                                client.DownloadFile(zipUrl, zipPath);
                            }
                            Logger.Info($"Downloaded to {zipPath}");

                            // Extract the zip
                            var extractFolder = Path.Combine(tempFolder, "extracted");
                            Directory.CreateDirectory(extractFolder);
                            ZipFile.ExtractToDirectory(zipPath, extractFolder);
                            Logger.Info($"Extracted to {extractFolder}");

                            // Find the .msixbundle file
                            msixbundlePath = null;
                            foreach (var file in Directory.GetFiles(extractFolder, "*.msixbundle", SearchOption.AllDirectories))
                            {
                                msixbundlePath = file;
                                break;
                            }

                            if (string.IsNullOrEmpty(msixbundlePath))
                            {
                                Logger.Error("No .msixbundle file found in the update package");
                                response.Add("UpdateStatus", "Error: No .msixbundle found in update");
                                await args.Request.SendResponseAsync(response);
                                return;
                            }
                        }

                        Logger.Info($"Found msixbundle: {msixbundlePath}");

                        // Launch the msixbundle installer
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = msixbundlePath,
                            UseShellExecute = true
                        };

                        Logger.Info("Launching msixbundle installer...");
                        var installerProcess = Process.Start(startInfo);

                        response.Add("UpdateStatus", "Installing");
                        await args.Request.SendResponseAsync(response);

                        // Wait for installer to fully load the package before exiting
                        // The msixbundle installer needs time to open and read the package
                        Logger.Info("Waiting for installer to load package...");
                        await Task.Delay(5000); // 5 seconds for installer to fully open

                        // Exit helper so installer can replace files
                        Logger.Info("Exiting helper for update installation...");
                        Environment.Exit(0);
                    }
                    catch (WebException ex)
                    {
                        Logger.Error($"Failed to download update: {ex.Message}");
                        response.Add("UpdateStatus", $"Error: Download failed - {ex.Message}");
                        await args.Request.SendResponseAsync(response);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to install update: {ex.Message}");
                        response.Add("UpdateStatus", $"Error: {ex.Message}");
                        await args.Request.SendResponseAsync(response);
                    }
                    return;
                }

                // Handle Labs section requests
                if (args.Request.Message.TryGetValue("Function", out object functionObj))
                {
                    int functionValue = Convert.ToInt32(functionObj);

                    // Labs: DAService Status request
                    if (functionValue == (int)Function.Labs_DAServiceStatus)
                    {
                        int status = GetDAServiceStatus();
                        var response = new global::Windows.Foundation.Collections.ValueSet();
                        response.Add("Function", (int)Function.Labs_DAServiceStatus);
                        response.Add("Value", status);
                        await args.Request.SendResponseAsync(response);
                        Logger.Info($"Labs: DAService status = {status} (0=Stopped, 1=Running, 2=NotFound)");
                        return;
                    }

                    // Labs: DAService Control request (Start/Stop)
                    if (functionValue == (int)Function.Labs_DAServiceControl)
                    {
                        if (args.Request.Message.TryGetValue("Value", out object actionObj))
                        {
                            int action = Convert.ToInt32(actionObj);
                            ControlDAService(action);

                            // Send back updated status after a short delay
                            await Task.Delay(500);
                            int status = GetDAServiceStatus();
                            var response = new global::Windows.Foundation.Collections.ValueSet();
                            response.Add("Function", (int)Function.Labs_DAServiceStatus);
                            response.Add("Value", status);
                            await args.Request.SendResponseAsync(response);
                            Logger.Info($"Labs: DAService control action={action}, new status={status}");
                        }
                        return;
                    }

                    // Labs: Legion Button Remap (L/R to Xbox Guide or Keyboard Shortcut)
                    if (functionValue == (int)Function.Labs_LegionButtonRemap)
                    {
                        string button = "L";      // "L" or "R"
                        bool enabled = false;
                        int actionType = 0;       // 0=Xbox Guide, 1=Keyboard Shortcut
                        string shortcut = "";

                        if (args.Request.Message.TryGetValue("Button", out object buttonObj))
                            button = buttonObj?.ToString() ?? "L";
                        if (args.Request.Message.TryGetValue("Enabled", out object enabledObj))
                            enabled = Convert.ToBoolean(enabledObj);
                        if (args.Request.Message.TryGetValue("Action", out object actionObj))
                            actionType = Convert.ToInt32(actionObj);
                        if (args.Request.Message.TryGetValue("Shortcut", out object shortcutObj))
                            shortcut = shortcutObj?.ToString() ?? "";

                        bool success = ConfigureLegionButtonRemap(button, enabled, actionType, shortcut);

                        var response = new global::Windows.Foundation.Collections.ValueSet();
                        response.Add("Success", success);
                        await args.Request.SendResponseAsync(response);

                        string actionName = actionType == 0 ? "Xbox Guide" : $"Shortcut: {shortcut}";
                        Logger.Info($"Labs: Legion {button} Remap - Enabled: {enabled}, Action: {actionName}, Success: {success}");
                        return;
                    }

                    // Labs: Scroll Wheel Remap (Up/Down/Click)
                    if (functionValue == (int)Function.Labs_LegionScrollRemap)
                    {
                        string direction = "Up";  // "Up", "Down", or "Click"
                        bool enabled = false;
                        int actionType = 0;       // 0=Xbox Guide, 1=Keyboard Shortcut, 2=Run Command, 3=Focus GoTweaks
                        string shortcut = "";

                        if (args.Request.Message.TryGetValue("Direction", out object directionObj))
                            direction = directionObj?.ToString() ?? "Up";
                        if (args.Request.Message.TryGetValue("Enabled", out object enabledObj))
                            enabled = Convert.ToBoolean(enabledObj);
                        if (args.Request.Message.TryGetValue("Action", out object actionObj))
                            actionType = Convert.ToInt32(actionObj);
                        if (args.Request.Message.TryGetValue("Shortcut", out object shortcutObj))
                            shortcut = shortcutObj?.ToString() ?? "";

                        bool success = ConfigureLegionScrollRemap(direction, enabled, actionType, shortcut);

                        var response = new global::Windows.Foundation.Collections.ValueSet();
                        response.Add("Success", success);
                        await args.Request.SendResponseAsync(response);

                        string actionName = actionType == 0 ? "Xbox Guide" : $"Shortcut: {shortcut}";
                        Logger.Info($"Labs: Scroll {direction} Remap - Enabled: {enabled}, Action: {actionName}, Success: {success}");
                        return;
                    }

                    // ViGEmBus: Check installed status
                    if (functionValue == (int)Function.ViGEmBusInstalled)
                    {
                        bool installed = XboxGamingBarHelper.Labs.ViGEmBusHelper.IsInstalled();
                        var response = new global::Windows.Foundation.Collections.ValueSet();
                        response.Add("Function", (int)Function.ViGEmBusInstalled);
                        response.Add("Value", installed);
                        await args.Request.SendResponseAsync(response);
                        Logger.Info($"ViGEmBus installed status: {installed}");
                        return;
                    }

                    // ViGEmBus: Install request - only install when explicitly requested with "install" content
                    if (functionValue == (int)Function.InstallViGEmBus)
                    {
                        // Check if this is a Set command with "install" content
                        bool shouldInstall = false;
                        if (args.Request.Message.TryGetValue("Command", out object vigemCmdObj) && vigemCmdObj is int vigemCmd && vigemCmd == (int)Shared.Enums.Command.Set)
                        {
                            if (args.Request.Message.TryGetValue("Content", out object vigemContentObj) && vigemContentObj?.ToString() == "install")
                            {
                                shouldInstall = true;
                            }
                        }

                        if (!shouldInstall)
                        {
                            Logger.Debug("InstallViGEmBus: Ignoring non-install request (Get or empty content)");
                            return;
                        }

                        Logger.Info("ViGEmBus installation requested from widget");
                        // Run installation asynchronously
                        _ = Task.Run(async () =>
                        {
                            bool success = XboxGamingBarHelper.Labs.ViGEmBusHelper.Install();
                            // After installation, send updated status to widget
                            bool installed = XboxGamingBarHelper.Labs.ViGEmBusHelper.IsInstalled();
                            if (connection != null)
                            {
                                var message = new global::Windows.Foundation.Collections.ValueSet();
                                message.Add("Command", (int)Shared.Enums.Command.Set);
                                message.Add("Function", (int)Function.ViGEmBusInstalled);
                                message.Add("Content", installed);
                                message.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                                await connection.SendMessageAsync(message);
                                Logger.Info($"ViGEmBus installation complete, sent updated status: {installed}");
                            }
                        });
                        return;
                    }

                    // Debug: Export Default Game Profiles to Desktop
                    if (functionValue == (int)Function.Debug_ExportDGPs)
                    {
                        var response = new global::Windows.Foundation.Collections.ValueSet();
                        try
                        {
                            string exportPath = ExportDefaultGameProfiles();
                            response.Add("ExportPath", exportPath);
                            Logger.Info($"Debug: DGPs exported to {exportPath}");
                        }
                        catch (Exception ex)
                        {
                            response.Add("Error", ex.Message);
                            Logger.Error($"Debug: Failed to export DGPs: {ex.Message}");
                        }
                        await args.Request.SendResponseAsync(response);
                        return;
                    }
                }

                // Handle BatchGet command for fast property sync
                if (args.Request.Message.TryGetValue("Command", out object cmdObj) &&
                    cmdObj is int cmdInt && cmdInt == (int)Shared.Enums.Command.BatchGet)
                {
                    await HandleBatchGetRequest(args.Request);
                    return;
                }

                await properties.OnRequestReceived(args.Request);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling request: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Handles messages received from the widget via Named Pipe
        /// </summary>
        private static async void PipeServer_MessageReceived(object sender, IPC.PipeMessageEventArgs e)
        {
            try
            {
                var pipeMsg = Shared.IPC.PipeMessage.FromJson(e.Message);
                Logger.Info($"Helper received pipe message: {pipeMsg}");

                // Convert to ValueSet for compatibility with existing handlers
                var valueSet = pipeMsg.ToValueSet();

                // Handle power plan change request
                if (pipeMsg.Extra.TryGetValue("PowerPlan", out object powerPlanValue) && powerPlanValue is string guidStr)
                {
                    if (Guid.TryParse(guidStr, out Guid planGuid))
                    {
                        Logger.Info($"Setting power plan to: {planGuid}");
                        Power.PowerManager.SetActivePowerPlan(planGuid);
                    }
                    return;
                }

                // Handle keyboard shortcut request
                if (pipeMsg.Extra.TryGetValue("SendKeyboardShortcut", out object shortcutValue) && shortcutValue is string shortcutStr)
                {
                    Logger.Info($"Sending keyboard shortcut via InputInjector: {shortcutStr}");
                    SendKeyboardShortcutViaInputInjector(shortcutStr);
                    return;
                }

                // Handle close game request
                if (pipeMsg.Extra.ContainsKey("CloseGame"))
                {
                    Logger.Info("CloseGame request received - attempting to close foreground window");
                    bool success = Windows.User32.CloseForegroundWindow();
                    Logger.Info($"CloseGame result: {success}");
                    return;
                }

                // Handle export logs request
                if (pipeMsg.Extra.ContainsKey("ExportLogs"))
                {
                    Logger.Info("Pipe: ExportLogs request received");
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var exportFolder = Path.Combine(desktopPath, $"GoTweaks_Logs_{timestamp}");

                        // Create export folder
                        Directory.CreateDirectory(exportFolder);
                        var helperFolder = Path.Combine(exportFolder, "Helper");
                        var widgetFolder = Path.Combine(exportFolder, "Widget");
                        Directory.CreateDirectory(helperFolder);
                        Directory.CreateDirectory(widgetFolder);

                        // Get log paths from app package location
                        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var packageFolder = Path.Combine(localAppData, "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg");
                        var helperLogPath = localAppData; // Helper logs are in %LocalAppData% when elevated
                        var widgetLogPath = Path.Combine(packageFolder, "LocalState");

                        // Copy helper logs (last 2) - check both locations
                        var helperLogs = new List<string>();
                        if (Directory.Exists(helperLogPath))
                        {
                            helperLogs.AddRange(Directory.GetFiles(helperLogPath, "helper_*.log"));
                        }
                        var packageHelperLogPath = Path.Combine(packageFolder, "LocalCache", "Local");
                        if (Directory.Exists(packageHelperLogPath))
                        {
                            helperLogs.AddRange(Directory.GetFiles(packageHelperLogPath, "helper_*.log"));
                        }
                        foreach (var log in helperLogs.OrderByDescending(f => File.GetLastWriteTime(f)).Take(2))
                        {
                            var destPath = Path.Combine(helperFolder, Path.GetFileName(log));
                            File.Copy(log, destPath, true);
                            Logger.Info($"Copied: {Path.GetFileName(log)}");
                        }

                        // Copy widget logs (last 2)
                        if (Directory.Exists(widgetLogPath))
                        {
                            var widgetLogs = Directory.GetFiles(widgetLogPath, "widget_*.log")
                                .OrderByDescending(f => File.GetLastWriteTime(f))
                                .Take(2);

                            foreach (var log in widgetLogs)
                            {
                                var destPath = Path.Combine(widgetFolder, Path.GetFileName(log));
                                File.Copy(log, destPath, true);
                                Logger.Info($"Copied: {Path.GetFileName(log)}");
                            }
                        }

                        Logger.Info($"Pipe: Logs exported to: {exportFolder}");
                        response.Add("Success", true);
                        response.Add("Path", exportFolder);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to export logs: {ex.Message}");
                        response.Add("Success", false);
                        response.Add("Error", ex.Message);
                    }

                    // Send response
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                // Handle exit helper request (for version mismatch restart - legacy)
                if (pipeMsg.Extra.ContainsKey("ExitHelper"))
                {
                    Logger.Info("Pipe: ExitHelper request received - shutting down helper for version update");
                    _isShuttingDown = true;

                    // Schedule a forced exit in case the main loop doesn't exit quickly
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000); // Give main loop 3 seconds to exit gracefully
                        if (_isShuttingDown)
                        {
                            Logger.Info("Forcing exit after ExitHelper timeout");
                            Environment.Exit(0);
                        }
                    });
                    return;
                }

                // Handle upgrade helper request (UAC-free upgrade - preferred)
                // The widget sends the MSIX source path so we can copy files after exit
                if (pipeMsg.Extra.ContainsKey("UpgradeHelper"))
                {
                    string msixSourcePath = pipeMsg.Extra["UpgradeHelper"]?.ToString();
                    if (!string.IsNullOrEmpty(msixSourcePath))
                    {
                        Logger.Info($"Pipe: UpgradeHelper request received - source: {msixSourcePath}");

                        // Launch upgrade script that will copy files and restart after we exit
                        LaunchUpgradeScript(msixSourcePath);

                        _isShuttingDown = true;

                        // Schedule a forced exit
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(2000); // Give main loop time to exit gracefully
                            if (_isShuttingDown)
                            {
                                Logger.Info("Forcing exit for upgrade");
                                Environment.Exit(0);
                            }
                        });
                    }
                    else
                    {
                        Logger.Warn("Pipe: UpgradeHelper request missing source path");
                    }
                    return;
                }

                // Handle batch get request (for fast property sync)
                if (pipeMsg.Command == Shared.Enums.Command.BatchGet)
                {
                    await HandleBatchGetRequestViaPipe(pipeMsg);
                    return;
                }

                // Handle property requests via the properties system
                if (pipeMsg.Function != Shared.Enums.Function.None)
                {
                    HandlePipePropertyRequest(pipeMsg);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling pipe message: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Handles property Get/Set requests from the pipe
        /// </summary>
        private static void HandlePipePropertyRequest(Shared.IPC.PipeMessage request)
        {
            try
            {
                global::Windows.Foundation.Collections.ValueSet response = null;

                // Handle special functions that are not in FunctionalProperties
                int functionValue = (int)request.Function;

                // Labs: DAService Status request
                if (functionValue == (int)Function.Labs_DAServiceStatus)
                {
                    int status = GetDAServiceStatus();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", status);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: Labs DAService status = {status}");
                }
                // Labs: DAService Control request (Start/Stop)
                else if (functionValue == (int)Function.Labs_DAServiceControl)
                {
                    if (request.Content != null)
                    {
                        int action = Convert.ToInt32(request.Content);
                        ControlDAService(action);
                        System.Threading.Thread.Sleep(500);
                        int status = GetDAServiceStatus();
                        response = new global::Windows.Foundation.Collections.ValueSet();
                        response.Add(nameof(Function), (int)Function.Labs_DAServiceStatus);
                        response.Add("Content", status);
                        response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                        Logger.Info($"Pipe: Labs DAService control action={action}, new status={status}");
                    }
                }
                // Labs: Legion Button Remap
                else if (functionValue == (int)Function.Labs_LegionButtonRemap)
                {
                    string button = "L";
                    bool enabled = false;
                    int actionType = 0;
                    string shortcut = "";

                    if (request.Extra.TryGetValue("Button", out object buttonObj))
                        button = buttonObj?.ToString() ?? "L";
                    if (request.Extra.TryGetValue("Enabled", out object enabledObj))
                        enabled = Convert.ToBoolean(enabledObj);
                    if (request.Extra.TryGetValue("Action", out object actionObj))
                        actionType = Convert.ToInt32(actionObj);
                    if (request.Extra.TryGetValue("Shortcut", out object shortcutObj))
                        shortcut = shortcutObj?.ToString() ?? "";

                    bool success = ConfigureLegionButtonRemap(button, enabled, actionType, shortcut);
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", success);
                    Logger.Info($"Pipe: Legion {button} Remap - Enabled: {enabled}, Success: {success}");
                }
                // Labs: Scroll Wheel Remap
                else if (functionValue == (int)Function.Labs_LegionScrollRemap)
                {
                    string direction = "Up";
                    bool enabled = false;
                    int actionType = 0;
                    string shortcut = "";

                    if (request.Extra.TryGetValue("Direction", out object directionObj))
                        direction = directionObj?.ToString() ?? "Up";
                    if (request.Extra.TryGetValue("Enabled", out object enabledObj))
                        enabled = Convert.ToBoolean(enabledObj);
                    if (request.Extra.TryGetValue("Action", out object actionObj))
                        actionType = Convert.ToInt32(actionObj);
                    if (request.Extra.TryGetValue("Shortcut", out object shortcutObj))
                        shortcut = shortcutObj?.ToString() ?? "";

                    bool success = ConfigureLegionScrollRemap(direction, enabled, actionType, shortcut);
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", success);
                    Logger.Info($"Pipe: Scroll {direction} Remap - Enabled: {enabled}, Success: {success}");
                }
                // ViGEmBus: Check installed status
                else if (functionValue == (int)Function.ViGEmBusInstalled)
                {
                    bool installed = XboxGamingBarHelper.Labs.ViGEmBusHelper.IsInstalled();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", installed);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: ViGEmBus installed status: {installed}");
                }
                // ViGEmBus: Install request - only install when explicitly requested with Set command and "install" content
                else if (functionValue == (int)Function.InstallViGEmBus)
                {
                    // Check if this is a Set command with "install" content
                    bool shouldInstall = request.Command == Shared.Enums.Command.Set && request.Content == "install";

                    if (!shouldInstall)
                    {
                        Logger.Debug("Pipe: InstallViGEmBus - Ignoring non-install request (Get or empty content)");
                        return;
                    }

                    Logger.Info("Pipe: ViGEmBus installation requested from widget");
                    _ = Task.Run(() =>
                    {
                        bool success = XboxGamingBarHelper.Labs.ViGEmBusHelper.Install();
                        bool installed = XboxGamingBarHelper.Labs.ViGEmBusHelper.IsInstalled();
                        // Send updated status via pipe
                        var updateMsg = new Shared.IPC.PipeMessage
                        {
                            Command = Shared.Enums.Command.Set,
                            Function = Function.ViGEmBusInstalled,
                            Content = installed.ToString()
                        };
                        SendPipeMessage(updateMsg);
                        Logger.Info($"Pipe: ViGEmBus installation complete, sent updated status: {installed}");
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true); // Acknowledge request started
                }
                // Debug: Export Default Game Profiles
                else if (functionValue == (int)Function.Debug_ExportDGPs)
                {
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        string exportPath = ExportDefaultGameProfiles();
                        response.Add("ExportPath", exportPath);
                        Logger.Info($"Pipe: DGPs exported to {exportPath}");
                    }
                    catch (Exception ex)
                    {
                        response.Add("Error", ex.Message);
                        Logger.Error($"Pipe: Failed to export DGPs: {ex.Message}");
                    }
                }
                // Debug: Check for local update (AppPackages)
                else if (functionValue == (int)Function.CheckLocalUpdate)
                {
                    Logger.Info("Pipe: CheckLocalUpdate request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        const string appPackagesPath = @"C:\Users\diego\OneDrive\Desktop\Diego\projects\XboxGamingBar\Samples\XboxGamingBarPackage\AppPackages";

                        if (!Directory.Exists(appPackagesPath))
                        {
                            response.Add("Error", $"AppPackages folder not found:\n{appPackagesPath}");
                        }
                        else
                        {
                            // Get all package folders and find the latest version
                            var packageFolders = Directory.GetDirectories(appPackagesPath)
                                .Where(d => Path.GetFileName(d).StartsWith("XboxGamingBarPackage_"))
                                .ToList();

                            if (packageFolders.Count == 0)
                            {
                                response.Add("Error", "No package folders found in AppPackages");
                            }
                            else
                            {
                                // Parse versions from folder names (e.g., XboxGamingBarPackage_0.3.98.0_Debug_Test)
                                string latestFolder = null;
                                string latestVersionStr = null;
                                Version latestVersion = null;

                                foreach (var folder in packageFolders)
                                {
                                    var folderName = Path.GetFileName(folder);
                                    var parts = folderName.Split('_');
                                    if (parts.Length >= 2)
                                    {
                                        var versionStr = parts[1];
                                        if (Version.TryParse(versionStr, out var version))
                                        {
                                            if (latestVersion == null || version > latestVersion)
                                            {
                                                latestVersion = version;
                                                latestVersionStr = versionStr;
                                                latestFolder = folder;
                                            }
                                        }
                                    }
                                }

                                if (latestFolder == null)
                                {
                                    response.Add("Error", "Could not parse version from folder names");
                                }
                                else
                                {
                                    // Find .msixbundle in the folder
                                    var msixbundleFiles = Directory.GetFiles(latestFolder, "*.msixbundle", SearchOption.AllDirectories);
                                    if (msixbundleFiles.Length == 0)
                                    {
                                        response.Add("Error", $"No .msixbundle found in:\n{Path.GetFileName(latestFolder)}");
                                    }
                                    else
                                    {
                                        var msixbundlePath = msixbundleFiles[0];
                                        Logger.Info($"Pipe: Found local update: version={latestVersionStr}, path={msixbundlePath}");

                                        response.Add("LatestVersion", latestVersionStr);
                                        response.Add("MsixbundlePath", msixbundlePath);
                                        response.Add("FolderName", Path.GetFileName(latestFolder));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to check for local update: {ex.Message}");
                        response.Add("Error", $"Failed: {ex.Message}");
                    }
                }
                // Debug/Development: Install Update (download and install from URL or local path)
                else if (functionValue == (int)Function.InstallUpdate)
                {
                    var updatePath = request.Content;
                    Logger.Info($"Pipe: InstallUpdate request received: {updatePath}");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    if (string.IsNullOrEmpty(updatePath))
                    {
                        response.Add("UpdateStatus", "Error: No URL/path provided");
                    }
                    else
                    {
                        try
                        {
                            string msixbundlePath;

                            // Check if this is a local msixbundle path (debug mode)
                            if (updatePath.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) && File.Exists(updatePath))
                            {
                                // Direct path to local msixbundle - skip download/extract
                                Logger.Info($"Pipe: [DEBUG] Using local msixbundle: {updatePath}");
                                msixbundlePath = updatePath;
                            }
                            else
                            {
                                // Download and extract from URL
                                var tempFolder = Path.Combine(Path.GetTempPath(), "GoTweaks_Update");
                                var zipPath = Path.Combine(tempFolder, "update.zip");

                                // Clean up and create temp folder
                                if (Directory.Exists(tempFolder))
                                    Directory.Delete(tempFolder, true);
                                Directory.CreateDirectory(tempFolder);

                                // Download the zip file
                                Logger.Info($"Pipe: Downloading update from {updatePath}...");
                                using (var client = new WebClient())
                                {
                                    client.Headers.Add("User-Agent", "GoTweaks/1.0");
                                    client.DownloadFile(updatePath, zipPath);
                                }
                                Logger.Info($"Pipe: Downloaded to {zipPath}");

                                // Extract the zip
                                var extractFolder = Path.Combine(tempFolder, "extracted");
                                Directory.CreateDirectory(extractFolder);
                                ZipFile.ExtractToDirectory(zipPath, extractFolder);
                                Logger.Info($"Pipe: Extracted to {extractFolder}");

                                // Find the .msixbundle file
                                msixbundlePath = null;
                                foreach (var file in Directory.GetFiles(extractFolder, "*.msixbundle", SearchOption.AllDirectories))
                                {
                                    msixbundlePath = file;
                                    break;
                                }

                                if (string.IsNullOrEmpty(msixbundlePath))
                                {
                                    Logger.Error("Pipe: No .msixbundle file found in the update package");
                                    response.Add("UpdateStatus", "Error: No .msixbundle found in update");
                                    // Send response and return early
                                    if (pipeServer != null && pipeServer.IsConnected)
                                    {
                                        var errMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                                        errMsg.RequestId = request.RequestId;
                                        pipeServer.SendMessage(errMsg.ToJson());
                                    }
                                    return;
                                }
                            }

                            Logger.Info($"Pipe: Found msixbundle: {msixbundlePath}");

                            // Send response before launching installer
                            response.Add("UpdateStatus", "Installing");
                            if (pipeServer != null && pipeServer.IsConnected)
                            {
                                var successMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                                successMsg.RequestId = request.RequestId;
                                pipeServer.SendMessage(successMsg.ToJson());
                            }

                            // Launch the msixbundle installer
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = msixbundlePath,
                                UseShellExecute = true
                            };

                            Logger.Info("Pipe: Launching msixbundle installer...");
                            var installerProcess = Process.Start(startInfo);

                            // Wait for installer to fully load the package before exiting
                            Logger.Info("Pipe: Waiting for installer to load package...");
                            System.Threading.Thread.Sleep(5000); // 5 seconds for installer to fully open

                            // Exit helper so installer can replace files
                            Logger.Info("Pipe: Exiting helper for update installation...");
                            Environment.Exit(0);
                            return; // Won't reach this but for clarity
                        }
                        catch (WebException ex)
                        {
                            Logger.Error($"Pipe: Failed to download update: {ex.Message}");
                            response.Add("UpdateStatus", $"Error: Download failed - {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Pipe: Failed to install update: {ex.Message}");
                            response.Add("UpdateStatus", $"Error: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // Convert to ValueSet and use the existing property handling
                    var valueSet = request.ToValueSet();
                    response = properties.HandlePipeMessage(valueSet);
                }

                if (response != null && pipeServer != null && pipeServer.IsConnected)
                {
                    // Convert response to JSON and send back
                    // Echo the RequestId so client can correlate the response
                    var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                    responseMsg.RequestId = request.RequestId;
                    pipeServer.SendMessage(responseMsg.ToJson());
                    Logger.Debug($"Sent pipe response for {request.Function} (RequestId={request.RequestId})");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling pipe property request: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the Named Pipe to the widget is connected.
        /// </summary>
        public static bool IsPipeConnected => pipeServer != null && pipeServer.IsConnected;

        /// <summary>
        /// Sends a message to the widget via Named Pipe
        /// </summary>
        public static bool SendPipeMessage(Shared.IPC.PipeMessage message)
        {
            if (pipeServer == null || !pipeServer.IsConnected)
            {
                Logger.Debug("Cannot send pipe message - not connected");
                return false;
            }
            return pipeServer.SendMessage(message.ToJson());
        }

        /// <summary>
        /// Handle batch get request via Named Pipe.
        /// Returns all requested property values in a single response.
        /// </summary>
        private static async Task HandleBatchGetRequestViaPipe(Shared.IPC.PipeMessage request)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!request.Extra.TryGetValue("Functions", out object functionsObj) || !(functionsObj is string functionsJson))
                {
                    Logger.Warn("BatchGet pipe request missing Functions");
                    return;
                }

                var functionIds = System.Text.Json.JsonSerializer.Deserialize<int[]>(functionsJson);
                if (functionIds == null || functionIds.Length == 0)
                {
                    Logger.Warn("BatchGet pipe request has empty Functions array");
                    return;
                }

                // Build batch response with all property values
                var batchData = new Dictionary<string, object>();
                foreach (var funcId in functionIds)
                {
                    var func = (Shared.Enums.Function)funcId;
                    if (properties.TryGetProperty(func, out var property))
                    {
                        try
                        {
                            var value = property.GetValue();

                            // Structs must be serialized to XML to match individual property sync format
                            // Otherwise they serialize as "{}" in JSON which fails XML deserialization on widget
                            // Only serialize custom structs, not built-in value types like DateTime, TimeSpan, etc.
                            if (value != null)
                            {
                                var valueType = value.GetType();
                                if (valueType.IsValueType && !valueType.IsPrimitive && !valueType.IsEnum
                                    && valueType.Namespace != null && valueType.Namespace.StartsWith("Shared"))
                                {
                                    // Special handling for RunningGame/TrackedGame - check if valid before serializing
                                    // These structs have null strings when invalid/empty which causes XML serialization to fail
                                    bool shouldSerialize = true;
                                    if (value is Shared.Data.RunningGame rg)
                                    {
                                        // Must check ProcessId, GameId.Name AND GameId.Path - XML serializer fails on null strings
                                        shouldSerialize = rg.IsValid() && rg.GameId.IsValid() && !string.IsNullOrEmpty(rg.GameId.Path);
                                        if (!shouldSerialize)
                                        {
                                            Logger.Debug($"RunningGame not valid for XML: ProcessId={rg.ProcessId}, Name={rg.GameId.Name ?? "null"}, Path={rg.GameId.Path ?? "null"}");
                                        }
                                    }
                                    else if (value is Shared.Data.TrackedGame tg)
                                    {
                                        shouldSerialize = !string.IsNullOrEmpty(tg.DisplayName);
                                    }

                                    if (shouldSerialize)
                                    {
                                        value = Shared.Utilities.XmlHelper.ToXMLStringRuntime(value, true);
                                    }
                                    else
                                    {
                                        value = ""; // Return empty string for invalid/empty structs
                                    }
                                }
                            }

                            var propData = new Dictionary<string, object>
                            {
                                { "Content", value },
                                { "UpdatedTime", property.UpdatedTime }
                            };
                            batchData[funcId.ToString()] = propData;
                        }
                        catch (Exception propEx)
                        {
                            var innerMsg = propEx.InnerException?.Message ?? "no inner";
                            Logger.Warn($"BatchGet: Failed to serialize property {func}: {propEx.Message} (Inner: {innerMsg})");
                        }
                    }
                }

                // Send response via pipe with the same RequestId
                var response = new Shared.IPC.PipeMessage
                {
                    RequestId = request.RequestId,
                    Command = Shared.Enums.Command.Response,
                    Function = Shared.Enums.Function.None
                };
                response.Extra["BatchData"] = System.Text.Json.JsonSerializer.Serialize(batchData);

                if (pipeServer != null && pipeServer.IsConnected)
                {
                    pipeServer.SendMessage(response.ToJson());
                }

                timer.Stop();
                Logger.Info($"[TIMING] BatchGet via pipe {functionIds.Length} properties: {timer.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Error($"BatchGet via pipe failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle batch get request for fast property sync.
        /// Returns all requested property values in a single response.
        /// </summary>
        private static async Task HandleBatchGetRequest(AppServiceRequest request)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!request.Message.TryGetValue("Functions", out object functionsObj) || !(functionsObj is string functionsJson))
                {
                    Logger.Warn("BatchGet request missing Functions");
                    return;
                }

                var functionIds = System.Text.Json.JsonSerializer.Deserialize<int[]>(functionsJson);
                if (functionIds == null || functionIds.Length == 0)
                {
                    Logger.Warn("BatchGet request has empty Functions array");
                    return;
                }

                // Build batch response with all property values
                var batchData = new Dictionary<string, object>();
                foreach (var funcId in functionIds)
                {
                    var func = (Shared.Enums.Function)funcId;
                    if (properties.TryGetProperty(func, out var property))
                    {
                        try
                        {
                            var value = property.GetValue();

                            // Structs must be serialized to XML to match individual property sync format
                            // Otherwise they serialize as "{}" in JSON which fails XML deserialization on widget
                            // Only serialize custom structs, not built-in value types like DateTime, TimeSpan, etc.
                            if (value != null)
                            {
                                var valueType = value.GetType();
                                if (valueType.IsValueType && !valueType.IsPrimitive && !valueType.IsEnum
                                    && valueType.Namespace != null && valueType.Namespace.StartsWith("Shared"))
                                {
                                    // Special handling for RunningGame/TrackedGame - check if valid before serializing
                                    // These structs have null strings when invalid/empty which causes XML serialization to fail
                                    bool shouldSerialize = true;
                                    if (value is Shared.Data.RunningGame rg)
                                    {
                                        // Must check ProcessId, GameId.Name AND GameId.Path - XML serializer fails on null strings
                                        shouldSerialize = rg.IsValid() && rg.GameId.IsValid() && !string.IsNullOrEmpty(rg.GameId.Path);
                                        if (!shouldSerialize)
                                        {
                                            Logger.Debug($"RunningGame not valid for XML: ProcessId={rg.ProcessId}, Name={rg.GameId.Name ?? "null"}, Path={rg.GameId.Path ?? "null"}");
                                        }
                                    }
                                    else if (value is Shared.Data.TrackedGame tg)
                                    {
                                        shouldSerialize = !string.IsNullOrEmpty(tg.DisplayName);
                                    }

                                    if (shouldSerialize)
                                    {
                                        value = Shared.Utilities.XmlHelper.ToXMLStringRuntime(value, true);
                                    }
                                    else
                                    {
                                        value = ""; // Return empty string for invalid/empty structs
                                    }
                                }
                            }

                            var propData = new Dictionary<string, object>
                            {
                                { "Content", value },
                                { "UpdatedTime", property.UpdatedTime }
                            };
                            batchData[funcId.ToString()] = propData;
                        }
                        catch (Exception propEx)
                        {
                            var innerMsg = propEx.InnerException?.Message ?? "no inner";
                            Logger.Warn($"BatchGet: Failed to serialize property {func}: {propEx.Message} (Inner: {innerMsg})");
                        }
                    }
                }

                var response = new global::Windows.Foundation.Collections.ValueSet();
                response.Add("BatchData", System.Text.Json.JsonSerializer.Serialize(batchData));
                await request.SendResponseAsync(response);

                timer.Stop();
                Logger.Info($"[TIMING] BatchGet {functionIds.Length} properties: {timer.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Error($"BatchGet failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the event when the app service connection is closed
        /// </summary>
        private static void Connection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
        {
            Logger.Info("Lost connection to the widget.");
            appServiceConnectionStatus = AppServiceConnectionStatus.AppServiceUnavailable;

            Logger.Info("Prepare to re-connect to the widget.");
            try
            {
                connection?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception occurred when disposing the connection: {ex}");
            }

            // Recreate connection
            InitializeConnection();

            // Update all manager connection references
            foreach (var manager in Managers)
            {
                manager.Connection = connection;
            }
        }

        /// <summary>
        /// Parse and send a keyboard shortcut using InputInjector (works in widget context unlike SendInput)
        /// </summary>
        private static void SendKeyboardShortcutViaInputInjector(string shortcut)
        {
            if (inputInjector == null)
            {
                Logger.Error("InputInjector not available - falling back to User32.SendKeyboardShortcut");
                Windows.User32.SendKeyboardShortcut(shortcut);
                return;
            }

            if (string.IsNullOrWhiteSpace(shortcut))
            {
                Logger.Warn("Empty shortcut string provided");
                return;
            }

            try
            {
                var parts = shortcut.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
                var keyInfos = new List<InjectedInputKeyboardInfo>();
                var modifierKeys = new List<ushort>();
                ushort mainKey = 0;

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    ushort vk = 0;

                    if (upper == "CTRL" || upper == "CONTROL")
                        vk = (ushort)VirtualKey.LeftControl;
                    else if (upper == "ALT")
                        vk = (ushort)VirtualKey.LeftMenu;
                    else if (upper == "SHIFT")
                        vk = (ushort)VirtualKey.LeftShift;
                    else if (upper == "WIN" || upper == "WINDOWS" || upper == "LWIN")
                        vk = (ushort)VirtualKey.LeftWindows;
                    else if (upper == "RWIN")
                        vk = (ushort)VirtualKey.RightWindows;
                    else if (upper == "TAB")
                        vk = (ushort)VirtualKey.Tab;
                    else if (upper == "ENTER" || upper == "RETURN")
                        vk = (ushort)VirtualKey.Enter;
                    else if (upper == "ESCAPE" || upper == "ESC")
                        vk = (ushort)VirtualKey.Escape;
                    else if (upper == "SPACE")
                        vk = (ushort)VirtualKey.Space;
                    else if (upper.Length == 1)
                    {
                        char c = upper[0];
                        if (c >= 'A' && c <= 'Z')
                            vk = (ushort)(VirtualKey.A + (c - 'A'));
                        else if (c >= '0' && c <= '9')
                            vk = (ushort)(VirtualKey.Number0 + (c - '0'));
                    }
                    else if (upper.StartsWith("F") && upper.Length <= 3)
                    {
                        if (int.TryParse(upper.Substring(1), out int fNum) && fNum >= 1 && fNum <= 24)
                            vk = (ushort)(VirtualKey.F1 + (fNum - 1));
                    }

                    if (vk == 0)
                    {
                        Logger.Warn($"Unknown key in shortcut: {trimmed}");
                        continue;
                    }

                    // Check if modifier
                    if (upper == "CTRL" || upper == "CONTROL" || upper == "ALT" ||
                        upper == "SHIFT" || upper == "WIN" || upper == "WINDOWS" ||
                        upper == "LWIN" || upper == "RWIN")
                    {
                        modifierKeys.Add(vk);
                    }
                    else
                    {
                        mainKey = vk;
                    }
                }

                // Build key sequence: press modifiers, press+release main key, release modifiers
                // Press modifiers
                foreach (var mod in modifierKeys)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = mod, KeyOptions = InjectedInputKeyOptions.None });
                }

                // Press main key
                if (mainKey != 0)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = mainKey, KeyOptions = InjectedInputKeyOptions.None });
                    // Release main key
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = mainKey, KeyOptions = InjectedInputKeyOptions.KeyUp });
                }

                // Release modifiers in reverse order
                for (int i = modifierKeys.Count - 1; i >= 0; i--)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = modifierKeys[i], KeyOptions = InjectedInputKeyOptions.KeyUp });
                }

                inputInjector.InjectKeyboardInput(keyInfos);
                Logger.Info($"Sent keyboard shortcut via InputInjector: {shortcut}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending keyboard shortcut '{shortcut}': {ex.Message}");
            }
        }

        /// <summary>
        /// Disposes all managers to free resources
        /// </summary>
        private static void DisposeManagers()
        {
            Logger.Info("Disposing all managers...");
            if (Managers != null)
            {
                // Create a copy of the list to avoid "collection was modified" exception
                var managersCopy = Managers.ToList();
                Managers.Clear();
                Managers = null;

                foreach (var manager in managersCopy)
                {
                    try
                    {
                        manager?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error disposing manager: {ex.Message}");
                    }
                }
            }

            // Dispose hotkey manager
            try
            {
                hotkeyManager?.Dispose();
                hotkeyManager = null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disposing hotkey manager: {ex.Message}");
            }

            // Dispose Legion button monitor
            try
            {
                legionButtonMonitor?.Dispose();
                legionButtonMonitor = null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disposing Legion button monitor: {ex.Message}");
            }

            // Clear references
            performanceManager = null;
            rtssManager = null;
            profileManager = null;
            systemManager = null;
            powerManager = null;
            amdManager = null;
            losslessScalingManager = null;
            settingsManager = null;
            legionManager = null;

            Logger.Info("All managers disposed.");
        }

        /// <summary>
        /// Initializes the hotkey manager and registers global hotkeys
        /// </summary>
        private static void InitializeHotkeyManager()
        {
            try
            {
                hotkeyManager = new HotkeyManager();

                // Register Ctrl+Shift+D for Desktop Controls toggle
                int hotkeyId = hotkeyManager.RegisterHotkey(
                    HotkeyManager.MOD_CONTROL | HotkeyManager.MOD_SHIFT,
                    HotkeyManager.VK_D,
                    ToggleDesktopControls);

                if (hotkeyId > 0)
                {
                    Logger.Info("Registered global hotkey Ctrl+Shift+D for Desktop Controls toggle");
                }
                else
                {
                    Logger.Warn("Failed to register Ctrl+Shift+D hotkey - may be in use by another application");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize hotkey manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles Desktop Controls preset via global hotkey (Ctrl+Shift+D)
        /// Applies Joystick-as-Mouse and button mappings for desktop navigation
        /// </summary>
        private static void ToggleDesktopControls()
        {
            try
            {
                if (legionManager == null || !legionManager.LegionGoDetected.Value)
                {
                    Logger.Warn("ToggleDesktopControls: Legion Go not detected, skipping");
                    return;
                }

                // Toggle the state
                bool newState = !legionManager.LegionDesktopControls.Value;
                Logger.Info($"ToggleDesktopControls: Hotkey pressed, toggling from {!newState} to {newState}");

                if (newState)
                {
                    // Enable Desktop Controls:
                    // 1. Set Right Stick as Mouse (mode 2)
                    legionManager.LegionJoystickAsMouseMode.ForceSetValue(2);

                    // 2. Apply desktop button mappings JSON
                    // Format: {"ButtonName":{"Type":X,"GamepadAction":Y,"KeyboardKeys":[...],"MouseButton":Z},...}
                    // Desktop Controls preset: DPAD/LS→Arrows, LSClick→Win, A→Enter, B→Esc, LB→LClick, LT→RClick
                    string desktopMappingsJson = @"{
                        ""DPadUp"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[82],""MouseButton"":0},
                        ""DPadDown"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[81],""MouseButton"":0},
                        ""DPadLeft"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[80],""MouseButton"":0},
                        ""DPadRight"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[79],""MouseButton"":0},
                        ""LSUp"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[82],""MouseButton"":0},
                        ""LSDown"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[81],""MouseButton"":0},
                        ""LSClick"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[227],""MouseButton"":0},
                        ""A"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[40],""MouseButton"":0},
                        ""B"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[41],""MouseButton"":0},
                        ""LB"":{""Type"":2,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LT"":{""Type"":2,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":1}
                    }";
                    legionManager.LegionGamepadMapping.ForceSetValue(desktopMappingsJson);
                }
                else
                {
                    // Disable Desktop Controls:
                    // 1. Disable Joystick as Mouse (mode 0)
                    legionManager.LegionJoystickAsMouseMode.ForceSetValue(0);

                    // 2. Clear button mappings (empty JSON resets to defaults)
                    string resetMappingsJson = @"{
                        ""DPadUp"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""DPadDown"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""DPadLeft"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""DPadRight"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LSUp"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LSDown"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LSClick"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""A"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""B"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LB"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LT"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0}
                    }";
                    legionManager.LegionGamepadMapping.ForceSetValue(resetMappingsJson);
                }

                // Update the Desktop Controls property (syncs to widget UI)
                legionManager.LegionDesktopControls.ForceSetValue(newState);

                Logger.Info($"ToggleDesktopControls: Desktop Controls now {(newState ? "ENABLED" : "DISABLED")}");
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleDesktopControls: Error toggling desktop controls: {ex.Message}");
            }
        }

        #region Labs Section

        /// <summary>
        /// Get the current status of DAService (Legion Space service).
        /// Returns: 0 = Stopped, 1 = Running, 2 = Not Found
        /// </summary>
        private static int GetDAServiceStatus()
        {
            try
            {
                using (var sc = new ServiceController("DAService"))
                {
                    var status = sc.Status;
                    return status == ServiceControllerStatus.Running ? 1 : 0;
                }
            }
            catch (InvalidOperationException)
            {
                // Service not found
                return 2;
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Error getting DAService status: {ex.Message}");
                return 2;
            }
        }

        /// <summary>
        /// Control DAService (Legion Space service).
        /// Action: 0 = Stop and Disable, 1 = Enable and Start
        /// </summary>
        private static void ControlDAService(int action)
        {
            try
            {
                if (action == 0) // Stop and Disable
                {
                    // First stop the service
                    using (var sc = new ServiceController("DAService"))
                    {
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            Logger.Info("Labs: Stopping DAService...");
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                            Logger.Info("Labs: DAService stopped");
                        }
                    }

                    // Then disable the service startup type using sc.exe
                    Logger.Info("Labs: Disabling DAService startup...");
                    var disableProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = "config DAService start= disabled",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true
                        }
                    };
                    disableProcess.Start();
                    disableProcess.WaitForExit(5000);
                    Logger.Info($"Labs: DAService startup disabled (exit code: {disableProcess.ExitCode})");
                }
                else // Enable and Start
                {
                    // First enable the service startup type using sc.exe
                    Logger.Info("Labs: Enabling DAService startup...");
                    var enableProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = "config DAService start= auto",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true
                        }
                    };
                    enableProcess.Start();
                    enableProcess.WaitForExit(5000);
                    Logger.Info($"Labs: DAService startup enabled (exit code: {enableProcess.ExitCode})");

                    // Then start the service
                    using (var sc = new ServiceController("DAService"))
                    {
                        if (sc.Status == ServiceControllerStatus.Stopped)
                        {
                            Logger.Info("Labs: Starting DAService...");
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                            Logger.Info("Labs: DAService started");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Error controlling DAService: {ex.Message}");
            }
        }

        /// <summary>
        /// Export all Default Game Profiles to a text file on the Desktop.
        /// </summary>
        private static string ExportDefaultGameProfiles()
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var exportPath = System.IO.Path.Combine(desktopPath, $"GoTweaks_DGPs_{DateTime.Now:yyyy-MM-dd_HHmmss}.txt");

            using (var writer = new System.IO.StreamWriter(exportPath))
            {
                writer.WriteLine($"GoTweaks Default Game Profiles Export");
                writer.WriteLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"========================================");
                writer.WriteLine();

                if (defaultGameProfileManager == null)
                {
                    writer.WriteLine("DefaultGameProfileManager not initialized.");
                    return exportPath;
                }

                var service = defaultGameProfileManager.GetService();
                if (service == null)
                {
                    writer.WriteLine("DefaultGameProfileService not available.");
                    return exportPath;
                }

                writer.WriteLine($"Hardware Variant: {service.HardwareVariant}");
                writer.WriteLine($"Primary Profile Key: {service.PrimaryProfileKey}");
                writer.WriteLine($"Effective Profile Key: {service.EffectiveProfileKey}");
                writer.WriteLine($"Total Profiles: {service.ProfileCount}");
                writer.WriteLine();
                writer.WriteLine("========================================");
                writer.WriteLine("PROFILE LIST (key -> game name [hardware]: TDP/FPS)");
                writer.WriteLine("========================================");
                writer.WriteLine();

                var keys = service.GetAllProfileKeys().OrderBy(k => k).ToList();
                foreach (var key in keys)
                {
                    var info = service.GetProfileDebugInfo(key);
                    // Format: key -> info (which already includes game name and profiles)
                    writer.WriteLine($"{key} -> {info}");
                }
            }

            return exportPath;
        }

        /// <summary>
        /// Load and apply Legion button remap settings from LocalSettings on startup.
        /// Uses LocalSettingsHelper which works both inside and outside package context.
        /// </summary>
        private static void LoadLegionButtonRemapSettings()
        {
            try
            {
                // Load Legion L settings using LocalSettingsHelper
                // Action: 0=Disabled, 1=Xbox Guide, 2=Keyboard Shortcut, 3=Run Command, 4=Focus GoTweaks
                int lAction = 0;
                string lShortcut = "";
                string lCommand = "";

                if (Settings.LocalSettingsHelper.TryGetValue<int>("LegionL_Action", out var lActionVal))
                    lAction = lActionVal;
                if (Settings.LocalSettingsHelper.TryGetValue<string>("LegionL_Shortcut", out var lShortcutVal))
                    lShortcut = lShortcutVal;
                if (Settings.LocalSettingsHelper.TryGetValue<string>("LegionL_Command", out var lCommandVal))
                    lCommand = lCommandVal;

                // Load Legion R settings
                int rAction = 0;
                string rShortcut = "";
                string rCommand = "";

                if (Settings.LocalSettingsHelper.TryGetValue<int>("LegionR_Action", out var rActionVal))
                    rAction = rActionVal;
                if (Settings.LocalSettingsHelper.TryGetValue<string>("LegionR_Shortcut", out var rShortcutVal))
                    rShortcut = rShortcutVal;
                if (Settings.LocalSettingsHelper.TryGetValue<string>("LegionR_Command", out var rCommandVal))
                    rCommand = rCommandVal;

                // Apply Legion L if not disabled
                if (lAction > 0)
                {
                    // Map action index to actionType: 1=Xbox Guide(0), 2=Shortcut(1), 3=Command(2), 4=FocusGoTweaks(3)
                    int actionType = lAction - 1;
                    string shortcutOrCommand = actionType == 1 ? lShortcut : (actionType == 2 ? lCommand : "");
                    bool success = ConfigureLegionButtonRemap("L", true, actionType, shortcutOrCommand);
                    Logger.Info($"Labs: Loaded Legion L remap from settings - Action={lAction}, Success={success}");
                }

                // Apply Legion R if not disabled
                if (rAction > 0)
                {
                    int actionType = rAction - 1;
                    string shortcutOrCommand = actionType == 1 ? rShortcut : (actionType == 2 ? rCommand : "");
                    bool success = ConfigureLegionButtonRemap("R", true, actionType, shortcutOrCommand);
                    Logger.Info($"Labs: Loaded Legion R remap from settings - Action={rAction}, Success={success}");
                }

                if (lAction == 0 && rAction == 0)
                {
                    Logger.Info("Labs: No Legion button remap settings found in LocalSettings");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Failed to load Legion button remap settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Load and apply Legion scroll wheel remap settings from LocalSettings on startup.
        /// Uses LocalSettingsHelper which works both inside and outside package context.
        /// </summary>
        private static void LoadLegionScrollRemapSettings()
        {
            try
            {
                // Load Scroll (unified Up/Down) settings
                // Action: 0=Disabled, 1=Xbox Guide, 2=Keyboard Shortcut, 3=Run Command, 4=Focus GoTweaks
                int scrollAction = 0;
                string scrollShortcut = "";
                string scrollCommand = "";

                if (Settings.LocalSettingsHelper.TryGetValue<int>("Scroll_Action", out var scrollActionVal))
                    scrollAction = scrollActionVal;
                if (Settings.LocalSettingsHelper.TryGetValue<string>("Scroll_Shortcut", out var scrollShortcutVal))
                    scrollShortcut = scrollShortcutVal;
                if (Settings.LocalSettingsHelper.TryGetValue<string>("Scroll_Command", out var scrollCommandVal))
                    scrollCommand = scrollCommandVal;

                // Load Scroll Click settings
                int clickAction = 0;
                string clickShortcut = "";
                string clickCommand = "";

                if (Settings.LocalSettingsHelper.TryGetValue<int>("ScrollClick_Action", out var clickActionVal))
                    clickAction = clickActionVal;
                if (Settings.LocalSettingsHelper.TryGetValue<string>("ScrollClick_Shortcut", out var clickShortcutVal))
                    clickShortcut = clickShortcutVal;
                if (Settings.LocalSettingsHelper.TryGetValue<string>("ScrollClick_Command", out var clickCommandVal))
                    clickCommand = clickCommandVal;

                // Apply Scroll Up/Down if not disabled (unified action for both directions)
                if (scrollAction > 0)
                {
                    // Map action index to actionType: 1=Xbox Guide(0), 2=Shortcut(1), 3=Command(2), 4=FocusGoTweaks(3)
                    int actionType = scrollAction - 1;
                    string shortcutOrCommand = actionType == 1 ? scrollShortcut : (actionType == 2 ? scrollCommand : "");

                    // Apply to both Up and Down (unified scroll action)
                    bool successUp = ConfigureLegionScrollRemap("Up", true, actionType, shortcutOrCommand);
                    bool successDown = ConfigureLegionScrollRemap("Down", true, actionType, shortcutOrCommand);
                    Logger.Info($"Labs: Loaded Scroll Up/Down remap from settings - Action={scrollAction}, SuccessUp={successUp}, SuccessDown={successDown}");
                }

                // Apply Scroll Click if not disabled
                if (clickAction > 0)
                {
                    int actionType = clickAction - 1;
                    string shortcutOrCommand = actionType == 1 ? clickShortcut : (actionType == 2 ? clickCommand : "");
                    bool success = ConfigureLegionScrollRemap("Click", true, actionType, shortcutOrCommand);
                    Logger.Info($"Labs: Loaded Scroll Click remap from settings - Action={clickAction}, Success={success}");
                }

                if (scrollAction == 0 && clickAction == 0)
                {
                    Logger.Info("Labs: No Legion scroll wheel remap settings found in LocalSettings");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Failed to load Legion scroll wheel remap settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Configure Legion button remap (L or R to Xbox Guide or Keyboard Shortcut).
        /// Uses a single unified monitor that handles both buttons and battery.
        /// </summary>
        /// <param name="button">"L" for Legion L, "R" for Legion R</param>
        /// <param name="enabled">Whether to enable the remap</param>
        /// <param name="actionType">0=Xbox Guide, 1=Keyboard Shortcut, 2=Run Command, 3=Focus GoTweaks</param>
        /// <param name="shortcutOrCommand">Keyboard shortcut string (e.g., "Win+G") or command path</param>
        /// <returns>True if successful</returns>
        private static bool ConfigureLegionButtonRemap(string button, bool enabled, int actionType, string shortcutOrCommand)
        {
            try
            {
                // Create unified monitor if it doesn't exist
                if (legionButtonMonitor == null)
                {
                    legionButtonMonitor = new LegionButtonMonitor();

                    // Subscribe to battery updates once
                    legionButtonMonitor.BatteryUpdated += (sender, e) =>
                    {
                        try
                        {
                            legionManager?.UpdateControllerBatteryFromButtonMonitor(
                                e.LeftBattery, e.LeftCharging, e.LeftConnected,
                                e.RightBattery, e.RightCharging, e.RightConnected);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Labs: BatteryUpdated handler exception: {ex.Message}\n{ex.StackTrace}");
                        }
                    };
                    Logger.Info("Labs: Created unified Legion button monitor with battery support");
                }

                // Remember state before configuration
                bool wasRunning = legionButtonMonitor.IsRunning;
                bool neededViGEmBefore = legionButtonMonitor.NeedsViGEm;

                // Configure the button on the unified monitor
                // This just updates internal flags - the monitor loop will pick up changes on next iteration
                legionButtonMonitor.ConfigureButton(
                    button,
                    enabled,
                    actionType,
                    shortcutOrCommand,
                    (shortcutKeys) =>
                    {
                        // Execute the keyboard shortcut when the button is pressed
                        Logger.Debug($"Labs: Executing shortcut '{shortcutKeys}'");
                        SendKeyboardShortcutViaInputInjector(shortcutKeys);
                    },
                    (commandPath) =>
                    {
                        // Execute the command when the button is pressed
                        Logger.Debug($"Labs: Executing command '{commandPath}'");
                        ExecuteCommand(commandPath);
                    },
                    () =>
                    {
                        // Focus GoTweaks widget when the button is pressed
                        Logger.Debug("Labs: Focusing GoTweaks widget");
                        FocusGoTweaksWidget();
                    }
                );

                // Check if ViGEm requirements changed (need to restart monitor to add/remove ViGEm controller)
                bool needsViGEmNow = legionButtonMonitor.NeedsViGEm;
                bool vigemRequirementChanged = neededViGEmBefore != needsViGEmNow;

                // Handle different scenarios
                if (!legionButtonMonitor.HasAnyButtonConfigured)
                {
                    // No buttons configured - restart for battery-only mode if it was running with buttons
                    if (wasRunning && neededViGEmBefore)
                    {
                        // Was running with ViGEm for buttons, restart for battery-only
                        Logger.Info($"Labs: Legion {button} button disabled, restarting monitor for battery-only mode");
                        legionButtonMonitor.Stop();
                        legionButtonMonitor.StartForBatteryMonitoring();
                    }
                    else if (!wasRunning)
                    {
                        // Monitor wasn't running, start for battery monitoring
                        legionButtonMonitor.StartForBatteryMonitoring();
                    }
                    // else: already running for battery-only, no change needed
                    Logger.Info($"Labs: Legion {button} button disabled, no buttons configured - battery monitoring continues");
                    return true;
                }
                else if (wasRunning && vigemRequirementChanged)
                {
                    // ViGEm requirement changed - need to restart monitor
                    Logger.Info($"Labs: ViGEm requirement changed ({neededViGEmBefore} -> {needsViGEmNow}), restarting monitor");
                    legionButtonMonitor.Stop();
                    if (!legionButtonMonitor.Start())
                    {
                        string errorReason = needsViGEmNow ? "ViGEmBus not installed or " : "";
                        Logger.Error($"Labs: Failed to restart Legion button monitoring ({errorReason}controller not found)");
                        return false;
                    }
                }
                else if (!wasRunning)
                {
                    // Monitor wasn't running - start it
                    if (!legionButtonMonitor.Start())
                    {
                        string errorReason = needsViGEmNow ? "ViGEmBus not installed or " : "";
                        Logger.Error($"Labs: Failed to start Legion button monitoring ({errorReason}controller not found)");
                        return false;
                    }
                }
                // else: Monitor was running and ViGEm requirement didn't change - config is hot-applied

                string actionName = !enabled ? "Disabled" :
                                   actionType == 0 ? "Xbox Guide" :
                                   actionType == 1 ? $"Shortcut: {shortcutOrCommand}" :
                                   actionType == 2 ? $"Command: {shortcutOrCommand}" :
                                   "Focus GoTweaks";
                Logger.Info($"Labs: Legion {button} button configured -> {actionName}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Error configuring Legion {button} button remap: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Configure scroll wheel remap (Up/Down/Click to Xbox Guide, Keyboard Shortcut, Command, or Focus GoTweaks).
        /// Uses the unified LegionButtonMonitor which handles both buttons and scroll wheel.
        /// </summary>
        /// <param name="direction">"Up", "Down", or "Click"</param>
        /// <param name="enabled">Whether to enable the remap</param>
        /// <param name="actionType">0=Xbox Guide, 1=Keyboard Shortcut, 2=Run Command, 3=Focus GoTweaks</param>
        /// <param name="shortcutOrCommand">Keyboard shortcut string or command path</param>
        /// <returns>True if successful</returns>
        private static bool ConfigureLegionScrollRemap(string direction, bool enabled, int actionType, string shortcutOrCommand)
        {
            try
            {
                // Create unified monitor if it doesn't exist
                if (legionButtonMonitor == null)
                {
                    legionButtonMonitor = new LegionButtonMonitor();

                    // Subscribe to battery updates once
                    legionButtonMonitor.BatteryUpdated += (sender, e) =>
                    {
                        try
                        {
                            legionManager?.UpdateControllerBatteryFromButtonMonitor(
                                e.LeftBattery, e.LeftCharging, e.LeftConnected,
                                e.RightBattery, e.RightCharging, e.RightConnected);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Labs: BatteryUpdated handler exception: {ex.Message}\n{ex.StackTrace}");
                        }
                    };
                    Logger.Info("Labs: Created unified Legion button monitor with battery support");
                }

                // Remember state before configuration
                bool wasRunning = legionButtonMonitor.IsRunning;
                bool neededViGEmBefore = legionButtonMonitor.NeedsViGEm;

                // Configure the scroll wheel action on the unified monitor
                legionButtonMonitor.ConfigureScrollWheel(
                    direction,
                    enabled,
                    actionType,
                    shortcutOrCommand,
                    (shortcutKeys) =>
                    {
                        // Execute the keyboard shortcut when scroll action is triggered
                        Logger.Debug($"Labs: Executing shortcut '{shortcutKeys}' for scroll {direction}");
                        SendKeyboardShortcutViaInputInjector(shortcutKeys);
                    },
                    (commandPath) =>
                    {
                        // Execute the command when scroll action is triggered
                        Logger.Debug($"Labs: Executing command '{commandPath}' for scroll {direction}");
                        ExecuteCommand(commandPath);
                    },
                    () =>
                    {
                        // Focus GoTweaks widget when scroll action is triggered
                        Logger.Debug($"Labs: Focusing GoTweaks widget for scroll {direction}");
                        FocusGoTweaksWidget();
                    }
                );

                // Check if ViGEm requirements changed
                bool needsViGEmNow = legionButtonMonitor.NeedsViGEm;
                bool vigemRequirementChanged = neededViGEmBefore != needsViGEmNow;

                // Handle different scenarios
                if (!legionButtonMonitor.HasAnyButtonConfigured && !legionButtonMonitor.HasAnyScrollConfigured)
                {
                    // No buttons or scroll configured - restart for battery-only mode if it was running
                    if (wasRunning && neededViGEmBefore)
                    {
                        Logger.Info($"Labs: Scroll {direction} disabled, no buttons/scroll configured - restarting for battery-only");
                        legionButtonMonitor.Stop();
                        legionButtonMonitor.StartForBatteryMonitoring();
                    }
                    else if (!wasRunning)
                    {
                        legionButtonMonitor.StartForBatteryMonitoring();
                    }
                    Logger.Info($"Labs: Scroll {direction} disabled - battery monitoring continues");
                    return true;
                }
                else if (wasRunning && vigemRequirementChanged)
                {
                    // ViGEm requirement changed - need to restart monitor
                    Logger.Info($"Labs: ViGEm requirement changed ({neededViGEmBefore} -> {needsViGEmNow}), restarting monitor");
                    legionButtonMonitor.Stop();
                    if (!legionButtonMonitor.Start())
                    {
                        string errorReason = needsViGEmNow ? "ViGEmBus not installed or " : "";
                        Logger.Error($"Labs: Failed to restart monitoring ({errorReason}controller not found)");
                        return false;
                    }
                }
                else if (!wasRunning)
                {
                    // Monitor wasn't running - start it
                    if (!legionButtonMonitor.Start())
                    {
                        string errorReason = needsViGEmNow ? "ViGEmBus not installed or " : "";
                        Logger.Error($"Labs: Failed to start monitoring ({errorReason}controller not found)");
                        return false;
                    }
                }
                // else: Monitor was running and ViGEm requirement didn't change - config is hot-applied

                string actionName = !enabled ? "Disabled" :
                                   actionType == 0 ? "Xbox Guide" :
                                   actionType == 1 ? $"Shortcut: {shortcutOrCommand}" :
                                   actionType == 2 ? $"Command: {shortcutOrCommand}" :
                                   "Focus GoTweaks";
                Logger.Info($"Labs: Scroll {direction} configured -> {actionName}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Error configuring scroll {direction} remap: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Execute a command/executable with optional arguments.
        /// </summary>
        private static void ExecuteCommand(string commandPath)
        {
            try
            {
                if (string.IsNullOrEmpty(commandPath))
                    return;

                // Parse the command - first part is the executable, rest are arguments
                string exe;
                string args = "";

                // Check if the path is quoted
                if (commandPath.StartsWith("\""))
                {
                    int endQuote = commandPath.IndexOf('"', 1);
                    if (endQuote > 0)
                    {
                        exe = commandPath.Substring(1, endQuote - 1);
                        if (endQuote + 1 < commandPath.Length)
                            args = commandPath.Substring(endQuote + 1).Trim();
                    }
                    else
                    {
                        exe = commandPath;
                    }
                }
                else
                {
                    // Find the first space that's not inside the exe path
                    int spaceIndex = commandPath.IndexOf(' ');
                    if (spaceIndex > 0)
                    {
                        exe = commandPath.Substring(0, spaceIndex);
                        args = commandPath.Substring(spaceIndex + 1).Trim();
                    }
                    else
                    {
                        exe = commandPath;
                    }
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
                };

                System.Diagnostics.Process.Start(startInfo);
                Logger.Info($"Labs: Executed command: {exe} {args}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Failed to execute command '{commandPath}': {ex.Message}");
            }
        }

        /// <summary>
        /// Focus GoTweaks widget by opening Game Bar and sending activation command to widget.
        /// Win+G is required to open Game Bar before widget can be activated.
        /// </summary>
        private static async void FocusGoTweaksWidget()
        {
            try
            {
                // Debounce: ignore rapid button presses
                var now = DateTime.Now;
                if ((now - lastFocusWidgetTime).TotalMilliseconds < FocusWidgetDebounceMs)
                {
                    Logger.Debug("Labs: Focus widget debounced (rapid press ignored)");
                    return;
                }
                lastFocusWidgetTime = now;

                // Open Game Bar (required for widget activation)
                SendKeyboardShortcutViaInputInjector("Win+G");
                Logger.Info("Labs: Sent Win+G to open Game Bar");

                // Delay to ensure Game Bar is fully open and widget is ready
                await Task.Delay(500);

                // Send focus command to widget via Named Pipes (works when running elevated)
                if (IsPipeConnected)
                {
                    var pipeMsg = new Shared.IPC.PipeMessage
                    {
                        Command = Shared.Enums.Command.Set,
                        Function = Shared.Enums.Function.Labs_FocusWidget
                    };
                    if (SendPipeMessage(pipeMsg))
                    {
                        Logger.Info("Labs: Sent focus widget command via Named Pipe");
                    }
                    else
                    {
                        Logger.Warn("Labs: Failed to send focus widget command via Named Pipe");
                    }
                }
                // Fallback to AppService if available (running in package context)
                else if (connection != null && appServiceConnectionStatus == AppServiceConnectionStatus.Success)
                {
                    var message = new global::Windows.Foundation.Collections.ValueSet();
                    message.Add("Command", (int)Shared.Enums.Command.Set);
                    message.Add("Function", (int)Shared.Enums.Function.Labs_FocusWidget);
                    await connection.SendMessageAsync(message);
                    Logger.Info("Labs: Sent focus widget command via AppService");
                }
                else
                {
                    Logger.Warn("Labs: Cannot send focus widget command - no connection available");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Failed to focus GoTweaks widget: {ex.Message}");
            }
        }

        #endregion
    }
}
