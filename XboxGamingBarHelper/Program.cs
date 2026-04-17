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
        private static Sidebar.SidebarManager sidebarManager;
        private static bool sidebarMenuEnabled;
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
                TryStartSidebar();
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
                sidebarManager?.Dispose();
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

            // Flush any pending debounced profile writes so we don't lose the last tick of changes.
            try { Shared.Data.GameProfile.FlushAllPendingWrites(); }
            catch (Exception ex) { Logger.Error(ex, "Error flushing pending profile writes"); }

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

            // Wire sidebar manager to live managers (WPF thread already running from TryStartSidebar)
            sidebarManager?.SetManagers(performanceManager, autoTDPManager, profileManager, legionManager, controllerEmulationManager, powerManager, rtssManager, systemManager);

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
                settingsManager.EmulationBackend,
                settingsManager.UsbipInstalled,
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

            // Restore all global profile settings (TDP, AutoTDP, CPUBoost, EPP, Legion mode, etc.)
            // on startup so saved values are applied to hardware after device restart.
            if (profileManager?.CurrentProfile != null)
            {
                Logger.Info($"Restoring global profile settings on startup: {profileManager.CurrentProfile.GameId.Name}");
                isApplyingProfile = true;
                try
                {
                    RestoreGlobalProfileSettings();
                }
                finally
                {
                    isApplyingProfile = false;
                }
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
    }
}
