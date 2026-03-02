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
    internal class Program
    {
        // IMPORTANT: This field initializer MUST appear BEFORE the Logger field!
        // It ensures LogDirectory GDC is set before NLog creates the Logger.
        // NLog resolves ${gdc:item=LogDirectory} when the logger/target is first used,
        // so we need LogDirectory set before any logging happens.
        private static readonly bool _logDirConfigured = InitLogDirectory();

        private static bool InitLogDirectory()
        {
            ConfigureLogDirectory();
            return true;
        }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static Mutex singleInstanceMutex;
        private static CancellationToken _serviceCancellationToken;
        private static bool _isRunningAsService = false;
        private static volatile bool _isShuttingDown = false;
        private static volatile bool _restartInProgress = false;
        private static HelperTrayIndicator _trayIndicator;

        // P/Invoke for SetDllDirectory - must be called BEFORE any native DLLs are loaded
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        // P/Invoke for LoadLibrary - used to explicitly preload native DLLs with full path
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        // P/Invoke for screen saver idle detection and monitor power control
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        private const uint WM_SYSCOMMAND = 0x0112;
        private const uint WM_QUIT = 0x0012;
        private static readonly IntPtr SC_MONITORPOWER = new IntPtr(0xF170);
        private static readonly IntPtr MONITOR_OFF = new IntPtr(2);

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
        private static GPDManager gpdManager;
        private static ControllerEmulationManager controllerEmulationManager;
        private static AutoTDPManager autoTDPManager;
        private static DefaultGameProfileManager defaultGameProfileManager;
        private static List<IManager> Managers;

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
        /// Timestamp when the last profile switch completed.
        /// Used to implement a cooldown period to reject stale widget messages.
        /// </summary>
        private static DateTime profileSwitchTime = DateTime.MinValue;

        /// <summary>
        /// Cooldown period in milliseconds after a profile switch.
        /// Stale widget messages arriving during this period are rejected to prevent profile corruption.
        /// </summary>
        private const int PROFILE_SWITCH_COOLDOWN_MS = 500;

        /// <summary>
        /// Helper method to check if we're in the cooldown period after a profile switch.
        /// </summary>
        private static bool IsInProfileSwitchCooldown()
        {
            return (DateTime.UtcNow - profileSwitchTime).TotalMilliseconds < PROFILE_SWITCH_COOLDOWN_MS;
        }

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
        private static readonly object legionButtonMonitorLock = new object();
        private static bool legionButtonMonitorBatteryHooked;

        /// <summary>
        /// Hotkey manager for global keyboard shortcuts (Ctrl+Shift+D for Desktop Controls)
        /// </summary>
        private static HotkeyManager hotkeyManager;

        /// <summary>
        /// Controller hotkey monitor for gamepad button combos (Menu+DPad, View+ABXY)
        /// Uses XInput to detect combos system-wide, including in games
        /// </summary>
        private static ControllerHotkeyMonitor controllerHotkeyMonitor;

        /// <summary>
        /// Named Pipe server for IPC with the widget (works when elevated via scheduled task)
        /// </summary>
        private static IPC.NamedPipeServer pipeServer;

        /// <summary>
        /// Flag indicating whether all managers are initialized and ready to handle requests.
        /// When false, BatchGet requests will return a "NotReady" response.
        /// </summary>
        private static volatile bool _managersReady = false;

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
        /// Screen saver idle monitoring - triggers Windows screen saver after idle timeout
        /// </summary>
        private static volatile bool screenSaverEnabled = false;
        private static volatile bool screenSaverTriggered = false;
        private static System.Threading.Timer screenSaverTimer;
        private const int ScreenSaverIdleTimeoutMs = 60000; // 60 seconds idle before triggering
        private const int ScreenSaverCheckIntervalMs = 5000; // Check every 5 seconds

        /// <summary>
        /// Auto hibernate idle monitoring - hibernates after inactivity timeout
        /// </summary>
        private static volatile bool autoHibernateEnabled = false;
        private static volatile int autoHibernateMode = 0; // 0=Always, 1=AC Only, 2=DC Only
        private static System.Threading.Timer autoHibernateTimer;
        private static DateTime lastAutoHibernateAttemptUtc = DateTime.MinValue;
        private static int autoHibernateIdleTimeoutMs = 15 * 60 * 1000; // 15 minutes idle before hibernate
        private const int AutoHibernateCheckIntervalMs = 30000; // Check every 30 seconds
        private const int AutoHibernateCooldownMs = 5 * 60 * 1000; // Minimum time between attempts

        /// <summary>
        /// Configures NLog to write logs to the package's LocalCache/Local folder.
        /// Must be called BEFORE any logging happens.
        /// </summary>
        private static void ConfigureLogDirectory()
        {
            string logDir = null;

            try
            {
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

                // Force NLog to reconfigure to pick up the new LogDirectory
                LogManager.ReconfigExistingLoggers();
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

            // Log startup info
            Logger.Info($"=== Helper starting, PID={Process.GetCurrentProcess().Id} ===");
            LogManager.Flush();

            // Check for export-profiles mode - exports registry game profiles to bundled JSON
            // Run this on a system with Xbox FSE enabled to generate bundled_profiles.json
            if (args.Contains("--export-profiles"))
            {
                Logger.Info("=== Export Profiles Mode ===");
                var outputPath = args.SkipWhile(a => a != "--export-profiles").Skip(1).FirstOrDefault()
                    ?? Path.Combine(Environment.CurrentDirectory, "bundled_profiles.json");

                Logger.Info($"Exporting profiles to: {outputPath}");
                var count = BundledProfileExporter.ExportToJson(outputPath);
                Logger.Info($"Exported {count} game profiles");
                Console.WriteLine($"Exported {count} game profiles to: {outputPath}");
                LogManager.Flush();
                return;
            }

            // Check for setup mode FIRST (before anything else)
            // Setup mode: deploy files, create scheduled task, run task, then EXIT
            // The task launches the elevated helper which will connect to the widget.
            if (args.Contains("--setup"))
            {
                // Debug file for tracing setup issues (independent of NLog)
                var setupDebugPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "setup_debug.txt");
                void SetupDebugLog(string msg) { try { File.AppendAllText(setupDebugPath, $"{DateTime.Now}: [Main] {msg}\n"); } catch { } }

                SetupDebugLog($"Setup mode entered, args={string.Join(" ", args)}");
                Logger.Info("=== Setup Mode ===");
                try
                {
                    SetupDebugLog("Calling PerformSetup...");
                    bool success = ElevationBootstrapper.PerformSetup();
                    SetupDebugLog($"PerformSetup returned: {success}");
                    Logger.Info($"Setup completed with result: {success}");

                    if (success)
                    {
                        // Run the scheduled task to start the elevated helper
                        // This helper will connect to the widget
                        Logger.Info("Running scheduled task to start elevated helper...");
                        SetupDebugLog("Running scheduled task...");

                        // CRITICAL: Shutdown NLog to release the log file BEFORE starting the elevated helper
                        // Otherwise the elevated helper's Wave1 initialization logs (including AMDManager/ADLX)
                        // will be blocked because this process still has the log file open
                        LogManager.Shutdown();

                        if (Services.ScheduledTaskService.RunTaskNow())
                        {
                            SetupDebugLog("Task started OK");
                        }
                        else
                        {
                            SetupDebugLog("Task failed to start");
                        }
                    }

                    SetupDebugLog("Exiting setup mode");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Setup failed with exception");
                    SetupDebugLog($"EXCEPTION in setup: {ex.Message}\n{ex.StackTrace}");
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
            LogManager.Flush(); // Ensure mutex acquisition log is written before Initialize

            try
            {
                TryStartTrayIndicator();
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
                DisposeTrayIndicator();
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
            DisposeTrayIndicator();

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

                // Dispose hotkey manager
                hotkeyManager?.Dispose();
                hotkeyManager = null;

                // Dispose Legion button monitor
                DisposeLegionButtonMonitor();

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
                    connected = pipeServer?.IsConnected ?? false,
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
                    defaultGameProfileManager = new DefaultGameProfileManager(performanceManager, rtssManager, systemManager, profileManager, legionManager);

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
        /// Wait for the widget to connect via Named Pipe
        /// </summary>
        private static Task WaitForWidgetConnection(bool blocking)
        {
            if (blocking && pipeServer != null)
            {
                Logger.Info("Waiting for widget to connect via Named Pipe...");
                // Don't block forever - the main loop will handle reconnection
                // The pipe server is already listening for connections
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Open connection to UWP app service
        /// </summary>
        private static async Task Initialize()
        {
            var initTimer = System.Diagnostics.Stopwatch.StartNew();

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

                        // Preload ADLXCSharpBind.dll with full path to ensure it's found
                        // This bypasses DLL search path issues when running from deployed location
                        var adlxDllPath = Path.Combine(exeDir, "ADLXCSharpBind.dll");
                        if (File.Exists(adlxDllPath))
                        {
                            var handle = LoadLibrary(adlxDllPath);
                            if (handle == IntPtr.Zero)
                            {
                                var error = Marshal.GetLastWin32Error();
                                Logger.Error($"Failed to preload ADLXCSharpBind.dll: Win32 error {error} (0x{error:X})");
                            }
                            else
                            {
                                Logger.Info($"Preloaded ADLXCSharpBind.dll from: {adlxDllPath}");
                            }
                        }
                        else
                        {
                            Logger.Warn($"ADLXCSharpBind.dll not found at: {adlxDllPath}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not set DLL directory: {ex.Message}");
            }

            // START PIPE SERVER EARLY - Widget can connect while managers initialize
            // BatchGet requests will return "NotReady" until _managersReady is true
            var pipeTimer = System.Diagnostics.Stopwatch.StartNew();
            InitializeConnection();
            pipeTimer.Stop();
            Logger.Info($"[TIMING] Pipe server started early: {pipeTimer.ElapsedMilliseconds}ms");

            // PRE-POPULATE DeviceDetector cache BEFORE parallel initialization
            // This avoids duplicate WMI queries when LegionManager and GPDManager both call DetectDevice()
            var deviceTimer = System.Diagnostics.Stopwatch.StartNew();
            var deviceInfo = Devices.DeviceDetector.DetectDevice();
            deviceTimer.Stop();
            Logger.Info($"[TIMING] DeviceDetector pre-cached: {deviceTimer.ElapsedMilliseconds}ms (Device: {deviceInfo.Manufacturer} {deviceInfo.Model})");

            // PARALLEL MANAGER INITIALIZATION - Wave-based to respect dependencies
            var totalTimer = System.Diagnostics.Stopwatch.StartNew();
            Logger.Info("Initialize managers (parallel waves)...");
            LogManager.Flush(); // Ensure this log appears before parallel tasks start

            // Wave 1: Independent managers (no dependencies) - run in parallel
            var wave1Timer = System.Diagnostics.Stopwatch.StartNew();
            Logger.Info("Wave 1: PerformanceManager, ProfileManager, AMDManager, LosslessScalingManager, SettingsManager, LegionManager, GPDManager");
            LogManager.Flush();

            PerformanceManager tempPerfMgr = null;
            ProfileManager tempProfileMgr = null;
            AMDManager tempAmdMgr = null;
            LosslessScalingManager tempLosslessMgr = null;
            SettingsManager tempSettingsMgr = null;
            LegionManager tempLegionMgr = null;
            GPDManager tempGpdMgr = null;

            var wave1Tasks = new[]
            {
                Task.Run(() => {
                    try { tempPerfMgr = new PerformanceManager(); Logger.Info("Wave1: PerformanceManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: PerformanceManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempProfileMgr = new ProfileManager(); Logger.Info("Wave1: ProfileManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: ProfileManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempAmdMgr = new AMDManager(); Logger.Info("Wave1: AMDManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: AMDManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempLosslessMgr = new LosslessScalingManager(); Logger.Info("Wave1: LosslessScalingManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: LosslessScalingManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempSettingsMgr = SettingsManager.CreateInstance(); Logger.Info("Wave1: SettingsManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: SettingsManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempLegionMgr = new LegionManager(); Logger.Info("Wave1: LegionManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: LegionManager FAILED"); throw; }
                }),
                Task.Run(() => {
                    try { tempGpdMgr = new GPDManager(); Logger.Info("Wave1: GPDManager DONE"); }
                    catch (Exception ex) { Logger.Error(ex, "Wave1: GPDManager FAILED"); throw; }
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

            // Flush logs from parallel tasks to ensure they appear in order
            LogManager.Flush();

            performanceManager = tempPerfMgr;
            profileManager = tempProfileMgr;
            amdManager = tempAmdMgr;
            losslessScalingManager = tempLosslessMgr;
            settingsManager = tempSettingsMgr;
            legionManager = tempLegionMgr;
            gpdManager = tempGpdMgr;

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
                Task.Run(() => { tempRtssMgr = new RTSSManager(performanceManager); }),
                Task.Run(() => { tempSystemMgr = new SystemManager(profileManager.GameProfiles); }),
                Task.Run(() => { tempPowerMgr = new PowerManager(performanceManager.RyzenAdjHandle); })
            };
            Task.WaitAll(wave2Tasks);
            LogManager.Flush();

            rtssManager = tempRtssMgr;
            systemManager = tempSystemMgr;
            powerManager = tempPowerMgr;

            wave2Timer.Stop();
            Logger.Info($"[TIMING] Wave 2 (parallel): {wave2Timer.ElapsedMilliseconds}ms");

            // Wave 3: Managers that depend on Wave 2
            var wave3Timer = System.Diagnostics.Stopwatch.StartNew();
            Logger.Info("Wave 3: AutoTDPManager");
            autoTDPManager = new AutoTDPManager(performanceManager, systemManager);
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

            // Set PerformanceManager reference in GPDManager for software fan curve CPU temperature access
            gpdManager?.SetPerformanceManager(performanceManager);

            // Initialize handheld-agnostic controller emulation manager.
            controllerEmulationManager = new ControllerEmulationManager(legionManager, gpdManager, settingsManager);

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

                    LegionButtonMonitor monitor = EnsureLegionButtonMonitor();

                    if (monitor.StartForBatteryMonitoring())
                    {
                        Logger.Info("Legion button monitor started for battery monitoring");
                        // Update VID:PID in LegionManager
                        var vidPid = monitor.DetectedVidPid;
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

            // Set RTSSManager reference in AutoTDPManager for frametime stability detection
            autoTDPManager.SetRTSSManager(rtssManager);

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
                gpdManager,
                controllerEmulationManager,
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
                systemManager.DisplayOrientation,
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
                settingsManager.AutoHibernateEnabled,
                settingsManager.AutoHibernateIdleMinutes,
                settingsManager.OnScreenDisplayProvider,
                settingsManager.UseManufacturerWMI,
                settingsManager.TdpMethod,
                // Profile Detection Settings
                settingsManager.ProfileMatchByExe,
                settingsManager.ProfileCustomGamePath,
                settingsManager.ProfileGamesOnly,
                settingsManager.ProfileBlacklistPaths,
                systemManager.ForegroundApp,
                // GPD specific properties
                gpdManager.GPDDetected,
                gpdManager.Win5Connected,
                gpdManager.Win5HidDebug,
                gpdManager.Win5HidDevices,
                gpdManager.DeviceName,
                gpdManager.SupportsFanControlProp,
                gpdManager.RestoreDefaults,
                gpdManager.ApplyMappings,
                // GPD Win 5 button remapping properties
                gpdManager.ButtonA,
                gpdManager.ButtonB,
                gpdManager.ButtonX,
                gpdManager.ButtonY,
                gpdManager.ButtonDPadUp,
                gpdManager.ButtonDPadDown,
                gpdManager.ButtonDPadLeft,
                gpdManager.ButtonDPadRight,
                gpdManager.ButtonL3,
                gpdManager.ButtonR3,
                gpdManager.ButtonL4,
                gpdManager.ButtonR4,
                gpdManager.ButtonLSUp,
                gpdManager.ButtonLSDown,
                gpdManager.ButtonLSLeft,
                gpdManager.ButtonLSRight,
                // GPD fan control properties
                gpdManager.FanSpeed,
                gpdManager.FanRPM,
                gpdManager.FanMode,
                // GPD software fan curve properties
                gpdManager.FanCurveEnabled,
                gpdManager.FanCurveData,
                gpdManager.FanCurveVisibleProp,
                gpdManager.CPUTemp,
                gpdManager.GyroSource,
                gpdManager.GyroSimulateMode,
                // Handheld-agnostic controller emulation properties
                controllerEmulationManager.ControllerEmulationAvailable,
                controllerEmulationManager.ControllerEmulationEnabled,
                controllerEmulationManager.ControllerEmulationHideStockController,
                controllerEmulationManager.ControllerEmulationImprovedInput,
                controllerEmulationManager.ControllerEmulationHideTarget,
                controllerEmulationManager.ControllerEmulationGyroSource,
                controllerEmulationManager.ControllerEmulationMode,
                controllerEmulationManager.ControllerEmulationRumbleProfile,
                controllerEmulationManager.ControllerEmulationGyroActivationMode,
                controllerEmulationManager.ControllerEmulationGyroActivationButton,
                controllerEmulationManager.ControllerEmulationDs4Orientation,
                controllerEmulationManager.ControllerEmulationPs4TouchpadEnabled,
                controllerEmulationManager.ControllerEmulationMouseSensitivity,
                controllerEmulationManager.ControllerEmulationMouseThreshold,
                controllerEmulationManager.ControllerEmulationMouseAxis,
                controllerEmulationManager.ControllerEmulationMouseInvertX,
                controllerEmulationManager.ControllerEmulationMouseInvertY,
                controllerEmulationManager.ControllerEmulationMouseGainX,
                controllerEmulationManager.ControllerEmulationMouseGainY,
                controllerEmulationManager.ControllerEmulationStickSensitivity,
                controllerEmulationManager.ControllerEmulationStickThreshold,
                controllerEmulationManager.ControllerEmulationStickAxis,
                controllerEmulationManager.ControllerEmulationStickInvertX,
                controllerEmulationManager.ControllerEmulationStickInvertY,
                controllerEmulationManager.ControllerEmulationStickGainX,
                controllerEmulationManager.ControllerEmulationStickGainY,
                controllerEmulationManager.ControllerEmulationStickSelect,
                controllerEmulationManager.ControllerEmulationStickExcessMove,
                controllerEmulationManager.ControllerEmulationStickRange,
                controllerEmulationManager.ControllerEmulationStickOnlyJoystickData,
                controllerEmulationManager.ControllerEmulationVirtualABXYLayout,
                controllerEmulationManager.ControllerEmulationLedForwardingEnabled,
                controllerEmulationManager.ControllerEmulationCalibrateGyro,
                controllerEmulationManager.ControllerEmulationStickMinGyroSpeed,
                controllerEmulationManager.ControllerEmulationStickMaxGyroSpeed,
                controllerEmulationManager.ControllerEmulationStickMinOutput,
                controllerEmulationManager.ControllerEmulationStickMaxOutput,
                controllerEmulationManager.ControllerEmulationStickPowerCurve,
                controllerEmulationManager.ControllerEmulationStickSensitivityV2,
                controllerEmulationManager.ControllerEmulationStickDeadzone,
                controllerEmulationManager.ControllerEmulationStickPrecisionSpeed,
                controllerEmulationManager.ControllerEmulationStickOutputMix,
                controllerEmulationManager.ControllerEmulationStickOrientationV2,
                controllerEmulationManager.ControllerEmulationStickConversion,
                // Legion Go specific properties
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
                // Device capability properties (for UI visibility based on device features)
                legionManager.DeviceDisplayName,
                legionManager.DeviceSupportsControllerRemap,
                legionManager.DeviceSupportsRgbLighting,
                legionManager.DeviceSupportsGyro,
                legionManager.DeviceHasScrollWheel,
                legionManager.DeviceHasDetachableControllers,
                legionManager.DeviceHasTouchpad,
                autoTDPManager.Enabled,
                autoTDPManager.TargetFPS,
                autoTDPManager.CurrentFPS,
                autoTDPManager.MinTDP,
                autoTDPManager.MaxTDP,
                autoTDPManager.TDPLimits,
                autoTDPManager.UseMLMode,
                autoTDPManager.ControllerType,  // 0=PID, 1=Q-Learning, 2=SARSA
                autoTDPManager.MLStatus,
                autoTDPManager.LearnedGameData,
                autoTDPManager.ResetML,
                autoTDPManager.PauseWhenUnfocused,
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
            if (settingsManager?.AutoHibernateEnabled != null)
            {
                settingsManager.AutoHibernateEnabled.PropertyChanged += AutoHibernateEnabled_PropertyChanged;
                SetAutoHibernateEnabled(settingsManager.AutoHibernateEnabled.Value);
            }
            if (settingsManager?.AutoHibernateIdleMinutes != null)
            {
                settingsManager.AutoHibernateIdleMinutes.PropertyChanged += AutoHibernateIdleMinutes_PropertyChanged;
                UpdateAutoHibernateIdleTimeout(settingsManager.AutoHibernateIdleMinutes.Value);
            }
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
                autoTDPManager.UseMLMode.PropertyChanged += AutoTDPSetting_PropertyChanged;  // Legacy
                autoTDPManager.ControllerType.PropertyChanged += AutoTDPSetting_PropertyChanged;
                autoTDPManager.PauseWhenUnfocused.PropertyChanged += AutoTDPSetting_PropertyChanged;
            }

            initTimer.Stop();
            Logger.Info($"[TIMING] Helper initialization (managers + properties): {initTimer.ElapsedMilliseconds}ms");

            // Mark managers as ready - BatchGet requests will now be processed
            // Pipe server was started earlier, widget may already be connected and waiting
            _managersReady = true;
            Logger.Info("Managers ready - BatchGet requests will now be processed");

            // Wait for widget connection (non-blocking if already connected)
            var connectTimer = System.Diagnostics.Stopwatch.StartNew();
            await WaitForWidgetConnection(true);
            connectTimer.Stop();
            Logger.Info($"[TIMING] Widget connection: {connectTimer.ElapsedMilliseconds}ms");

            // Start battery monitoring after pipe server is ready
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

        private static void TryStartTrayIndicator()
        {
            if (_isRunningAsService || _trayIndicator != null)
            {
                return;
            }

            try
            {
                _trayIndicator = new HelperTrayIndicator(OnTrayRestartRequested, OnTrayExitRequested);
                if (_trayIndicator.Start())
                {
                    Logger.Info("Tray indicator started");
                    return;
                }

                _trayIndicator.Dispose();
                _trayIndicator = null;
                Logger.Warn("Tray indicator failed to start");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Tray indicator startup failed: {ex.Message}");
                _trayIndicator = null;
            }
        }

        private static void DisposeTrayIndicator()
        {
            try
            {
                _trayIndicator?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Debug($"Tray indicator dispose failed: {ex.Message}");
            }
            finally
            {
                _trayIndicator = null;
            }
        }

        private static void OnTrayExitRequested()
        {
            if (_isShuttingDown)
            {
                return;
            }

            Logger.Info("Tray: Exit requested");
            _isShuttingDown = true;
            LogManager.Flush();
        }

        private static void OnTrayRestartRequested()
        {
            if (_isShuttingDown)
            {
                return;
            }

            Logger.Info("Tray: Restart requested");
            _restartInProgress = true;

            bool restartScriptStarted = TryRestartHelperProcess();
            if (!restartScriptStarted)
            {
                Logger.Warn("Tray restart script did not start; attempting scheduled task fallback");
                try
                {
                    Services.ScheduledTaskService.RunTaskNow();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Tray restart fallback via scheduled task failed");
                }
            }

            _isShuttingDown = true;
            LogManager.Flush();

            // Some helper components keep foreground threads alive briefly after the main loop exits.
            // Force process termination so the detached restart script can relaunch reliably.
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                if (_restartInProgress)
                {
                    Logger.Info("Tray restart: forcing helper process exit");
                    LogManager.Flush();
                    Environment.Exit(0);
                }
            });
        }

        private static bool TryRestartHelperProcess()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                string currentExePath = currentProcess.MainModule?.FileName;
                int currentPid = currentProcess.Id;

                if (string.IsNullOrWhiteSpace(currentExePath))
                {
                    Logger.Error($"Restart requested but helper executable path is invalid: '{currentExePath}'");
                    return false;
                }

                string escapedExePath = currentExePath.Replace("'", "''");
                string tempScriptPath = Path.Combine(Path.GetTempPath(), $"GoTweaksRestart_{currentPid}.ps1");
                string script = $@"$pidToWait = {currentPid}
$exePath = '{escapedExePath}'

while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {{
    Start-Sleep -Milliseconds 250
}}

try {{
    Start-Process -FilePath $exePath -ErrorAction Stop | Out-Null
}}
catch {{
    schtasks.exe /Run /TN 'GoTweaks\GoTweaksHelper' | Out-Null
}}

Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";

                File.WriteAllText(tempScriptPath, script);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                Logger.Info($"Restart request completed: helper relaunch script started ({tempScriptPath})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to relaunch helper after tray restart request");
                return false;
            }
        }

        private sealed class HelperTrayIndicator : IDisposable
        {
            private readonly Action _onRestartRequested;
            private readonly Action _onExitRequested;
            private readonly ManualResetEventSlim _startupSignal = new ManualResetEventSlim(false);
            private Thread _uiThread;
            private Exception _startupException;
            private uint _threadId;
            private bool _disposed;

            internal HelperTrayIndicator(Action onRestartRequested, Action onExitRequested)
            {
                _onRestartRequested = onRestartRequested;
                _onExitRequested = onExitRequested;
            }

            internal bool Start()
            {
                _uiThread = new Thread(TrayUiThreadMain)
                {
                    IsBackground = true,
                    Name = "GoTweaksHelperTray",
                };
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.Start();

                if (!_startupSignal.Wait(5000))
                {
                    _startupException = new System.TimeoutException("Timed out waiting for tray thread to initialize.");
                }

                return _startupException == null;
            }

            private void TrayUiThreadMain()
            {
                _threadId = GetCurrentThreadId();

                try
                {
                    using (var menu = new global::System.Windows.Forms.ContextMenuStrip())
                    {
                        var restartItem = new global::System.Windows.Forms.ToolStripMenuItem("Restart");
                        restartItem.Click += (sender, args) => _onRestartRequested?.Invoke();

                        var exitItem = new global::System.Windows.Forms.ToolStripMenuItem("Exit");
                        exitItem.Click += (sender, args) => _onExitRequested?.Invoke();

                        menu.Items.Add(restartItem);
                        menu.Items.Add(new global::System.Windows.Forms.ToolStripSeparator());
                        menu.Items.Add(exitItem);

                        using (var notifyIcon = new global::System.Windows.Forms.NotifyIcon())
                        {
                            notifyIcon.Text = "GoTweaks Helper";
                            notifyIcon.Icon = global::System.Drawing.SystemIcons.Application;
                            notifyIcon.ContextMenuStrip = menu;
                            notifyIcon.Visible = true;

                            _startupSignal.Set();
                            global::System.Windows.Forms.Application.Run();

                            notifyIcon.Visible = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _startupException = ex;
                    _startupSignal.Set();
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                try
                {
                    if (_threadId != 0)
                    {
                        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                catch
                {
                    // Ignore shutdown messaging failures.
                }

                try
                {
                    _uiThread?.Join(2000);
                }
                catch
                {
                    // Ignore join failures; process shutdown will clean up if needed.
                }

                _startupSignal.Dispose();
            }
        }

        private static void CPUState_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUState_PropertyChanged - already applying profile");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUState_PropertyChanged - in profile switch cooldown");
                return;
            }

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU State to Max={powerManager.MaxCPUState.Value}%, Min={powerManager.MinCPUState.Value}%.");
            profileManager.CurrentProfile.MaxCPUState = powerManager.MaxCPUState.Value;
            profileManager.CurrentProfile.MinCPUState = powerManager.MinCPUState.Value;
        }

        private static void SystemManager_ResumeFromSleep(object sender)
        {
            Logger.Info("System resumed from sleep/hibernation, refreshing hardware sensors and re-applying profile.");

            // Reset RTSS OSD connection (can become stale after hibernation, causing frozen OSD values)
            rtssManager?.ResetRTSSConnection();

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

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUBoost_PropertyChanged - in profile switch cooldown");
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

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUEPP_PropertyChanged - in profile switch cooldown");
                return;
            }

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU EPP from {profileManager.CurrentProfile.CPUEPP} to {powerManager.CPUEPP}.");
            profileManager.CurrentProfile.CPUEPP = powerManager.CPUEPP;
        }

        private static void AutoHibernateEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (settingsManager?.AutoHibernateEnabled == null) return;
            SetAutoHibernateEnabled(settingsManager.AutoHibernateEnabled.Value);
        }

        private static void AutoHibernateIdleMinutes_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (settingsManager?.AutoHibernateIdleMinutes == null) return;
            UpdateAutoHibernateIdleTimeout(settingsManager.AutoHibernateIdleMinutes.Value);
        }

        private static void LegionControllerSetting_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug("Skipping LegionControllerSetting_PropertyChanged - already applying profile");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug("Skipping LegionControllerSetting_PropertyChanged - in profile switch cooldown");
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
                // Also save directly to GlobalProfile to ensure it's always in sync
                // This fixes an issue where the restore reads from GlobalProfile but save goes to CurrentProfile
                if (profileManager.CurrentProfile.IsGlobalProfile)
                {
                    profileManager.GlobalProfile.LegionPerformanceMode = legionManager.LegionPerformanceMode.Value;
                    Logger.Debug($"Also saved LegionPerformanceMode to GlobalProfile directly: {legionManager.LegionPerformanceMode.Value}");
                }
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

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug("Skipping AutoTDPSetting_PropertyChanged - in profile switch cooldown");
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
            else if (sender == autoTDPManager.UseMLMode)
            {
                // Legacy: sync UseMLMode to profile for backwards compatibility
                Logger.Info($"Saving AutoTDPUseMLMode to profile {profileName}");
                profileManager.CurrentProfile.AutoTDPUseMLMode = autoTDPManager.UseMLMode.Value;
            }
            else if (sender == autoTDPManager.ControllerType)
            {
                Logger.Info($"Saving AutoTDPControllerType to profile {profileName}");
                profileManager.CurrentProfile.AutoTDPControllerType = autoTDPManager.ControllerType.Value;
                // Also sync legacy UseMLMode for backwards compatibility
                profileManager.CurrentProfile.AutoTDPUseMLMode = autoTDPManager.ControllerType.Value > 0;
            }
            else if (sender == autoTDPManager.PauseWhenUnfocused)
            {
                Logger.Info($"Saving AutoTDPPauseWhenUnfocused to profile {profileName}");
                profileManager.CurrentProfile.AutoTDPPauseWhenUnfocused = autoTDPManager.PauseWhenUnfocused.Value;
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
            // Apply explicit safe defaults when profile entries are missing so gyro never
            // inherits prior game/global values by accident.
            int legionGyroButton = profile.LegionGyroButton ?? 0;
            int legionGyroTarget = profile.LegionGyroTarget ?? 0;
            int legionGyroSensitivityX = profile.LegionGyroSensitivityX ?? 50;
            int legionGyroSensitivityY = profile.LegionGyroSensitivityY ?? 50;
            bool legionGyroInvertX = profile.LegionGyroInvertX ?? false;
            bool legionGyroInvertY = profile.LegionGyroInvertY ?? false;
            int legionGyroMappingType = profile.LegionGyroMappingType ?? 0;
            int legionGyroActivationMode = profile.LegionGyroActivationMode ?? 0;
            int legionGyroDeadzone = profile.LegionGyroDeadzone ?? 10;

            Logger.Debug($"Applying LegionGyroButton: {legionGyroButton}{(profile.LegionGyroButton.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroActivationButton.SetValue(legionGyroButton);

            Logger.Debug($"Applying LegionGyroTarget: {legionGyroTarget}{(profile.LegionGyroTarget.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroTarget.SetValue(legionGyroTarget);

            Logger.Debug($"Applying LegionGyroSensitivityX: {legionGyroSensitivityX}{(profile.LegionGyroSensitivityX.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroSensitivityX.SetValue(legionGyroSensitivityX);

            Logger.Debug($"Applying LegionGyroSensitivityY: {legionGyroSensitivityY}{(profile.LegionGyroSensitivityY.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroSensitivityY.SetValue(legionGyroSensitivityY);

            Logger.Debug($"Applying LegionGyroInvertX: {legionGyroInvertX}{(profile.LegionGyroInvertX.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroInvertX.SetValue(legionGyroInvertX);

            Logger.Debug($"Applying LegionGyroInvertY: {legionGyroInvertY}{(profile.LegionGyroInvertY.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroInvertY.SetValue(legionGyroInvertY);

            Logger.Debug($"Applying LegionGyroMappingType: {legionGyroMappingType}{(profile.LegionGyroMappingType.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroMappingType.SetValue(legionGyroMappingType);

            Logger.Debug($"Applying LegionGyroActivationMode: {legionGyroActivationMode}{(profile.LegionGyroActivationMode.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroActivationMode.SetValue(legionGyroActivationMode);

            Logger.Debug($"Applying LegionGyroDeadzone: {legionGyroDeadzone}{(profile.LegionGyroDeadzone.HasValue ? string.Empty : " (default)")}");
            legionManager.LegionGyroDeadzone.SetValue(legionGyroDeadzone);

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

            // Lighting settings - apply color, brightness, and speed BEFORE mode
            // to prevent flash to white when mode is applied with old/default color
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
            // Apply mode last so it uses the updated color/brightness/speed
            if (profile.LegionLightMode.HasValue)
            {
                Logger.Debug($"Applying LegionLightMode: {profile.LegionLightMode.Value}");
                legionManager.LegionLightMode.SetValue(profile.LegionLightMode.Value);
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
            // Use ForceSetValue for Enabled to ensure the pipe message is ALWAYS sent to the widget.
            // If the global profile was corrupted (AutoTDPEnabled=true from a previous bug),
            // SetValue would skip NotifyPropertyChanged when the value hasn't changed (e.g., game
            // profile had AutoTDP=true and corrupted global also has true), leaving the widget
            // with the wrong toggle state.
            Logger.Debug($"Applying AutoTDPEnabled: {profile.AutoTDPEnabled}");
            autoTDPManager.Enabled.ForceSetValue(profile.AutoTDPEnabled);

            Logger.Debug($"Applying AutoTDPTargetFPS: {profile.AutoTDPTargetFPS}");
            autoTDPManager.TargetFPS.SetValue(profile.AutoTDPTargetFPS);

            Logger.Debug($"Applying AutoTDPMinTDP: {profile.AutoTDPMinTDP}");
            autoTDPManager.MinTDP.SetValue(profile.AutoTDPMinTDP);

            Logger.Debug($"Applying AutoTDPMaxTDP: {profile.AutoTDPMaxTDP}");
            autoTDPManager.MaxTDP.SetValue(profile.AutoTDPMaxTDP);

            Logger.Debug($"Applying AutoTDPPauseWhenUnfocused: {profile.AutoTDPPauseWhenUnfocused}");
            autoTDPManager.PauseWhenUnfocused.SetValue(profile.AutoTDPPauseWhenUnfocused);

            // Apply controller type (0=PID, 1=Q-Learning, 2=SARSA)
            // Try new property first, fall back to legacy UseMLMode for migration
            int controllerType = profile.AutoTDPControllerType;
            if (controllerType == 0 && profile.AutoTDPUseMLMode)
            {
                // Legacy migration: UseMLMode=true -> Q-Learning (1)
                controllerType = 1;
            }
            Logger.Debug($"Applying AutoTDPControllerType: {controllerType} (PID=0, Q-Learning=1, SARSA=2)");
            autoTDPManager.ControllerType.SetValue(controllerType);
            autoTDPManager.UseMLMode.SetValue(controllerType > 0);  // Legacy sync
        }

        /// <summary>
        /// Restores global profile settings (TDP, AutoTDP, Legion mode, etc.)
        /// Called when transitioning away from a per-game profile:
        /// - Game stops (RunningGame becomes invalid)
        /// - Game changes from per-game profile game to non-per-game-profile game
        /// - Per-game profile is explicitly disabled by widget
        /// Must be called within isApplyingProfile = true context.
        /// </summary>
        private static void RestoreGlobalProfileSettings()
        {
            // Refresh GlobalProfile from cache — property change handlers (TDP_PropertyChanged, etc.)
            // update CurrentProfile (a struct copy), which saves to cache/disk, but the
            // GlobalProfile field stays stale since GameProfile is a struct.
            profileManager.RefreshGlobalProfile();

            profileManager.CurrentProfile.SetValue(profileManager.GlobalProfile);

            Logger.Info($"Applying global profile settings: TDP={profileManager.GlobalProfile.TDP}, CPUBoost={profileManager.GlobalProfile.CPUBoost}, EPP={profileManager.GlobalProfile.CPUEPP}");

            // IMPORTANT: Disable AutoTDP FIRST, before setting TDP.
            // TDPProperty.NotifyPropertyChanged() skips hardware apply when IsAutoTDPActive is true.
            // If we set TDP while AutoTDP is still active, the TDP value never gets applied to hardware.
            ApplyAutoTDPSettingsFromProfile();
            // Clear the AutoTDP active flag immediately so the TDP set below applies to hardware.
            // The AutoTDP tick will also clear this on its next iteration, but we can't wait for that.
            performanceManager.IsAutoTDPActive = false;

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

                        // Apply AutoTDP settings FIRST, before TDP.
                        // TDPProperty.NotifyPropertyChanged() skips hardware apply when IsAutoTDPActive is true.
                        // Applying AutoTDP first ensures IsAutoTDPActive is cleared when disabling AutoTDP,
                        // so the subsequent TDP.SetProfileValue applies to hardware.
                        ApplyAutoTDPSettingsFromProfile();
                        performanceManager.IsAutoTDPActive = false;

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
                    }
                    finally
                    {
                        profileSwitchTime = DateTime.UtcNow;
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
                    // Don't enable per-game profile if there's no valid game running
                    // This prevents race conditions when game closes and stale PerGameProfile=true arrives
                    if (!systemManager.RunningGame.Value.IsValid())
                    {
                        Logger.Info("Ignoring PerGameProfile=true - no valid game running (stale message)");
                        return;
                    }

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

                    // Apply saved LegionPerformanceMode from game profile, or default to Custom (255) for new profiles.
                    // Previously this always switched to Custom, which overrode user-saved preset modes.
                    if (legionManager != null)
                    {
                        int? savedMode = gameProfile.LegionPerformanceMode;
                        if (savedMode.HasValue && savedMode.Value > 0)
                        {
                            if (legionManager.LegionPerformanceMode.Value != savedMode.Value)
                            {
                                Logger.Info($"Applying saved performance mode ({savedMode.Value}) for per-game profile '{systemManager.RunningGame.Value.GameId.Name}'");
                                legionManager.LegionPerformanceMode.SetValue(savedMode.Value);
                            }
                            else
                            {
                                Logger.Debug($"Per-game profile already in saved mode ({savedMode.Value})");
                            }
                        }
                        else if (legionManager.LegionPerformanceMode.Value != 255)
                        {
                            Logger.Info("Switching to Custom TDP mode for new per-game profile (no saved mode)");
                            legionManager.LegionPerformanceMode.SetValue(255);
                        }
                    }

                    // Set current profile and apply settings from per-game profile.
                    // CurrentProfile_PropertyChanged is blocked by isApplyingProfile, so we
                    // must apply settings explicitly here (same pattern as RestoreGlobalProfileSettings).
                    profileManager.CurrentProfile.SetValue(gameProfile);

                    ApplyAutoTDPSettingsFromProfile();
                    performanceManager.IsAutoTDPActive = false;

                    performanceManager.TDP.SetProfileValue(gameProfile.TDP);
                    performanceManager.TDPBoostEnabled.SetValue(gameProfile.TDPBoostEnabled);
                    powerManager.CPUBoost.SetValue(gameProfile.CPUBoost);
                    powerManager.CPUEPP.SetValue(gameProfile.CPUEPP);
                    powerManager.MaxCPUState.SetValue(gameProfile.MaxCPUState);
                    powerManager.MinCPUState.SetValue(gameProfile.MinCPUState);
                }
                else
                {
                    // Don't disable per-game profile if a game with an active profile is still running
                    // This prevents race conditions when widget sends stale PerGameProfile=false
                    if (systemManager.RunningGame.Value.IsValid())
                    {
                        // Check if the current profile matches the running game (or a similar name variant)
                        var currentProfile = profileManager.CurrentProfile;
                        if (currentProfile != null && currentProfile != profileManager.GlobalProfile && currentProfile.Use)
                        {
                            var runningGameName = systemManager.RunningGame.Value.GameId.Name ?? "";
                            var profileName = currentProfile.GameId.Name ?? "";

                            // Check for exact match or name variants (e.g., "Game: Title" vs "Game Title")
                            bool isSameGame = string.Equals(runningGameName, profileName, StringComparison.OrdinalIgnoreCase) ||
                                              runningGameName.Replace(":", "").Replace("  ", " ").Trim().Equals(
                                                  profileName.Replace(":", "").Replace("  ", " ").Trim(),
                                                  StringComparison.OrdinalIgnoreCase);

                            if (isSameGame)
                            {
                                // Only ignore if this is likely a stale message from a recent game
                                // transition (within 2 seconds). Otherwise, honor the user's explicit
                                // toggle and restore global settings.
                                double secondsSinceSwitch = (DateTime.UtcNow - profileSwitchTime).TotalSeconds;
                                if (secondsSinceSwitch < 2)
                                {
                                    Logger.Info($"Ignoring stale PerGameProfile=false - game '{runningGameName}' still running, recent switch {secondsSinceSwitch:F1}s ago");
                                    return;
                                }
                                Logger.Info($"Honoring PerGameProfile=false while game '{runningGameName}' is running (user toggle, {secondsSinceSwitch:F1}s since last switch)");
                                // Fall through to disable per-game profile and restore global settings
                            }
                        }
                    }

                    if (profileManager.TryGetProfile(systemManager.RunningGame.Value.GameId, out gameProfile))
                    {
                        gameProfile.Use = false;
                    }
                    // Restore global profile and apply all its settings (TDP, AutoTDP, etc.)
                    // CurrentProfile_PropertyChanged is blocked by isApplyingProfile, so we
                    // must apply settings explicitly here
                    RestoreGlobalProfileSettings();
                }
            }
            finally
            {
                profileSwitchTime = DateTime.UtcNow;
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

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping TDP_PropertyChanged - in profile switch cooldown (TDP={performanceManager.TDP})");
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

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping TDPBoostEnabled_PropertyChanged - in profile switch cooldown");
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
                    bool gameHasActiveProfile = false;
                    if (profileManager.TryGetProfile(systemManager.RunningGame.Value.GameId, out var runningGameProfile))
                    {
                        if (runningGameProfile.Use)
                        {
                            Logger.Info($"Game {systemManager.RunningGame.GameId} has per-game profile in use.");
                            profileManager.CurrentProfile.SetValue(runningGameProfile);
                            gameHasActiveProfile = true;

                            // Notify widget that per-game profile is active.
                            // The widget may not auto-enable (e.g., disabled preference stored locally),
                            // so the helper must assert this to keep both sides in sync.
                            profileManager.PerGameProfile.ForceSetValue(true);

                            // Apply all settings explicitly. CurrentProfile_PropertyChanged is blocked
                            // by isApplyingProfile, so we must apply here (same as PerGameProfile_PropertyChanged).
                            if (legionManager != null)
                            {
                                int? savedMode = runningGameProfile.LegionPerformanceMode;
                                if (savedMode.HasValue && savedMode.Value > 0)
                                {
                                    if (legionManager.LegionPerformanceMode.Value != savedMode.Value)
                                    {
                                        Logger.Info($"Applying saved performance mode ({savedMode.Value}) for game '{systemManager.RunningGame.Value.GameId.Name}'");
                                        legionManager.LegionPerformanceMode.SetValue(savedMode.Value);
                                    }
                                }
                                else if (legionManager.LegionPerformanceMode.Value != 255)
                                {
                                    Logger.Info("Switching to Custom TDP mode for game profile (no saved mode)");
                                    legionManager.LegionPerformanceMode.SetValue(255);
                                }
                            }

                            ApplyAutoTDPSettingsFromProfile();
                            performanceManager.IsAutoTDPActive = false;

                            performanceManager.TDP.SetProfileValue(runningGameProfile.TDP);
                            performanceManager.TDPBoostEnabled.SetValue(runningGameProfile.TDPBoostEnabled);
                            powerManager.CPUBoost.SetValue(runningGameProfile.CPUBoost);
                            powerManager.CPUEPP.SetValue(runningGameProfile.CPUEPP);
                            powerManager.MaxCPUState.SetValue(runningGameProfile.MaxCPUState);
                            powerManager.MinCPUState.SetValue(runningGameProfile.MinCPUState);

                            if (legionManager != null)
                            {
                                ApplyLegionControllerSettingsFromProfile();
                            }

                            Logger.Info($"Applied per-game profile settings for {systemManager.RunningGame.Value.GameId.Name}: TDP={runningGameProfile.TDP}, AutoTDP={runningGameProfile.AutoTDPEnabled}");
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

                    // Only restore global if the current game doesn't have an active profile
                    // AND we're currently on a per-game profile from a previous game.
                    // Without the gameHasActiveProfile check, re-firing for the SAME game
                    // (e.g., foreground change) would incorrectly restore global and cause
                    // per-game AutoTDP settings to bleed into the global profile.
                    if (!gameHasActiveProfile && !profileManager.CurrentProfile.IsGlobalProfile)
                    {
                        Logger.Info($"Previous game had per-game profile active, restoring global profile for {systemManager.RunningGame.GameId}");
                        RestoreGlobalProfileSettings();
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
                    RestoreGlobalProfileSettings();

                    // Reset Lossless Scaling to Default profile when game stops
                    if (losslessScalingManager.LosslessScalingInstalled.Value)
                    {
                        losslessScalingManager.SetCurrentGame("Default", "");
                    }
                }
            }
            finally
            {
                profileSwitchTime = DateTime.UtcNow;
                isApplyingProfile = false;
            }
        }

        // NOTE: Connection_RequestReceived (AppService handler) was removed - using Named Pipes only
        // See PipeServer_MessageReceived below for the pipe-based message handler

        // The following code was Connection_RequestReceived content - it has been removed
        // since we now use PipeServer_MessageReceived for all widget communication


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
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle keyboard shortcut request
                if (pipeMsg.Extra.TryGetValue("SendKeyboardShortcut", out object shortcutValue) && shortcutValue is string shortcutStr)
                {
                    Logger.Info($"Sending keyboard shortcut via InputInjector: {shortcutStr}");
                    SendKeyboardShortcutViaInputInjector(shortcutStr);
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle close game request
                if (pipeMsg.Extra.ContainsKey("CloseGame"))
                {
                    Logger.Info("CloseGame request received - attempting to close foreground window");
                    bool success = Windows.User32.CloseForegroundWindow();
                    Logger.Info($"CloseGame result: {success}");
                    SendPipeAck(pipeMsg.RequestId, success);
                    return;
                }

                // Handle touch keyboard toggle request
                if (pipeMsg.Extra.ContainsKey("ToggleTouchKeyboard"))
                {
                    try
                    {
                        Logger.Info("Pipe: ToggleTouchKeyboard request received");
                        TouchKeyboardHelper.Toggle();
                        Logger.Info("Pipe: Touch keyboard toggled");
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to toggle touch keyboard: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle LaunchUrl request (for Donate button, etc.)
                if (pipeMsg.Extra.TryGetValue("LaunchUrl", out object urlValue) && urlValue is string url)
                {
                    try
                    {
                        Logger.Info($"Pipe: LaunchUrl request received: {url}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                        Logger.Info("Pipe: URL launched successfully");
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to launch URL: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle hibernate request
                if (pipeMsg.Extra.ContainsKey("Hibernate"))
                {
                    try
                    {
                        Logger.Info("Pipe: Hibernate request received - initiating system hibernation");
                        bool success = Windows.PowrProf.SetSuspendState(
                            bHibernate: true,      // Hibernate, not sleep
                            bForce: false,         // Don't force - let apps save work
                            bWakeupEventsDisabled: false);  // Allow wake events

                        if (success)
                        {
                            Logger.Info("Pipe: Hibernate initiated successfully");
                        }
                        else
                        {
                            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                            Logger.Error($"Pipe: Failed to initiate hibernate, Win32 error: {error}");
                        }
                        SendPipeAck(pipeMsg.RequestId, success);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to hibernate: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
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

                // Handle export profiles request
                if (pipeMsg.Extra.ContainsKey("ExportProfiles"))
                {
                    Logger.Info("Pipe: ExportProfiles request received");
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var exportPath = Path.Combine(desktopPath, $"GoTweaks_Profiles_{timestamp}.xml");

                        // Collect all profiles for export
                        var gameProfilesList = new List<Shared.Data.GameProfile>();
                        foreach (var kvp in profileManager.GameProfiles)
                        {
                            gameProfilesList.Add(kvp.Value);
                        }

                        // Get app version from assembly
                        var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

                        // Parse global widget settings from message (sent by widget as serialized GlobalWidgetSettings)
                        Shared.Data.GlobalWidgetSettings globalSettings = null;
                        if (pipeMsg.Extra.TryGetValue("GlobalSettings", out object gsXml) && gsXml is string gsXmlStr)
                        {
                            try
                            {
                                // Deserialize the GlobalWidgetSettings directly
                                globalSettings = Shared.Utilities.XmlHelper.FromXMLString<Shared.Data.GlobalWidgetSettings>(gsXmlStr);
                                if (globalSettings != null)
                                {
                                    Logger.Info("Pipe: Global widget settings included in export");
                                    Logger.Info($"  TDP Limits: Min={globalSettings.DeviceTDPMin}, Max={globalSettings.DeviceTDPMax}");
                                }
                            }
                            catch (Exception gsEx)
                            {
                                Logger.Warn($"Pipe: Failed to parse global settings: {gsEx.Message}");
                            }
                        }

                        // Create export container
                        var export = new Shared.Data.ProfileExport(
                            profileManager.GlobalProfile,
                            gameProfilesList,
                            appVersion,
                            globalSettings
                        );

                        // Serialize to XML file
                        if (Shared.Utilities.XmlHelper.ToXMLFile(export, exportPath))
                        {
                            Logger.Info($"Pipe: Profiles exported to: {exportPath}");
                            Logger.Info($"  Global profile + {gameProfilesList.Count} game profile(s) exported");
                            if (globalSettings != null)
                                Logger.Info("  Global widget settings (Legion buttons, scroll wheel, TDP limits, OSD) included");
                            response.Add("Success", true);
                            response.Add("Path", exportPath);
                            response.Add("ProfileCount", gameProfilesList.Count + 1); // +1 for global
                        }
                        else
                        {
                            throw new Exception("Failed to write XML file");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to export profiles: {ex.Message}");
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

                // Handle import profiles request
                if (pipeMsg.Extra.ContainsKey("ImportProfiles"))
                {
                    Logger.Info("Pipe: ImportProfiles request received");
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        // Get import path from message
                        var importPath = pipeMsg.Extra["ImportProfiles"] as string;
                        if (string.IsNullOrEmpty(importPath) || !File.Exists(importPath))
                        {
                            throw new Exception($"Import file not found: {importPath}");
                        }

                        Logger.Info($"Pipe: Importing profiles from: {importPath}");

                        // Deserialize the export file
                        var import = Shared.Utilities.XmlHelper.FromXMLFile<Shared.Data.ProfileExport>(importPath);
                        if (import == null)
                        {
                            throw new Exception("Failed to parse import file");
                        }

                        Logger.Info($"Pipe: Import file version {import.Version}, created {import.ExportDate} by app v{import.AppVersion}");

                        int importedCount = 0;
                        int skippedCount = 0;

                        // Import global profile settings (merge into existing global)
                        var globalProfile = profileManager.GlobalProfile;
                        globalProfile.TDP = import.GlobalProfile.TDP;
                        globalProfile.CPUBoost = import.GlobalProfile.CPUBoost;
                        globalProfile.CPUEPP = import.GlobalProfile.CPUEPP;
                        globalProfile.MaxCPUState = import.GlobalProfile.MaxCPUState;
                        globalProfile.MinCPUState = import.GlobalProfile.MinCPUState;
                        globalProfile.TDPBoostEnabled = import.GlobalProfile.TDPBoostEnabled;
                        globalProfile.TDP_DC = import.GlobalProfile.TDP_DC;
                        globalProfile.CPUBoost_DC = import.GlobalProfile.CPUBoost_DC;
                        globalProfile.CPUEPP_DC = import.GlobalProfile.CPUEPP_DC;
                        globalProfile.MaxCPUState_DC = import.GlobalProfile.MaxCPUState_DC;
                        globalProfile.MinCPUState_DC = import.GlobalProfile.MinCPUState_DC;
                        globalProfile.FPSLimit = import.GlobalProfile.FPSLimit;
                        globalProfile.FPSLimit_DC = import.GlobalProfile.FPSLimit_DC;
                        globalProfile.OSPowerMode = import.GlobalProfile.OSPowerMode;
                        globalProfile.OSPowerMode_DC = import.GlobalProfile.OSPowerMode_DC;
                        Logger.Info("Global profile settings imported");
                        importedCount++;

                        // Import game profiles
                        var profilesFolder = Profile.ProfileManager.GetGameProfilesFolder();
                        foreach (var gameProfile in import.GameProfiles)
                        {
                            try
                            {
                                // Generate profile path from game executable name
                                var exeName = Path.GetFileNameWithoutExtension(gameProfile.GameId.Path);
                                if (string.IsNullOrEmpty(exeName))
                                {
                                    Logger.Warn($"Skipping profile with empty path: {gameProfile.GameId.Name}");
                                    skippedCount++;
                                    continue;
                                }

                                var profilePath = Path.Combine(profilesFolder, $"{exeName}.xml");

                                // Create a copy with the correct path set
                                var importedProfile = gameProfile;
                                importedProfile.Path = profilePath;

                                // Serialize directly to file
                                if (Shared.Utilities.XmlHelper.ToXMLFile(importedProfile, profilePath))
                                {
                                    Logger.Info($"Imported profile for: {gameProfile.GameId.Name}");
                                    importedCount++;
                                }
                                else
                                {
                                    Logger.Warn($"Failed to save profile for: {gameProfile.GameId.Name}");
                                    skippedCount++;
                                }
                            }
                            catch (Exception profileEx)
                            {
                                Logger.Warn($"Failed to import profile {gameProfile.GameId.Name}: {profileEx.Message}");
                                skippedCount++;
                            }
                        }

                        Logger.Info($"Pipe: Import complete - {importedCount} profile(s) imported, {skippedCount} skipped");
                        response.Add("Success", true);
                        response.Add("ImportedCount", importedCount);
                        response.Add("SkippedCount", skippedCount);
                        response.Add("Message", $"Imported {importedCount} profile(s). Restart the helper to load imported game profiles.");

                        // Return global widget settings to widget for restoration
                        if (import.GlobalSettings != null)
                        {
                            try
                            {
                                var globalSettingsXml = Shared.Utilities.XmlHelper.ToXMLString(import.GlobalSettings, true);
                                response.Add("GlobalSettings", globalSettingsXml);
                                Logger.Info("Pipe: Global widget settings returned to widget for restoration");
                            }
                            catch (Exception gsEx)
                            {
                                Logger.Warn($"Pipe: Failed to serialize global settings for response: {gsEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to import profiles: {ex.Message}");
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
                    Logger.Info("Pipe: ExitHelper request received - shutting down helper for restart");
                    LogManager.Flush(); // Ensure log is written before we start shutdown
                    SendPipeAck(pipeMsg.RequestId);
                    _isShuttingDown = true;
                    // Main loop will exit, Initialize() returns, finally block releases mutex naturally
                    // No Environment.Exit needed - process will exit cleanly via Main() return
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
                        LogManager.Flush(); // Ensure log is written before we start shutdown
                        SendPipeAck(pipeMsg.RequestId);

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
                                LogManager.Flush(); // Ensure log is written before exit

                                // Release mutex before exiting to ensure clean restart
                                try
                                {
                                    singleInstanceMutex?.ReleaseMutex();
                                    singleInstanceMutex?.Dispose();
                                }
                                catch { /* Ignore mutex errors during shutdown */ }

                                Environment.Exit(0);
                            }
                        });
                    }
                    else
                    {
                        Logger.Warn("Pipe: UpgradeHelper request missing source path");
                        SendPipeAck(pipeMsg.RequestId, false);
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
                    response.Add("Success", success);
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
                    response.Add("Success", success);
                    Logger.Info($"Pipe: Scroll {direction} Remap - Enabled: {enabled}, Success: {success}");
                }
                // Controller Hotkey Config: Receive hotkey settings from widget for XInput monitoring
                else if (functionValue == (int)Function.ControllerHotkeyConfig)
                {
                    if (request.Content != null)
                    {
                        string configJson = request.Content.ToString();
                        ApplyControllerHotkeyConfig(configJson);
                    }
                }
                // Quick Metrics: Enable/disable metrics push timer
                else if (functionValue == (int)Function.QuickMetricsEnabled)
                {
                    if (request.Content != null && performanceManager != null)
                    {
                        bool enabled = false;
                        if (bool.TryParse(request.Content.ToString(), out enabled) || request.Content.ToString() == "True" || request.Content.ToString() == "true")
                        {
                            enabled = request.Content.ToString().ToLower() == "true" || request.Content.ToString() == "True";
                        }
                        performanceManager.QuickMetricsEnabled = enabled;
                        Logger.Info($"Pipe: Quick Metrics enabled set to: {enabled}");
                    }
                }
                // Screen Saver: Enable/disable idle-triggered screen saver
                else if (functionValue == (int)Function.ScreenSaverEnabled)
                {
                    if (request.Content != null)
                    {
                        bool enabled = request.Content.ToString().ToLower() == "true";
                        SetScreenSaverEnabled(enabled);
                        Logger.Info($"Pipe: Screen Saver enabled set to: {enabled}");
                    }
                }
                // Auto Hibernate Mode: 0=Always, 1=AC Only, 2=DC Only
                else if (functionValue == (int)Function.AutoHibernateMode)
                {
                    if (request.Content != null && int.TryParse(request.Content.ToString(), out int mode))
                    {
                        autoHibernateMode = mode;
                        Logger.Info($"Pipe: Auto Hibernate mode set to: {mode} ({(mode == 0 ? "Always" : mode == 1 ? "AC Only" : "DC Only")})");
                    }
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
                // HidHide: Check installed status
                else if (functionValue == (int)Function.HidHideInstalled)
                {
                    bool installed = XboxGamingBarHelper.Labs.HidHideHelper.IsInstalled();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", installed);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: HidHide installed status: {installed}");
                }
                // HidHide: Install request - only install when explicitly requested with Set command and "install" content
                else if (functionValue == (int)Function.InstallHidHide)
                {
                    bool shouldInstall = request.Command == Shared.Enums.Command.Set && request.Content == "install";

                    if (!shouldInstall)
                    {
                        Logger.Debug("Pipe: InstallHidHide - Ignoring non-install request (Get or empty content)");
                        return;
                    }

                    Logger.Info("Pipe: HidHide installation requested from widget");
                    _ = Task.Run(() =>
                    {
                        bool success = XboxGamingBarHelper.Labs.HidHideHelper.Install();
                        bool installed = XboxGamingBarHelper.Labs.HidHideHelper.IsInstalled();
                        var updateMsg = new Shared.IPC.PipeMessage
                        {
                            Command = Shared.Enums.Command.Set,
                            Function = Function.HidHideInstalled,
                            Content = installed.ToString()
                        };
                        SendPipeMessage(updateMsg);
                        Logger.Info($"Pipe: HidHide installation complete (success={success}), sent updated status: {installed}");
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
                // Debug: Export Per-Game Profiles
                else if (functionValue == (int)Function.Debug_ExportProfiles)
                {
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        string exportPath = ExportProfiles();
                        response.Add("ExportPath", exportPath);
                        Logger.Info($"Pipe: Profiles exported to {exportPath}");
                    }
                    catch (Exception ex)
                    {
                        response.Add("Error", ex.Message);
                        Logger.Error($"Pipe: Failed to export profiles: {ex.Message}");
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
                            LogManager.Flush(); // Ensure log is written before exit

                            // Release mutex before exiting to ensure clean restart
                            try
                            {
                                singleInstanceMutex?.ReleaseMutex();
                                singleInstanceMutex?.Dispose();
                            }
                            catch { /* Ignore mutex errors during shutdown */ }

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
                // System Restore: Prepare for Uninstall
                else if (functionValue == (int)Function.PrepareForUninstall)
                {
                    Logger.Info("Pipe: PrepareForUninstall request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        string result = Services.SystemRestoreService.PrepareForUninstall();
                        response.Add(nameof(Function), functionValue);
                        response.Add("Content", result);
                        response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                        Logger.Info("Pipe: PrepareForUninstall completed successfully");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: PrepareForUninstall failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                // System Restore: Get status of saved original values
                else if (functionValue == (int)Function.SystemRestoreStatus)
                {
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    string status = Services.SystemRestoreService.GetSavedValuesStatus();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", status);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: SystemRestoreStatus requested");
                }
                // Export All Data (comprehensive backup)
                else if (functionValue == (int)Function.ExportAllData)
                {
                    Logger.Info("Pipe: ExportAllData request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        // Widget settings may be passed in Content
                        string widgetSettings = request.Content;
                        string exportPath = ExportAllData(widgetSettings);
                        response.Add(nameof(Function), functionValue);
                        response.Add("Content", exportPath);
                        response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                        Logger.Info($"Pipe: ExportAllData completed: {exportPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: ExportAllData failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                // Import All Data (restore from backup)
                else if (functionValue == (int)Function.ImportAllData)
                {
                    Logger.Info("Pipe: ImportAllData request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        string importPath = request.Content;
                        if (string.IsNullOrEmpty(importPath))
                        {
                            response.Add("Content", "Error: No import path provided");
                        }
                        else
                        {
                            var (summary, widgetSettings) = ImportAllData(importPath);
                            response.Add(nameof(Function), functionValue);
                            response.Add("Content", summary);
                            if (!string.IsNullOrEmpty(widgetSettings))
                            {
                                response.Add("WidgetSettings", widgetSettings);
                            }
                            response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                            Logger.Info($"Pipe: ImportAllData completed from {importPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: ImportAllData failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                // PawnIO Debug: Get CPU Info
                else if (functionValue == (int)Function.PawnIOGetCpuInfo)
                {
                    Logger.Info("Pipe: PawnIOGetCpuInfo request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        string cpuInfo = performanceManager?.GetPawnIOCpuInfo() ?? "PerformanceManager not initialized";
                        response.Add("Content", cpuInfo);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: PawnIOGetCpuInfo failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                // PawnIO Debug: Apply Settings
                else if (functionValue == (int)Function.PawnIOApplySettings)
                {
                    Logger.Info("Pipe: PawnIOApplySettings request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        int coAll = 0, coGfx = 0, gfxClk = 0, tctlTemp = 0;
                        var valueSet = request.ToValueSet();
                        if (valueSet.TryGetValue("CoAll", out object coAllObj)) coAll = Convert.ToInt32(coAllObj);
                        if (valueSet.TryGetValue("CoGfx", out object coGfxObj)) coGfx = Convert.ToInt32(coGfxObj);
                        if (valueSet.TryGetValue("GfxClk", out object gfxClkObj)) gfxClk = Convert.ToInt32(gfxClkObj);
                        if (valueSet.TryGetValue("TctlTemp", out object tctlObj)) tctlTemp = Convert.ToInt32(tctlObj);

                        Logger.Info($"PawnIO Apply: CoAll={coAll}, CoGfx={coGfx}, GfxClk={gfxClk}, Tctl={tctlTemp}");
                        string result = performanceManager?.ApplyPawnIODebugSettings(coAll, coGfx, gfxClk, tctlTemp)
                            ?? "PerformanceManager not initialized";
                        response.Add("Content", result);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: PawnIOApplySettings failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
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
        /// If managers aren't ready yet, returns a NotReady response so widget can retry.
        /// </summary>
        private static async Task HandleBatchGetRequestViaPipe(Shared.IPC.PipeMessage request)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Check if managers are ready - if not, tell widget to wait and retry
                if (!_managersReady)
                {
                    Logger.Info("BatchGet request received but managers not ready yet - sending NotReady response");
                    var notReadyResponse = new Shared.IPC.PipeMessage
                    {
                        Command = Shared.Enums.Command.BatchGet,
                        RequestId = request.RequestId,
                        Extra = new Dictionary<string, object>
                        {
                            ["NotReady"] = true,
                            ["Message"] = "Helper managers are still initializing, please retry"
                        }
                    };
                    pipeServer?.SendMessage(notReadyResponse.ToJson());
                    return;
                }

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
                // Skip DefaultGameProfile properties - they're managed by ResyncCurrentState
                // Including them in batch causes race condition where stale values overwrite resync
                var helperManagedProperties = new HashSet<Shared.Enums.Function>
                {
                    Shared.Enums.Function.DefaultGameProfileAvailable,
                    Shared.Enums.Function.DefaultGameProfileData,
                    Shared.Enums.Function.DefaultGameProfileEnabled
                };

                var batchData = new Dictionary<string, object>();
                foreach (var funcId in functionIds)
                {
                    var func = (Shared.Enums.Function)funcId;

                    // Skip helper-managed properties that are resynced separately
                    if (helperManagedProperties.Contains(func))
                    {
                        Logger.Debug($"BatchGet: Skipping {func} - managed by ResyncCurrentState");
                        continue;
                    }

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

                // After batch sync, resync DefaultGameProfile state to fix race condition
                // where widget may have received stale ProfileAvailable=false during sync
                defaultGameProfileManager?.ResyncCurrentState();
            }
            catch (Exception ex)
            {
                Logger.Error($"BatchGet via pipe failed: {ex.Message}");
            }
        }


        /// <summary>
        /// Sends a simple acknowledgment response to the widget via Named Pipe.
        /// Used for fire-and-forget messages that still need a response to avoid timeout.
        /// </summary>
        private static void SendPipeAck(int requestId, bool success = true)
        {
            try
            {
                if (pipeServer != null && pipeServer.IsConnected && requestId > 0)
                {
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Success", success);
                    var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                    responseMsg.RequestId = requestId;
                    pipeServer.SendMessage(responseMsg.ToJson());
                    Logger.Debug($"Sent pipe ack for RequestId={requestId}, Success={success}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send pipe ack: {ex.Message}");
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
                var mainKeys = new List<ushort>(); // Support multiple non-modifier keys

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    ushort vk = 0;

                    if (upper == "CTRL" || upper == "CONTROL" || upper == "LCTRL" || upper == "LCONTROL")
                        vk = (ushort)VirtualKey.LeftControl;
                    else if (upper == "RCTRL" || upper == "RCONTROL")
                        vk = (ushort)VirtualKey.RightControl;
                    else if (upper == "ALT" || upper == "LALT")
                        vk = (ushort)VirtualKey.LeftMenu;
                    else if (upper == "RALT")
                        vk = (ushort)VirtualKey.RightMenu;
                    else if (upper == "SHIFT" || upper == "LSHIFT")
                        vk = (ushort)VirtualKey.LeftShift;
                    else if (upper == "RSHIFT")
                        vk = (ushort)VirtualKey.RightShift;
                    else if (upper == "WIN" || upper == "WINDOWS" || upper == "LWIN" || upper == "LMETA" || upper == "META")
                        vk = (ushort)VirtualKey.LeftWindows;
                    else if (upper == "RWIN" || upper == "RMETA")
                        vk = (ushort)VirtualKey.RightWindows;
                    else if (upper == "TAB")
                        vk = (ushort)VirtualKey.Tab;
                    else if (upper == "ENTER" || upper == "RETURN")
                        vk = (ushort)VirtualKey.Enter;
                    else if (upper == "ESCAPE" || upper == "ESC")
                        vk = (ushort)VirtualKey.Escape;
                    else if (upper == "SPACE")
                        vk = (ushort)VirtualKey.Space;
                    else if (upper == "BACKSPACE" || upper == "BACK")
                        vk = (ushort)VirtualKey.Back;
                    else if (upper == "DELETE" || upper == "DEL")
                        vk = (ushort)VirtualKey.Delete;
                    else if (upper == "HOME")
                        vk = (ushort)VirtualKey.Home;
                    else if (upper == "END")
                        vk = (ushort)VirtualKey.End;
                    else if (upper == "PGUP" || upper == "PAGEUP")
                        vk = (ushort)VirtualKey.PageUp;
                    else if (upper == "PGDN" || upper == "PAGEDOWN")
                        vk = (ushort)VirtualKey.PageDown;
                    else if (upper == "INSERT" || upper == "INS")
                        vk = (ushort)VirtualKey.Insert;
                    else if (upper == "UP")
                        vk = (ushort)VirtualKey.Up;
                    else if (upper == "DOWN")
                        vk = (ushort)VirtualKey.Down;
                    else if (upper == "LEFT")
                        vk = (ushort)VirtualKey.Left;
                    else if (upper == "RIGHT")
                        vk = (ushort)VirtualKey.Right;
                    else if (upper == "PAUSE")
                        vk = (ushort)VirtualKey.Pause;
                    else if (upper == "PRINTSCREEN" || upper == "PRTSC")
                        vk = (ushort)VirtualKey.Snapshot;
                    else if (upper == "VOLUME_UP" || upper == "VOLUMEUP")
                        vk = 0xAF; // VK_VOLUME_UP
                    else if (upper == "VOLUME_DOWN" || upper == "VOLUMEDOWN")
                        vk = 0xAE; // VK_VOLUME_DOWN
                    else if (upper == "VOLUME_MUTE" || upper == "VOLUMEMUTE" || upper == "MUTE")
                        vk = 0xAD; // VK_VOLUME_MUTE
                    else if (trimmed == "[" || upper == "LEFTBRACKET")
                        vk = 0xDB; // VK_OEM_4 (left bracket)
                    else if (trimmed == "]" || upper == "RIGHTBRACKET")
                        vk = 0xDD; // VK_OEM_6 (right bracket)
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
                    if (upper == "CTRL" || upper == "CONTROL" || upper == "LCTRL" || upper == "LCONTROL" ||
                        upper == "RCTRL" || upper == "RCONTROL" ||
                        upper == "ALT" || upper == "LALT" || upper == "RALT" ||
                        upper == "SHIFT" || upper == "LSHIFT" || upper == "RSHIFT" ||
                        upper == "WIN" || upper == "WINDOWS" || upper == "LWIN" || upper == "RWIN" ||
                        upper == "LMETA" || upper == "RMETA" || upper == "META")
                    {
                        modifierKeys.Add(vk);
                    }
                    else
                    {
                        mainKeys.Add(vk); // Add to list instead of overwriting
                    }
                }

                // Build key sequence: press modifiers, press all main keys, release all main keys in reverse, release modifiers
                // Press modifiers
                foreach (var mod in modifierKeys)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = mod, KeyOptions = InjectedInputKeyOptions.None });
                }

                // Press all main keys
                foreach (var key in mainKeys)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = key, KeyOptions = InjectedInputKeyOptions.None });
                }

                // Release all main keys in reverse order
                for (int i = mainKeys.Count - 1; i >= 0; i--)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = mainKeys[i], KeyOptions = InjectedInputKeyOptions.KeyUp });
                }

                // Release modifiers in reverse order
                for (int i = modifierKeys.Count - 1; i >= 0; i--)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = modifierKeys[i], KeyOptions = InjectedInputKeyOptions.KeyUp });
                }

                inputInjector.InjectKeyboardInput(keyInfos);
                Logger.Info($"Sent keyboard shortcut via InputInjector: {shortcut} (modifiers: {modifierKeys.Count}, keys: {mainKeys.Count})");
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
                DisposeLegionButtonMonitor();
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
            controllerEmulationManager = null;

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

            // Initialize controller hotkey monitor for XInput-based button combos
            InitializeControllerHotkeyMonitor();
        }

        /// <summary>
        /// Initializes the controller hotkey monitor for XInput-based button combos.
        /// This allows detection of Menu+DPad and View+ABXY combos in games.
        /// </summary>
        private static void InitializeControllerHotkeyMonitor()
        {
            try
            {
                controllerHotkeyMonitor = new ControllerHotkeyMonitor();
                controllerHotkeyMonitor.LoadSettings();

                // Set up callbacks for each combo
                // These will execute the same actions as the Xbox Game Bar hotkey watchers
                // Names must match widget's LocalSettings keys (without "Hotkey_" prefix)
                controllerHotkeyMonitor.OnMenuDPadUp = () => ExecuteControllerHotkeyAction("MenuDpadUp");
                controllerHotkeyMonitor.OnMenuDPadDown = () => ExecuteControllerHotkeyAction("MenuDpadDown");
                controllerHotkeyMonitor.OnMenuDPadLeft = () => ExecuteControllerHotkeyAction("MenuDpadLeft");
                controllerHotkeyMonitor.OnMenuDPadRight = () => ExecuteControllerHotkeyAction("MenuDpadRight");
                controllerHotkeyMonitor.OnViewA = () => ExecuteControllerHotkeyAction("MenuA");
                controllerHotkeyMonitor.OnViewB = () => ExecuteControllerHotkeyAction("MenuB");
                controllerHotkeyMonitor.OnViewX = () => ExecuteControllerHotkeyAction("MenuX");
                controllerHotkeyMonitor.OnViewY = () => ExecuteControllerHotkeyAction("MenuY");

                controllerHotkeyMonitor.Start();
                Logger.Info("Controller hotkey monitor initialized for XInput-based button combos");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize controller hotkey monitor: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes the configured action for a controller hotkey combo.
        /// Uses cached config from Named Pipe, falls back to LocalSettings.
        /// </summary>
        private static void ExecuteControllerHotkeyAction(string hotkeyName)
        {
            try
            {
                int action = 0;
                string keyParam = "";
                bool foundInConfig = false;

                // Try cached config first (received from widget via pipe)
                if (_controllerHotkeyConfig != null)
                {
                    if (_controllerHotkeyConfig.TryGetValue($"{hotkeyName}_Action", out var actionElement))
                    {
                        action = actionElement.GetInt32();
                        foundInConfig = true;
                    }
                    if (_controllerHotkeyConfig.TryGetValue($"{hotkeyName}_Key", out var keyElement))
                        keyParam = keyElement.GetString() ?? "";
                }

                // Fallback to LocalSettings if not in cached config
                // Widget saves as "Hotkey_{hotkeyName}_Action" and "Hotkey_{hotkeyName}_Key"
                if (!foundInConfig)
                {
                    if (LocalSettingsHelper.TryGetValue<int>($"Hotkey_{hotkeyName}_Action", out var localAction))
                        action = localAction;
                    if (LocalSettingsHelper.TryGetValue<string>($"Hotkey_{hotkeyName}_Key", out var localKey))
                        keyParam = localKey ?? "";
                    Logger.Debug($"ExecuteControllerHotkeyAction: Using LocalSettings fallback for {hotkeyName}");
                }

                Logger.Info($"ExecuteControllerHotkeyAction: {hotkeyName} action={action} key={keyParam}");

                // HotkeyAction enum from widget:
                // 0=Disabled, 1=KeyboardKey, 2=KeyboardShortcut, 3=ToggleOSD, 4=Screenshot,
                // 5=AltTab, 6=AltF4, 7=OpenKeyboard, 8=CtrlAltDel, 9=TaskManager, 10=FocusGoTweaks
                switch (action)
                {
                    case 0: // Disabled
                        break;
                    case 1: // KeyboardKey - single key press
                        if (!string.IsNullOrEmpty(keyParam))
                        {
                            ExecuteKeyboardShortcut(keyParam);
                        }
                        break;
                    case 2: // KeyboardShortcut - combo like Ctrl+Alt+X
                        if (!string.IsNullOrEmpty(keyParam))
                        {
                            ExecuteKeyboardShortcut(keyParam);
                        }
                        break;
                    case 3: // Toggle OSD
                        ToggleOSD();
                        break;
                    case 4: // Screenshot (Win+Shift+S)
                        ExecuteKeyboardShortcut("Win+Shift+S");
                        break;
                    case 5: // Alt+Tab
                        ExecuteKeyboardShortcut("Alt+Tab");
                        break;
                    case 6: // Alt+F4
                        ExecuteKeyboardShortcut("Alt+F4");
                        break;
                    case 7: // Open On-Screen Keyboard
                        OpenOnScreenKeyboard();
                        break;
                    case 8: // Ctrl+Alt+Del
                        ExecuteKeyboardShortcut("Ctrl+Alt+Delete");
                        break;
                    case 9: // Task Manager (Ctrl+Shift+Esc)
                        ExecuteKeyboardShortcut("Ctrl+Shift+Escape");
                        break;
                    case 10: // Focus GoTweaks widget
                        FocusGoTweaksWidget();
                        break;
                    default:
                        Logger.Warn($"ExecuteControllerHotkeyAction: Unknown action {action} for {hotkeyName}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ExecuteControllerHotkeyAction: Error executing {hotkeyName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a keyboard shortcut string (e.g., "Ctrl+Alt+Delete")
        /// Uses InputInjector which works properly from elevated helper context
        /// </summary>
        private static void ExecuteKeyboardShortcut(string shortcut)
        {
            try
            {
                Logger.Info($"ExecuteKeyboardShortcut: {shortcut}");
                // Use InputInjector (same as widget's SendKeyboardShortcutViaHelper)
                SendKeyboardShortcutViaInputInjector(shortcut);
            }
            catch (Exception ex)
            {
                Logger.Error($"ExecuteKeyboardShortcut: Failed to send {shortcut}: {ex.Message}");
            }
        }

        /// <summary>
        /// Opens the Windows on-screen keyboard using TouchKeyboardHelper
        /// </summary>
        private static void OpenOnScreenKeyboard()
        {
            try
            {
                Logger.Info("OpenOnScreenKeyboard: Toggling touch keyboard");
                TouchKeyboardHelper.Toggle();
                Logger.Info("OpenOnScreenKeyboard: Touch keyboard toggled");
            }
            catch (Exception ex)
            {
                Logger.Error($"OpenOnScreenKeyboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles OSD visibility by cycling through OSD levels (0=Off, 1, 2, 3)
        /// </summary>
        private static void ToggleOSD()
        {
            try
            {
                if (onScreenDisplay == null)
                {
                    Logger.Warn("ToggleOSD: OSD property not initialized");
                    return;
                }

                // Cycle through levels: 0 -> 1 -> 2 -> 3 -> 0
                int currentLevel = onScreenDisplay.Value;
                int newLevel = (currentLevel + 1) % 4;  // 0, 1, 2, 3, then back to 0

                onScreenDisplay.SetValue(newLevel);
                Logger.Info($"ToggleOSD: OSD level changed from {currentLevel} to {newLevel}");
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleOSD: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply controller hotkey configuration received from the widget.
        /// Enables/disables XInput-based button combo detection.
        /// </summary>
        private static void ApplyControllerHotkeyConfig(string configJson)
        {
            try
            {
                Logger.Info($"Applying controller hotkey config from widget");

                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(configJson);
                if (config == null || controllerHotkeyMonitor == null)
                {
                    Logger.Warn("ApplyControllerHotkeyConfig: Config is null or monitor not initialized");
                    return;
                }

                // Apply View + ABXY settings (View button = Back/two-squares button in XInput)
                // Widget saves as "Hotkey_MenuA_Action", etc.
                if (config.TryGetValue("MenuA_Action", out var menuAAction))
                    controllerHotkeyMonitor.ViewAEnabled = menuAAction.GetInt32() > 0;
                if (config.TryGetValue("MenuB_Action", out var menuBAction))
                    controllerHotkeyMonitor.ViewBEnabled = menuBAction.GetInt32() > 0;
                if (config.TryGetValue("MenuX_Action", out var menuXAction))
                    controllerHotkeyMonitor.ViewXEnabled = menuXAction.GetInt32() > 0;
                if (config.TryGetValue("MenuY_Action", out var menuYAction))
                    controllerHotkeyMonitor.ViewYEnabled = menuYAction.GetInt32() > 0;

                // Apply Menu + DPad settings (Menu button = Start/three-lines button in XInput)
                // Widget saves as "Hotkey_MenuDpadUp_Action", etc.
                if (config.TryGetValue("MenuDpadUp_Action", out var dpadUpAction))
                    controllerHotkeyMonitor.MenuDPadUpEnabled = dpadUpAction.GetInt32() > 0;
                if (config.TryGetValue("MenuDpadDown_Action", out var dpadDownAction))
                    controllerHotkeyMonitor.MenuDPadDownEnabled = dpadDownAction.GetInt32() > 0;
                if (config.TryGetValue("MenuDpadLeft_Action", out var dpadLeftAction))
                    controllerHotkeyMonitor.MenuDPadLeftEnabled = dpadLeftAction.GetInt32() > 0;
                if (config.TryGetValue("MenuDpadRight_Action", out var dpadRightAction))
                    controllerHotkeyMonitor.MenuDPadRightEnabled = dpadRightAction.GetInt32() > 0;

                // Store config for action execution
                _controllerHotkeyConfig = config;

                Logger.Info($"Controller hotkey config applied - Menu+DPad: Up={controllerHotkeyMonitor.MenuDPadUpEnabled}, Down={controllerHotkeyMonitor.MenuDPadDownEnabled}, Left={controllerHotkeyMonitor.MenuDPadLeftEnabled}, Right={controllerHotkeyMonitor.MenuDPadRightEnabled}");
                Logger.Info($"Controller hotkey config applied - View+ABXY: A={controllerHotkeyMonitor.ViewAEnabled}, B={controllerHotkeyMonitor.ViewBEnabled}, X={controllerHotkeyMonitor.ViewXEnabled}, Y={controllerHotkeyMonitor.ViewYEnabled}");
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyControllerHotkeyConfig: {ex.Message}");
            }
        }

        // Cached controller hotkey config for action execution
        private static Dictionary<string, System.Text.Json.JsonElement> _controllerHotkeyConfig;

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
        /// Returns: 0 = Stopped, 1 = Running, 2 = Not Found, 3 = Stopping, 4 = Starting
        /// </summary>
        private static int GetDAServiceStatus()
        {
            try
            {
                using (var sc = new ServiceController("DAService"))
                {
                    var status = sc.Status;
                    Logger.Debug($"Labs: DAService status = {status}");
                    switch (status)
                    {
                        case ServiceControllerStatus.Running:
                            return 1;
                        case ServiceControllerStatus.StopPending:
                            return 3; // Stopping
                        case ServiceControllerStatus.StartPending:
                            return 4; // Starting
                        default:
                            return 0; // Stopped or other
                    }
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
                    // Save original state before first modification (for clean uninstall)
                    try
                    {
                        using (var sc = new ServiceController("DAService"))
                        {
                            bool wasEnabled = sc.Status == ServiceControllerStatus.Running;
                            Services.SystemRestoreService.SaveOriginalDAServiceState(wasEnabled);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to save original DAService state: {ex.Message}");
                    }

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
        /// Parses global widget settings from a serialized ValueSet XML string.
        /// The widget sends settings as a ValueSet serialized to XML.
        /// </summary>
        private static Shared.Data.GlobalWidgetSettings ParseGlobalSettingsFromValueSet(string valueSetXml)
        {
            // The widget sends a compact XML representation of a ValueSet
            // We need to extract the key-value pairs and build a GlobalWidgetSettings object
            var gs = new Shared.Data.GlobalWidgetSettings();

            try
            {
                // Parse the XML to extract values
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(valueSetXml);

                // Helper to get int value
                int? GetInt(string key)
                {
                    var node = doc.SelectSingleNode($"//*[local-name()='{key}']");
                    if (node != null && int.TryParse(node.InnerText, out int val))
                        return val;
                    return null;
                }

                // Helper to get string value
                string GetString(string key)
                {
                    var node = doc.SelectSingleNode($"//*[local-name()='{key}']");
                    return node?.InnerText;
                }

                // Legion Button Remapping
                gs.LegionL_Action = GetInt("LegionL_Action");
                gs.LegionL_Shortcut = GetString("LegionL_Shortcut");
                gs.LegionL_Command = GetString("LegionL_Command");
                gs.LegionR_Action = GetInt("LegionR_Action");
                gs.LegionR_Shortcut = GetString("LegionR_Shortcut");
                gs.LegionR_Command = GetString("LegionR_Command");

                // Scroll Wheel Remapping
                gs.Scroll_Action = GetInt("Scroll_Action");
                gs.Scroll_Shortcut = GetString("Scroll_Shortcut");
                gs.Scroll_Command = GetString("Scroll_Command");
                gs.ScrollClick_Action = GetInt("ScrollClick_Action");
                gs.ScrollClick_Shortcut = GetString("ScrollClick_Shortcut");
                gs.ScrollClick_Command = GetString("ScrollClick_Command");

                // Device TDP Limits
                gs.DeviceTDPMin = GetInt("DeviceTDPMin");
                gs.DeviceTDPMax = GetInt("DeviceTDPMax");

                // OSD Customization
                gs.OSD_TextSize = GetInt("OSD_TextSize");
                gs.OSD_TextColor = GetString("OSD_TextColor");
                gs.OSD_LabelColor = GetString("OSD_LabelColor");
                gs.OSD_Opacity = GetInt("OSD_Opacity");

                // OSD Level Configuration - Order
                gs.OSD_L1_Order = GetString("OSD_L1_Order");
                gs.OSD_L2_Order = GetString("OSD_L2_Order");
                gs.OSD_L3_Order = GetString("OSD_L3_Order");

                // OSD Level Configuration - Enabled items
                gs.OSD_L1_Enabled = GetString("OSD_L1_Enabled");
                gs.OSD_L2_Enabled = GetString("OSD_L2_Enabled");
                gs.OSD_L3_Enabled = GetString("OSD_L3_Enabled");

                // OSD Level Configuration - Per-item colors
                gs.OSD_L1_ItemColors = GetString("OSD_L1_ItemColors");
                gs.OSD_L2_ItemColors = GetString("OSD_L2_ItemColors");
                gs.OSD_L3_ItemColors = GetString("OSD_L3_ItemColors");

                // OSD Level Configuration - Columns
                gs.OSD_L1_Columns = GetInt("OSD_L1_Columns");
                gs.OSD_L2_Columns = GetInt("OSD_L2_Columns");
                gs.OSD_L3_Columns = GetInt("OSD_L3_Columns");

                Logger.Info($"Parsed global settings: TDPMin={gs.DeviceTDPMin}, TDPMax={gs.DeviceTDPMax}, " +
                           $"LegionL={gs.LegionL_Action}, LegionR={gs.LegionR_Action}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error parsing global settings XML: {ex.Message}");
            }

            return gs;
        }

        /// <summary>
        /// Export all per-game profiles to a folder on the Desktop.
        /// Copies profile XML files and creates an index file.
        /// </summary>
        private static string ExportProfiles()
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var exportFolderName = $"GoTweaks_Profiles_{DateTime.Now:yyyy-MM-dd_HHmmss}";
            var exportPath = Path.Combine(desktopPath, exportFolderName);

            // Create export folder
            Directory.CreateDirectory(exportPath);

            // Get profiles folder path
            var profilesFolder = XboxGamingBarHelper.Profile.ProfileManager.GetGameProfilesFolder();
            var globalProfilePath = XboxGamingBarHelper.Profile.ProfileManager.GetGlobalProfilePath();

            int copiedCount = 0;

            // Copy global profile
            if (File.Exists(globalProfilePath))
            {
                var destPath = Path.Combine(exportPath, Path.GetFileName(globalProfilePath));
                File.Copy(globalProfilePath, destPath, true);
                copiedCount++;
            }

            // Copy all per-game profile XMLs
            if (Directory.Exists(profilesFolder))
            {
                var xmlFiles = Directory.GetFiles(profilesFolder, "*.xml");
                foreach (var xmlFile in xmlFiles)
                {
                    var destPath = Path.Combine(exportPath, Path.GetFileName(xmlFile));
                    File.Copy(xmlFile, destPath, true);
                    copiedCount++;
                }
            }

            // Create index file with summary
            var indexPath = Path.Combine(exportPath, "_index.txt");
            using (var writer = new StreamWriter(indexPath))
            {
                writer.WriteLine($"GoTweaks Per-Game Profiles Export");
                writer.WriteLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"========================================");
                writer.WriteLine();
                writer.WriteLine($"Total profiles exported: {copiedCount}");
                writer.WriteLine();
                writer.WriteLine("Files:");

                // List global profile
                if (File.Exists(globalProfilePath))
                {
                    writer.WriteLine($"  - {Path.GetFileName(globalProfilePath)} (Global)");
                }

                // List per-game profiles
                if (Directory.Exists(profilesFolder))
                {
                    var xmlFiles = Directory.GetFiles(profilesFolder, "*.xml").OrderBy(f => f);
                    foreach (var xmlFile in xmlFiles)
                    {
                        writer.WriteLine($"  - {Path.GetFileName(xmlFile)}");
                    }
                }
            }

            Logger.Info($"Exported {copiedCount} profiles to {exportPath}");
            return exportPath;
        }

        /// <summary>
        /// Export all GoTweaks data: profiles, settings, Q-learning model, OSD config.
        /// Creates a comprehensive backup folder on the Desktop.
        /// </summary>
        /// <param name="widgetSettings">JSON string of widget LocalSettings to include in export</param>
        /// <returns>Path to the export folder</returns>
        private static string ExportAllData(string widgetSettings = null)
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var exportFolderName = $"GoTweaks_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}";
            var exportPath = Path.Combine(desktopPath, exportFolderName);

            // Create export folder structure
            Directory.CreateDirectory(exportPath);
            var profilesFolder = Path.Combine(exportPath, "profiles");
            Directory.CreateDirectory(profilesFolder);

            int itemCount = 0;
            var manifest = new System.Text.StringBuilder();
            manifest.AppendLine("{");
            manifest.AppendLine($"  \"exportDate\": \"{DateTime.Now:O}\",");
            manifest.AppendLine($"  \"version\": \"1.0\",");
            manifest.AppendLine($"  \"appVersion\": \"{typeof(Program).Assembly.GetName().Version}\",");

            // 1. Export per-game profiles
            var srcProfilesFolder = XboxGamingBarHelper.Profile.ProfileManager.GetGameProfilesFolder();
            var profileNames = new List<string>();
            if (Directory.Exists(srcProfilesFolder))
            {
                var xmlFiles = Directory.GetFiles(srcProfilesFolder, "*.xml");
                foreach (var xmlFile in xmlFiles)
                {
                    var destPath = Path.Combine(profilesFolder, Path.GetFileName(xmlFile));
                    File.Copy(xmlFile, destPath, true);
                    profileNames.Add(Path.GetFileNameWithoutExtension(xmlFile));
                    itemCount++;
                }
            }

            // 2. Export global profile
            var globalProfilePath = XboxGamingBarHelper.Profile.ProfileManager.GetGlobalProfilePath();
            if (File.Exists(globalProfilePath))
            {
                var destPath = Path.Combine(exportPath, "global.xml");
                File.Copy(globalProfilePath, destPath, true);
                itemCount++;
            }

            // 3. Export Q-learning model (AutoTDP)
            var localStatePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg", "LocalState"
            );
            var qTablePath = Path.Combine(localStatePath, "autotdp_qtable.json");
            if (File.Exists(qTablePath))
            {
                var destPath = Path.Combine(exportPath, "autotdp_qtable.json");
                File.Copy(qTablePath, destPath, true);
                itemCount++;
                Logger.Info("Exported Q-learning model");
            }

            // 4. Export helper settings (from LocalCache/settings.json)
            var localCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg", "LocalCache"
            );
            var helperSettingsPath = Path.Combine(localCachePath, "settings.json");
            if (File.Exists(helperSettingsPath))
            {
                var destPath = Path.Combine(exportPath, "helper_settings.json");
                File.Copy(helperSettingsPath, destPath, true);
                itemCount++;
                Logger.Info("Exported helper settings");
            }

            // 5. Export widget settings (passed from widget)
            if (!string.IsNullOrEmpty(widgetSettings))
            {
                var destPath = Path.Combine(exportPath, "widget_settings.json");
                File.WriteAllText(destPath, widgetSettings);
                itemCount++;
                Logger.Info("Exported widget settings");
            }

            // 6. Export system restore data
            var systemRestorePath = Path.Combine(localStatePath, "system_restore_data.json");
            if (File.Exists(systemRestorePath))
            {
                var destPath = Path.Combine(exportPath, "system_restore_data.json");
                File.Copy(systemRestorePath, destPath, true);
                itemCount++;
            }

            // Build manifest
            manifest.AppendLine($"  \"profileCount\": {profileNames.Count},");
            manifest.AppendLine($"  \"profiles\": [{string.Join(", ", profileNames.Select(p => $"\"{p}\""))}],");
            manifest.AppendLine($"  \"hasGlobalProfile\": {(File.Exists(globalProfilePath) ? "true" : "false")},");
            manifest.AppendLine($"  \"hasQTable\": {(File.Exists(qTablePath) ? "true" : "false")},");
            manifest.AppendLine($"  \"hasHelperSettings\": {(File.Exists(helperSettingsPath) ? "true" : "false")},");
            manifest.AppendLine($"  \"hasWidgetSettings\": {(!string.IsNullOrEmpty(widgetSettings) ? "true" : "false")},");
            manifest.AppendLine($"  \"totalItems\": {itemCount}");
            manifest.AppendLine("}");

            File.WriteAllText(Path.Combine(exportPath, "manifest.json"), manifest.ToString());

            Logger.Info($"Exported {itemCount} items to {exportPath}");
            return exportPath;
        }

        /// <summary>
        /// Import all GoTweaks data from a backup folder.
        /// </summary>
        /// <param name="importPath">Path to the backup folder</param>
        /// <returns>Summary of imported items and widget settings JSON to apply</returns>
        private static (string summary, string widgetSettings) ImportAllData(string importPath)
        {
            if (!Directory.Exists(importPath))
            {
                throw new DirectoryNotFoundException($"Import folder not found: {importPath}");
            }

            var summary = new System.Text.StringBuilder();
            summary.AppendLine("=== Import Results ===");
            summary.AppendLine();

            int importedCount = 0;
            int skippedCount = 0;
            string widgetSettings = null;

            // 1. Import per-game profiles
            var srcProfilesFolder = Path.Combine(importPath, "profiles");
            var destProfilesFolder = XboxGamingBarHelper.Profile.ProfileManager.GetGameProfilesFolder();
            Directory.CreateDirectory(destProfilesFolder);

            if (Directory.Exists(srcProfilesFolder))
            {
                var xmlFiles = Directory.GetFiles(srcProfilesFolder, "*.xml");
                foreach (var xmlFile in xmlFiles)
                {
                    try
                    {
                        var destPath = Path.Combine(destProfilesFolder, Path.GetFileName(xmlFile));
                        File.Copy(xmlFile, destPath, true);
                        importedCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to import profile {Path.GetFileName(xmlFile)}: {ex.Message}");
                        skippedCount++;
                    }
                }
                summary.AppendLine($"✓ Imported {xmlFiles.Length} per-game profiles");
            }

            // 2. Import global profile
            var srcGlobalProfile = Path.Combine(importPath, "global.xml");
            if (File.Exists(srcGlobalProfile))
            {
                try
                {
                    var destPath = XboxGamingBarHelper.Profile.ProfileManager.GetGlobalProfilePath();
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(srcGlobalProfile, destPath, true);
                    importedCount++;
                    summary.AppendLine("✓ Imported global profile");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to import global profile: {ex.Message}");
                    summary.AppendLine($"✗ Failed to import global profile: {ex.Message}");
                }
            }

            // 3. Import Q-learning model
            var srcQTable = Path.Combine(importPath, "autotdp_qtable.json");
            if (File.Exists(srcQTable))
            {
                try
                {
                    var localStatePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg", "LocalState"
                    );
                    Directory.CreateDirectory(localStatePath);
                    var destPath = Path.Combine(localStatePath, "autotdp_qtable.json");
                    File.Copy(srcQTable, destPath, true);
                    importedCount++;
                    summary.AppendLine("✓ Imported AutoTDP Q-learning model");

                    // Notify AutoTDPManager to reload if active
                    if (autoTDPManager != null)
                    {
                        autoTDPManager.ReloadQLearningModel();
                        summary.AppendLine("  (Q-learning model reloaded)");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to import Q-learning model: {ex.Message}");
                    summary.AppendLine($"✗ Failed to import Q-learning model: {ex.Message}");
                }
            }

            // 4. Import helper settings
            var srcHelperSettings = Path.Combine(importPath, "helper_settings.json");
            if (File.Exists(srcHelperSettings))
            {
                try
                {
                    var localCachePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg", "LocalCache"
                    );
                    Directory.CreateDirectory(localCachePath);
                    var destPath = Path.Combine(localCachePath, "settings.json");
                    File.Copy(srcHelperSettings, destPath, true);
                    importedCount++;
                    summary.AppendLine("✓ Imported helper settings");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to import helper settings: {ex.Message}");
                    summary.AppendLine($"✗ Failed to import helper settings: {ex.Message}");
                }
            }

            // 5. Read widget settings (to be applied by widget)
            var srcWidgetSettings = Path.Combine(importPath, "widget_settings.json");
            if (File.Exists(srcWidgetSettings))
            {
                try
                {
                    widgetSettings = File.ReadAllText(srcWidgetSettings);
                    importedCount++;
                    summary.AppendLine("✓ Widget settings loaded (will be applied)");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to read widget settings: {ex.Message}");
                    summary.AppendLine($"✗ Failed to read widget settings: {ex.Message}");
                }
            }

            // 6. Import system restore data
            var srcSystemRestore = Path.Combine(importPath, "system_restore_data.json");
            if (File.Exists(srcSystemRestore))
            {
                try
                {
                    var localStatePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg", "LocalState"
                    );
                    Directory.CreateDirectory(localStatePath);
                    var destPath = Path.Combine(localStatePath, "system_restore_data.json");
                    File.Copy(srcSystemRestore, destPath, true);
                    importedCount++;
                    summary.AppendLine("✓ Imported system restore data");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to import system restore data: {ex.Message}");
                }
            }

            summary.AppendLine();
            summary.AppendLine($"Total: {importedCount} imported, {skippedCount} skipped");
            summary.AppendLine();
            summary.AppendLine("Note: Restart the widget to apply all changes.");

            Logger.Info($"Import complete: {importedCount} imported, {skippedCount} skipped");
            return (summary.ToString(), widgetSettings);
        }

        private static LegionButtonMonitor EnsureLegionButtonMonitor()
        {
            lock (legionButtonMonitorLock)
            {
                if (legionButtonMonitor == null)
                {
                    legionButtonMonitor = new LegionButtonMonitor();
                    Logger.Info("Labs: Created unified Legion button monitor with battery support");
                }

                if (!legionButtonMonitorBatteryHooked)
                {
                    legionButtonMonitor.BatteryUpdated += LegionButtonMonitor_BatteryUpdated;
                    legionButtonMonitorBatteryHooked = true;
                }

                return legionButtonMonitor;
            }
        }

        private static void LegionButtonMonitor_BatteryUpdated(object sender, LegionButtonBatteryEventArgs e)
        {
            try
            {
                legionManager?.UpdateControllerBatteryFromButtonMonitor(
                    e.LeftBattery, e.LeftCharging, e.LeftConnected,
                    e.RightBattery, e.RightCharging, e.RightConnected);

                LegionButtonMonitor monitor = sender as LegionButtonMonitor;
                string vidPid = monitor?.DetectedVidPid ?? legionButtonMonitor?.DetectedVidPid;
                if (!string.IsNullOrEmpty(vidPid))
                {
                    legionManager?.UpdateControllerVidPid(vidPid);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"BatteryUpdated handler exception: {ex.Message}");
            }
        }

        private static void DisposeLegionButtonMonitor()
        {
            lock (legionButtonMonitorLock)
            {
                if (legionButtonMonitor == null)
                {
                    legionButtonMonitorBatteryHooked = false;
                    return;
                }

                try
                {
                    if (legionButtonMonitorBatteryHooked)
                    {
                        legionButtonMonitor.BatteryUpdated -= LegionButtonMonitor_BatteryUpdated;
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
                finally
                {
                    legionButtonMonitorBatteryHooked = false;
                }

                try
                {
                    legionButtonMonitor.Dispose();
                }
                finally
                {
                    legionButtonMonitor = null;
                }
            }
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
                lock (legionButtonMonitorLock)
                {
                    LegionButtonMonitor monitor = EnsureLegionButtonMonitor();

                    // Remember state before configuration
                    bool wasRunning = monitor.IsRunning;
                    bool neededViGEmBefore = monitor.NeedsViGEm;

                    // Configure the button on the unified monitor
                    // This just updates internal flags - the monitor loop will pick up changes on next iteration
                    monitor.ConfigureButton(
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
                    bool needsViGEmNow = monitor.NeedsViGEm;
                    bool vigemRequirementChanged = neededViGEmBefore != needsViGEmNow;

                    // Handle different scenarios
                    if (!monitor.HasAnyButtonConfigured)
                    {
                        // No buttons configured - restart for battery-only mode if it was running with buttons
                        if (wasRunning && neededViGEmBefore)
                        {
                            // Was running with ViGEm for buttons, restart for battery-only
                            Logger.Info($"Labs: Legion {button} button disabled, restarting monitor for battery-only mode");
                            monitor.Stop();
                            monitor.StartForBatteryMonitoring();
                        }
                        else if (!wasRunning)
                        {
                            // Monitor wasn't running, start for battery monitoring
                            monitor.StartForBatteryMonitoring();
                        }
                        // else: already running for battery-only, no change needed
                        Logger.Info($"Labs: Legion {button} button disabled, no buttons configured - battery monitoring continues");
                        return true;
                    }
                    else if (wasRunning && vigemRequirementChanged)
                    {
                        // ViGEm requirement changed - need to restart monitor
                        Logger.Info($"Labs: ViGEm requirement changed ({neededViGEmBefore} -> {needsViGEmNow}), restarting monitor");
                        monitor.Stop();
                        if (!monitor.Start())
                        {
                            string errorReason = needsViGEmNow ? "ViGEmBus not installed or " : "";
                            Logger.Error($"Labs: Failed to restart Legion button monitoring ({errorReason}controller not found)");
                            return false;
                        }
                    }
                    else if (!wasRunning)
                    {
                        // Monitor wasn't running - start it
                        if (!monitor.Start())
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
                lock (legionButtonMonitorLock)
                {
                    LegionButtonMonitor monitor = EnsureLegionButtonMonitor();

                    // Remember state before configuration
                    bool wasRunning = monitor.IsRunning;
                    bool neededViGEmBefore = monitor.NeedsViGEm;

                    // Configure the scroll wheel action on the unified monitor
                    monitor.ConfigureScrollWheel(
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
                    bool needsViGEmNow = monitor.NeedsViGEm;
                    bool vigemRequirementChanged = neededViGEmBefore != needsViGEmNow;

                    // Handle different scenarios
                    if (!monitor.HasAnyButtonConfigured && !monitor.HasAnyScrollConfigured)
                    {
                        // No buttons or scroll configured - restart for battery-only mode if it was running
                        if (wasRunning && neededViGEmBefore)
                        {
                            Logger.Info($"Labs: Scroll {direction} disabled, no buttons/scroll configured - restarting for battery-only");
                            monitor.Stop();
                            monitor.StartForBatteryMonitoring();
                        }
                        else if (!wasRunning)
                        {
                            monitor.StartForBatteryMonitoring();
                        }
                        Logger.Info($"Labs: Scroll {direction} disabled - battery monitoring continues");
                        return true;
                    }
                    else if (wasRunning && vigemRequirementChanged)
                    {
                        // ViGEm requirement changed - need to restart monitor
                        Logger.Info($"Labs: ViGEm requirement changed ({neededViGEmBefore} -> {needsViGEmNow}), restarting monitor");
                        monitor.Stop();
                        if (!monitor.Start())
                        {
                            string errorReason = needsViGEmNow ? "ViGEmBus not installed or " : "";
                            Logger.Error($"Labs: Failed to restart monitoring ({errorReason}controller not found)");
                            return false;
                        }
                    }
                    else if (!wasRunning)
                    {
                        // Monitor wasn't running - start it
                        if (!monitor.Start())
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
                await Task.Delay(200);

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
                else
                {
                    Logger.Warn("Labs: Cannot send focus widget command - no pipe connection available");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Labs: Failed to focus GoTweaks widget: {ex.Message}");
            }
        }

        #endregion

        #region Screen Saver

        private static void SetScreenSaverEnabled(bool enabled)
        {
            screenSaverEnabled = enabled;
            if (enabled)
            {
                if (screenSaverTimer == null)
                {
                    screenSaverTimer = new System.Threading.Timer(ScreenSaverIdleCheck, null, ScreenSaverCheckIntervalMs, ScreenSaverCheckIntervalMs);
                    Logger.Info("Screen Saver: Started idle monitoring timer");
                }
            }
            else
            {
                if (screenSaverTimer != null)
                {
                    screenSaverTimer.Dispose();
                    screenSaverTimer = null;
                    Logger.Info("Screen Saver: Stopped idle monitoring timer");
                }
            }
        }

        private static void ScreenSaverIdleCheck(object state)
        {
            if (!screenSaverEnabled) return;

            try
            {
                var lastInput = new LASTINPUTINFO();
                lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

                if (GetLastInputInfo(ref lastInput))
                {
                    uint idleMs = (uint)Environment.TickCount - lastInput.dwTime;
                    if (idleMs >= ScreenSaverIdleTimeoutMs)
                    {
                        if (!screenSaverTriggered)
                        {
                            screenSaverTriggered = true;
                            Logger.Info($"Screen Off: Idle for {idleMs}ms, turning off display");
                            SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, MONITOR_OFF);
                        }
                    }
                    else if (screenSaverTriggered)
                    {
                        // User moved mouse/pressed key — re-arm for next idle period
                        screenSaverTriggered = false;
                        Logger.Info("Screen Saver: Input detected, re-armed");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Screen Saver: Idle check failed: {ex.Message}");
            }
        }

        #endregion

        #region Auto Hibernate

        private static void SetAutoHibernateEnabled(bool enabled)
        {
            autoHibernateEnabled = enabled;
            if (enabled)
            {
                if (autoHibernateTimer == null)
                {
                    autoHibernateTimer = new System.Threading.Timer(AutoHibernateIdleCheck, null, AutoHibernateCheckIntervalMs, AutoHibernateCheckIntervalMs);
                    Logger.Info("Auto Hibernate: Started idle monitoring timer");
                }
            }
            else
            {
                if (autoHibernateTimer != null)
                {
                    autoHibernateTimer.Dispose();
                    autoHibernateTimer = null;
                    Logger.Info("Auto Hibernate: Stopped idle monitoring timer");
                }
            }
        }

        private static void UpdateAutoHibernateIdleTimeout(int minutes)
        {
            if (minutes < 1) minutes = 1;
            autoHibernateIdleTimeoutMs = minutes * 60 * 1000;
            Logger.Info($"Auto Hibernate: Idle timeout set to {minutes} minutes");
        }

        private static void AutoHibernateIdleCheck(object state)
        {
            if (!autoHibernateEnabled) return;

            try
            {
                var lastInput = new LASTINPUTINFO();
                lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

                if (GetLastInputInfo(ref lastInput))
                {
                    uint idleMs = (uint)Environment.TickCount - lastInput.dwTime;
                    if (idleMs < autoHibernateIdleTimeoutMs)
                    {
                        return;
                    }

                    if ((DateTime.UtcNow - lastAutoHibernateAttemptUtc).TotalMilliseconds < AutoHibernateCooldownMs)
                    {
                        return;
                    }

                    // Check power source mode: 1=AC Only, 2=DC Only
                    if (autoHibernateMode == 1) // AC Only
                    {
                        var powerStatus = global::Windows.System.Power.PowerManager.PowerSupplyStatus;
                        if (powerStatus != global::Windows.System.Power.PowerSupplyStatus.Adequate)
                        {
                            return; // Not on AC, skip
                        }
                    }
                    else if (autoHibernateMode == 2) // DC Only
                    {
                        var powerStatus = global::Windows.System.Power.PowerManager.PowerSupplyStatus;
                        if (powerStatus == global::Windows.System.Power.PowerSupplyStatus.Adequate)
                        {
                            return; // On AC, skip
                        }
                    }

                    // Avoid hibernating while a game is in the foreground
                    if (systemManager?.RunningGame?.Value.IsValid() == true && systemManager.RunningGame.Value.IsForeground)
                    {
                        Logger.Info("Auto Hibernate: Skipping - game is in foreground");
                        return;
                    }

                    lastAutoHibernateAttemptUtc = DateTime.UtcNow;
                    Logger.Info($"Auto Hibernate: Idle for {idleMs}ms, hibernating now");

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown",
                            Arguments = "/h",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Auto Hibernate: Failed to initiate hibernate: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Auto Hibernate: Idle check failed: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Helper class to toggle the Windows touch keyboard via COM interop
    /// </summary>
    internal static class TouchKeyboardHelper
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
        private class UIHostNoLaunch { }

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITipInvocation
        {
            void Toggle(IntPtr hwnd);
        }

        public static void Toggle()
        {
            try
            {
                var uiHostNoLaunch = new UIHostNoLaunch();
                var tipInvocation = (ITipInvocation)uiHostNoLaunch;
                tipInvocation.Toggle(IntPtr.Zero);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(uiHostNoLaunch);
                Logger.Info("Touch keyboard toggle executed via COM");
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Logger.Error($"COM error toggling touch keyboard: {ex.Message}");
                // Fallback: try launching TabTip.exe
                TryLaunchTabTip();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling touch keyboard: {ex.Message}");
                TryLaunchTabTip();
            }
        }

        private static void TryLaunchTabTip()
        {
            try
            {
                var tabtipPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                    "microsoft shared", "ink", "TabTip.exe");

                if (System.IO.File.Exists(tabtipPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tabtipPath,
                        UseShellExecute = true
                    });
                    Logger.Info("Launched TabTip.exe as fallback");
                }
                else
                {
                    Logger.Warn("TabTip.exe not found for fallback");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to launch TabTip.exe: {ex.Message}");
            }
        }
    }
}
