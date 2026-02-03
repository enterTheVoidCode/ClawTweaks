using LibreHardwareMonitor.Hardware;
using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.System.Power;
using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.PawnIO;
using XboxGamingBarHelper.Performance.Sensors;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.Performance
{
    internal class HardwareSensors : IDictionary<string, HardwareSensor>
    {
        HardwareSensor IDictionary<string, HardwareSensor>.this[string key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        ICollection<string> IDictionary<string, HardwareSensor>.Keys => throw new NotImplementedException();

        ICollection<HardwareSensor> IDictionary<string, HardwareSensor>.Values => throw new NotImplementedException();

        int ICollection<KeyValuePair<string, HardwareSensor>>.Count => throw new NotImplementedException();

        bool ICollection<KeyValuePair<string, HardwareSensor>>.IsReadOnly => throw new NotImplementedException();

        void IDictionary<string, HardwareSensor>.Add(string key, HardwareSensor value)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<string, HardwareSensor>>.Add(KeyValuePair<string, HardwareSensor> item)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<string, HardwareSensor>>.Clear()
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<string, HardwareSensor>>.Contains(KeyValuePair<string, HardwareSensor> item)
        {
            throw new NotImplementedException();
        }

        bool IDictionary<string, HardwareSensor>.ContainsKey(string key)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<string, HardwareSensor>>.CopyTo(KeyValuePair<string, HardwareSensor>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        IEnumerator<KeyValuePair<string, HardwareSensor>> IEnumerable<KeyValuePair<string, HardwareSensor>>.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        bool IDictionary<string, HardwareSensor>.Remove(string key)
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<string, HardwareSensor>>.Remove(KeyValuePair<string, HardwareSensor> item)
        {
            throw new NotImplementedException();
        }

        bool IDictionary<string, HardwareSensor>.TryGetValue(string key, out HardwareSensor value)
        {
            throw new NotImplementedException();
        }
    }

    internal class PerformanceManager : Manager
    {
        // Native methods for fast PawnIO driver detection
        private const string PAWNIO_DEVICE_PATH = @"\\.\PawnIO";
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private Computer computer;
        private IVisitor updateVisitor;
        private IntPtr ryzenAdjHandle;
        public IntPtr RyzenAdjHandle
        {
            get { return ryzenAdjHandle; }
        }

        public CPUUsageSensor CPUUsage { get; }
        public CPUClockSensor CPUClock { get; }
        public CPUWattageSensor CPUWattage { get ; }
        public CPUTemperatureSensor CPUTemperature { get; }
        public VRMTemperatureSensor VRMTemperature { get; }

        public GPUUsageSensor GPUUsage { get; }
        public GPUClockSensor GPUClock { get; }
        public GPUWattageSensor GPUWattage { get; }
        public GPUTemperatureSensor GPUTemperature { get; }

        public MemoryUsageSensor MemoryUsage { get; }
        public MemoryUsedSensor MemoryUsed { get; }
        public MemoryAvailableSensor MemoryAvailable { get; }

        public GPUMemoryUsedSensor GPUMemoryUsed { get; }
        public GPUMemoryFreeSensor GPUMemoryFree { get; }
        public GPUMemoryClockSensor GPUMemoryClock { get; }

        public BatteryLevelSensor BatteryLevel { get; }
        public BatteryRemainingTimeSensor BatteryRemainingTime { get; }
        public BatteryDischargeRateSensor BatteryDischargeRate { get; }
        public BatteryChargeRateSensor BatteryChargeRate { get; }
        public BatteryRemainingCapacitySensor BatteryRemainingCapacity { get; }
        public BatteryFullChargeCapacitySensor BatteryFullChargeCapacity { get; }

        /// <summary>
        /// Calculated time to full charge in seconds. Returns -1 if not charging or cannot calculate.
        /// </summary>
        public float BatteryTimeToFull
        {
            get
            {
                // Only calculate when charging (charge rate > 0)
                if (BatteryChargeRate.Value <= 0)
                    return -1;

                // Need remaining and full capacity to calculate
                if (BatteryRemainingCapacity.Value <= 0 || BatteryFullChargeCapacity.Value <= 0)
                {
                    Logger.Debug($"BatteryTimeToFull: Missing capacity values - Remaining={BatteryRemainingCapacity.Value}, Full={BatteryFullChargeCapacity.Value}");
                    return -1;
                }

                // Already full
                if (BatteryRemainingCapacity.Value >= BatteryFullChargeCapacity.Value)
                    return 0;

                // Time to Full (hours) = (Full Capacity - Remaining Capacity) mWh / 1000 / Charge Rate W
                // Capacity sensors report in mWh, charge rate is in W
                float remainingToChargeWh = (BatteryFullChargeCapacity.Value - BatteryRemainingCapacity.Value) / 1000f;
                float timeHours = remainingToChargeWh / BatteryChargeRate.Value;

                // Return in seconds for consistency with BatteryRemainingTime
                return timeHours * 3600;
            }
        }

        /// <summary>
        /// Calculated time remaining on battery in seconds. Returns -1 if not discharging or cannot calculate.
        /// Uses Windows API for battery percentage (more reliable after sleep/hibernate) combined with
        /// full charge capacity to get accurate remaining capacity.
        /// </summary>
        public float BatteryTimeRemaining
        {
            get
            {
                // Only calculate when discharging (discharge rate > 0)
                if (BatteryDischargeRate.Value <= 0)
                    return -1;

                // Sanity check: discharge rate should be reasonable (under 100W for a laptop)
                // Stale values after hibernate can be very high from gaming sessions
                if (BatteryDischargeRate.Value > 100)
                {
                    Logger.Debug($"BatteryTimeRemaining: Discharge rate too high ({BatteryDischargeRate.Value}W), likely stale");
                    return -1;
                }

                // Try to use Windows API percentage combined with full charge capacity
                // This is more reliable than LibreHardwareMonitor's remaining capacity after sleep/hibernate
                float remainingCapacityWh = -1;
                try
                {
                    int windowsBatteryPercent = PowerManager.RemainingChargePercent;
                    if (windowsBatteryPercent >= 0 && windowsBatteryPercent <= 100 && BatteryFullChargeCapacity.Value > 0)
                    {
                        // Calculate remaining from Windows % and full capacity
                        // Full capacity is in mWh, convert to Wh
                        float fullCapacityWh = BatteryFullChargeCapacity.Value / 1000f;
                        remainingCapacityWh = fullCapacityWh * windowsBatteryPercent / 100f;
                    }
                }
                catch
                {
                    // Windows API not available, fall through to LibreHardwareMonitor
                }

                // Fallback to LibreHardwareMonitor remaining capacity if Windows API failed
                if (remainingCapacityWh < 0)
                {
                    if (BatteryRemainingCapacity.Value <= 0)
                    {
                        Logger.Debug($"BatteryTimeRemaining: Missing capacity value - Remaining={BatteryRemainingCapacity.Value}");
                        return -1;
                    }
                    remainingCapacityWh = BatteryRemainingCapacity.Value / 1000f;
                }

                // Time Remaining (hours) = Remaining Capacity Wh / Discharge Rate W
                float timeHours = remainingCapacityWh / BatteryDischargeRate.Value;

                // Sanity check: time should be reasonable (under 24 hours)
                if (timeHours > 24)
                {
                    Logger.Debug($"BatteryTimeRemaining: Calculated time too high ({timeHours:F1}h), likely stale values");
                    return -1;
                }

                // Return in seconds
                return timeHours * 3600;
            }
        }

        public NetworkDownloadSensor NetworkDownload { get; }
        public NetworkUploadSensor NetworkUpload { get; }

        private List<HardwareSensor> hardwareSensors;

        private TDPProperty tdp;
        public TDPProperty TDP
        {
            get { return tdp; }
        }

        private CurrentTDPProperty currentTdp;
        public CurrentTDPProperty CurrentTDP
        {
            get { return currentTdp; }
        }

        // TDP Boost properties
        private TDPBoostEnabledProperty tdpBoostEnabled;
        public TDPBoostEnabledProperty TDPBoostEnabled
        {
            get { return tdpBoostEnabled; }
        }

        private TDPBoostSPPTProperty tdpBoostSPPT;
        public TDPBoostSPPTProperty TDPBoostSPPT
        {
            get { return tdpBoostSPPT; }
        }

        private TDPBoostFPPTProperty tdpBoostFPPT;
        public TDPBoostFPPTProperty TDPBoostFPPT
        {
            get { return tdpBoostFPPT; }
        }

        // Current TDP limits (for OSD display)
        public int CurrentSPL { get; private set; }
        public int CurrentSPPT { get; private set; }
        public int CurrentFPPT { get; private set; }

        // Flag to indicate AutoTDP is managing TDP - when true, widget TDP updates are ignored
        public bool IsAutoTDPActive { get; set; }

        private System.Timers.Timer currentTdpTimer;
        private string lastTdpString = "";
        private int consecutiveReadFailures = 0;

        // Legion Go support for manufacturer WMI TDP
        private LegionManager legionManager;

        // PawnIO/RyzenSMU support for anti-cheat compatible TDP control
        private RyzenSmuService ryzenSmuService;
        private bool pawnIOAvailable;

        // WinRing0 removed - deprecated TDP method, no longer bundled
        // private bool winRing0Available;
        // private const string WinRing0BackupFolder = @"C:\GoTweaks";

        // PawnIO driver installation status
        private bool pawnIOInstalled;

        // RyzenAdj lazy loading - only load when user disables Manufacturer WMI
        private bool ryzenAdjInitialized = false;
        private bool ryzenAdjInitAttempted = false;

        // Thread synchronization locks to prevent race conditions
        private readonly object tdpLock = new object();
        private readonly object ryzenAdjInitLock = new object();

        // Properties for TDP method availability
        // private TdpMethodAvailableProperty winRing0AvailableProperty; // WinRing0 removed
        private TdpMethodAvailableProperty pawnIOAvailableProperty;
        private TdpMethodAvailableProperty pawnIOInstalledProperty;
        private InstallPawnIOProperty installPawnIOProperty;

        /// <summary>
        /// Gets whether PawnIO is available for TDP control.
        /// </summary>
        public bool IsPawnIOAvailable => pawnIOAvailable;

        // WinRing0 removed - deprecated TDP method
        // /// <summary>
        // /// Gets whether WinRing0 files are available in C:\GoTweaks.
        // /// </summary>
        // public bool IsWinRing0Available => winRing0Available;

        // /// <summary>
        // /// Property for WinRing0 availability (exposed to widget).
        // /// </summary>
        // public TdpMethodAvailableProperty WinRing0AvailableProperty => winRing0AvailableProperty;

        /// <summary>
        /// Property for PawnIO availability (exposed to widget).
        /// </summary>
        public TdpMethodAvailableProperty PawnIOAvailableProperty => pawnIOAvailableProperty;

        /// <summary>
        /// Gets whether PawnIO driver is installed (may not work for TDP yet).
        /// </summary>
        public bool IsPawnIOInstalled => pawnIOInstalled;

        /// <summary>
        /// Property for PawnIO driver installed status (exposed to widget).
        /// </summary>
        public TdpMethodAvailableProperty PawnIOInstalledProperty => pawnIOInstalledProperty;

        /// <summary>
        /// Property to trigger PawnIO installation (exposed to widget).
        /// </summary>
        public InstallPawnIOProperty InstallPawnIOProperty => installPawnIOProperty;

        /// <summary>
        /// Sets the Legion Manager reference for WMI TDP support.
        /// Must be called after LegionManager is initialized.
        /// </summary>
        public void SetLegionManager(LegionManager manager)
        {
            legionManager = manager;
            Logger.Info($"LegionManager reference set. Legion detected: {manager?.LegionGoDetected?.Value ?? false}");
        }

        /// <summary>
        /// Initializes PawnIO/RyzenSMU for anti-cheat compatible TDP control.
        /// Call this after the helper is initialized.
        /// </summary>
        public void InitializePawnIO()
        {
            try
            {
                Logger.Info("Attempting to initialize PawnIO/RyzenSMU...");
                ryzenSmuService = new RyzenSmuService();

                if (ryzenSmuService.Initialize())
                {
                    pawnIOAvailable = true;
                    Logger.Info($"PawnIO/RyzenSMU initialized successfully. CPU: {ryzenSmuService.CpuCodeName}, SMU: 0x{ryzenSmuService.SmuVersion:X8}");
                }
                else
                {
                    pawnIOAvailable = false;
                    Logger.Warn("PawnIO/RyzenSMU initialization failed. PawnIO driver may not be installed.");
                    ryzenSmuService?.Dispose();
                    ryzenSmuService = null;
                }
            }
            catch (Exception ex)
            {
                pawnIOAvailable = false;
                Logger.Error($"Exception initializing PawnIO: {ex.Message}");
                ryzenSmuService?.Dispose();
                ryzenSmuService = null;
            }

            // Update the availability property if already initialized
            pawnIOAvailableProperty?.SetAvailable(pawnIOAvailable);
            Logger.Info($"PawnIO availability updated: {pawnIOAvailable}");
        }
        private const int MaxConsecutiveFailuresBeforeReinit = 5;
        private const double NormalTimerInterval = 2000; // 2 seconds
        private const double BackoffTimerInterval = 10000; // 10 seconds during failures
        private const int VerificationDelayMs = 500; // Delay before verification read after SetTDP
        private const int TDPDebounceDelayMs = 150; // Debounce delay for rapid TDP changes

        // TDP debouncing to prevent queue buildup from rapid changes
        private System.Threading.Timer tdpDebounceTimer;
        private int pendingTDP = -1; // -1 means no pending TDP
        private readonly object debounceLock = new object();

        // Flag to indicate if hardware detection is complete
        private volatile bool hardwareInitialized = false;

        internal PerformanceManager() : base()
        {
            // Initialize the computer sensors
            // Only enable hardware types actually used - others can cause hangs:
            // - IsMotherboardEnabled: SuperIO chip probing can hang
            // - IsStorageEnabled: SMART queries can timeout on some drives
            // - IsControllerEnabled: Fan controller probing (Legion has its own)
            // - IsNetworkEnabled: Not used in OSD
            Logger.Info("PerformanceManager: Creating Computer object...");
            computer = new Computer
            {
                IsCpuEnabled = true,        // CPU usage, clock, temp, wattage
                IsGpuEnabled = true,        // GPU usage, clock, temp, wattage, VRAM
                IsMemoryEnabled = true,     // RAM usage
                IsMotherboardEnabled = false, // DISABLED - not used, can hang on SuperIO
                IsControllerEnabled = false,  // DISABLED - not used, Legion has own fan control
                IsNetworkEnabled = false,     // DISABLED - not used in OSD
                IsStorageEnabled = false,     // DISABLED - not used, SMART can hang
                IsBatteryEnabled = true,      // Battery level, time, charge/discharge rate
            };
            Logger.Info("PerformanceManager: Creating UpdateVisitor...");
            updateVisitor = new UpdateVisitor();

            // Start hardware detection in background - don't block constructor
            // Sensors will return default values until hardware is initialized
            Logger.Info("PerformanceManager: Starting hardware detection in background...");
            Task.Run(() => InitializeHardwareAsync());

            // Initialize hardware sensors immediately (they'll work once hardware is detected)
            Logger.Info("Initializing hardware sensors...");
            CPUClock = new CPUClockSensor();
            CPUUsage = new CPUUsageSensor();
            CPUWattage = new CPUWattageSensor();
            CPUTemperature = new CPUTemperatureSensor();
            VRMTemperature = new VRMTemperatureSensor();
            GPUUsage = new GPUUsageSensor();
            GPUClock = new GPUClockSensor();
            GPUTemperature = new GPUTemperatureSensor();
            GPUWattage = new GPUWattageSensor();
            MemoryUsage = new MemoryUsageSensor();
            MemoryUsed = new MemoryUsedSensor();
            MemoryAvailable = new MemoryAvailableSensor();
            GPUMemoryUsed = new GPUMemoryUsedSensor();
            GPUMemoryFree = new GPUMemoryFreeSensor();
            GPUMemoryClock = new GPUMemoryClockSensor();
            BatteryLevel = new BatteryLevelSensor();
            BatteryRemainingTime = new BatteryRemainingTimeSensor();
            BatteryDischargeRate = new BatteryDischargeRateSensor();
            BatteryChargeRate = new BatteryChargeRateSensor();
            BatteryRemainingCapacity = new BatteryRemainingCapacitySensor();
            BatteryFullChargeCapacity = new BatteryFullChargeCapacitySensor();
            NetworkDownload = new NetworkDownloadSensor();
            NetworkUpload = new NetworkUploadSensor();

            hardwareSensors = new List<HardwareSensor>()
            {
                CPUClock,
                CPUUsage,
                CPUWattage,
                CPUTemperature,
                VRMTemperature,
                GPUUsage,
                GPUClock,
                GPUTemperature,
                GPUWattage,
                MemoryUsage,
                MemoryUsed,
                MemoryAvailable,
                GPUMemoryUsed,
                GPUMemoryFree,
                GPUMemoryClock,
                BatteryLevel,
                BatteryRemainingTime,
                BatteryDischargeRate,
                BatteryChargeRate,
                BatteryRemainingCapacity,
                BatteryFullChargeCapacity,
                NetworkDownload,
                NetworkUpload,
            };

            // RyzenAdj initialization deferred - WinRing0 no longer bundled
            // Use PawnIO for TDP control instead (anti-cheat compatible)
            Logger.Info("RyzenAdj initialization deferred (deprecated - PawnIO preferred for TDP control)");
            var initialTDP = 25;
            var initialCurrentTDP = "-- W";

            Logger.Info("Creating TDP properties...");
            tdp = new TDPProperty(initialTDP, null, this);
            currentTdp = new CurrentTDPProperty(initialCurrentTDP, null, this);
            lastTdpString = initialCurrentTDP;

            // Initialize TDP Boost properties (defaults: enabled=false, SPPT=1W, FPPT=3W)
            tdpBoostEnabled = new TDPBoostEnabledProperty(false, this);
            tdpBoostSPPT = new TDPBoostSPPTProperty(1, this);
            tdpBoostFPPT = new TDPBoostFPPTProperty(3, this);
            Logger.Info("TDP Boost properties initialized (defaults: enabled=false, SPPT=1W, FPPT=3W)");

            // Set up timer to update current TDP every 3 seconds
            currentTdpTimer = new System.Timers.Timer(NormalTimerInterval);
            currentTdpTimer.Elapsed += UpdateCurrentTDP;
            currentTdpTimer.AutoReset = true;
            currentTdpTimer.Start();
            Logger.Info("CurrentTDP timer started, updating every 3 seconds");

            // WinRing0 removed - deprecated TDP method, no longer bundled
            // CheckWinRing0Availability();

            // Check PawnIO driver installation status
            CheckPawnIODriverInstalled();

            // Initialize TDP method availability properties
            // winRing0AvailableProperty = new TdpMethodAvailableProperty(winRing0Available, Function.TdpMethod_WinRing0Available, this); // WinRing0 removed
            pawnIOAvailableProperty = new TdpMethodAvailableProperty(pawnIOAvailable, Function.TdpMethod_PawnIOAvailable, this);
            pawnIOInstalledProperty = new TdpMethodAvailableProperty(pawnIOInstalled, Function.TdpMethod_PawnIOInstalled, this);
            installPawnIOProperty = new InstallPawnIOProperty(this);
            Logger.Info($"TDP method availability: PawnIO={pawnIOAvailable}, PawnIOInstalled={pawnIOInstalled}");
        }

        /// <summary>
        /// Initialize hardware detection in background. This is slow (2+ seconds) so we don't block the constructor.
        /// </summary>
        private void InitializeHardwareAsync()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Logger.Info("PerformanceManager: Background hardware detection starting...");

                // Call computer.Open() - this is the slow part
                try
                {
                    computer.Open();
                    Logger.Info("PerformanceManager: computer.Open() completed successfully.");
                }
                catch (AggregateException ae)
                {
                    Logger.Warn($"PerformanceManager: computer.Open() had {ae.InnerExceptions.Count} errors (some hardware may not be available):");
                    foreach (var innerEx in ae.InnerExceptions)
                    {
                        Logger.Warn($"  - {innerEx.GetType().Name}: {innerEx.Message}");
                    }
                    // Continue anyway - use whatever hardware was initialized
                }
                catch (Exception ex)
                {
                    Logger.Error($"PerformanceManager: computer.Open() failed: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Logger.Error($"  Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                }

                // Accept the visitor for whatever hardware was found
                try
                {
                    computer.Accept(updateVisitor);
                    Logger.Info("PerformanceManager: computer.Accept() completed.");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"PerformanceManager: computer.Accept() failed: {ex.Message}");
                }

                // Log discovered hardware
                foreach (IHardware hardware in computer.Hardware)
                {
                    var properties = string.Empty;
                    if (hardware.Properties.Count > 0)
                    {
                        foreach (var property in hardware.Properties)
                        {
                            properties = properties.Length == 0 ? $"{property.Key}:{property.Value}" : $"{properties}, {property.Key}:{property.Value}";
                        }
                    }
                    Logger.Info($"Found hardware {hardware.HardwareType}: Name={hardware.Name}, Type={hardware.HardwareType}, Id={hardware.Identifier}, Properties={properties}");

                    // Log sensors for key hardware types
                    if (hardware.HardwareType == HardwareType.Cpu ||
                        hardware.HardwareType == HardwareType.Battery ||
                        hardware.HardwareType == HardwareType.GpuNvidia ||
                        hardware.HardwareType == HardwareType.GpuIntel ||
                        hardware.HardwareType == HardwareType.GpuAmd)
                    {
                        hardware.Update();
                        foreach (ISensor sensor in hardware.Sensors)
                        {
                            Logger.Info($"  {hardware.HardwareType} Sensor: Name='{sensor.Name}', Type={sensor.SensorType}, Value={sensor.Value}");
                        }
                    }
                }

                hardwareInitialized = true;
                timer.Stop();
                Logger.Info($"[TIMING] Background hardware detection completed: {timer.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Error($"PerformanceManager: Background hardware detection failed: {ex.Message}");
                timer.Stop();
            }
        }

        // WinRing0 removed - deprecated TDP method, no longer bundled
        // /// <summary>
        // /// Checks if WinRing0 files exist in C:\GoTweaks folder.
        // /// </summary>
        // private void CheckWinRing0Availability()
        // {
        //     try
        //     {
        //         // Check for bundled WinRing0 files in helper's directory
        //         string helperDir = AppDomain.CurrentDomain.BaseDirectory;
        //         string dllPath = Path.Combine(helperDir, "WinRing0x64.dll");
        //         string sysPath = Path.Combine(helperDir, "WinRing0x64.sys");
        //         string libRyzenAdjPath = Path.Combine(helperDir, "libryzenadj.dll");
        //
        //         winRing0Available = File.Exists(dllPath) && File.Exists(sysPath) && File.Exists(libRyzenAdjPath);
        //
        //         if (winRing0Available)
        //         {
        //             Logger.Info($"WinRing0 files found (bundled) in {helperDir}");
        //
        //             // Also copy to backup folder for external access if needed
        //             try
        //             {
        //                 if (!Directory.Exists(WinRing0BackupFolder))
        //                     Directory.CreateDirectory(WinRing0BackupFolder);
        //
        //                 CopyFileIfNewer(dllPath, Path.Combine(WinRing0BackupFolder, "WinRing0x64.dll"));
        //                 CopyFileIfNewer(sysPath, Path.Combine(WinRing0BackupFolder, "WinRing0x64.sys"));
        //                 CopyFileIfNewer(libRyzenAdjPath, Path.Combine(WinRing0BackupFolder, "libryzenadj.dll"));
        //             }
        //             catch (Exception ex)
        //             {
        //                 Logger.Warn($"Could not copy WinRing0 files to backup folder: {ex.Message}");
        //             }
        //         }
        //         else
        //         {
        //             Logger.Info($"WinRing0 files not bundled in {helperDir} (WinRing0 TDP method will be hidden)");
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         winRing0Available = false;
        //         Logger.Warn($"Error checking WinRing0 availability: {ex.Message}");
        //     }
        // }

        private void CopyFileIfNewer(string source, string dest)
        {
            if (!File.Exists(source)) return;

            bool needsCopy = !File.Exists(dest);
            if (!needsCopy)
            {
                var sourceInfo = new FileInfo(source);
                var destInfo = new FileInfo(dest);
                needsCopy = sourceInfo.Length != destInfo.Length || sourceInfo.LastWriteTimeUtc > destInfo.LastWriteTimeUtc;
            }

            if (needsCopy)
            {
                File.Copy(source, dest, true);
            }
        }

        /// <summary>
        /// Checks if PawnIO driver is installed on the system.
        /// Uses fast CreateFile check instead of slow WMI query.
        /// </summary>
        private void CheckPawnIODriverInstalled()
        {
            Logger.Info("Checking PawnIO driver installation status...");

            try
            {
                // Try to open the PawnIO device - this is much faster than WMI
                IntPtr handle = CreateFile(
                    PAWNIO_DEVICE_PATH,
                    GENERIC_READ,
                    FILE_SHARE_READ,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                {
                    // Successfully opened - driver is installed
                    CloseHandle(handle);
                    pawnIOInstalled = true;
                    Logger.Info("PawnIO driver is installed (device opened successfully)");
                }
                else
                {
                    // Failed to open - driver not installed or not running
                    pawnIOInstalled = false;
                    int error = Marshal.GetLastWin32Error();
                    Logger.Info($"PawnIO driver is not installed (error code: {error})");
                }
            }
            catch (Exception ex)
            {
                pawnIOInstalled = false;
                Logger.Warn($"Error checking PawnIO driver: {ex.Message}");
            }
        }

        /// <summary>
        /// Refreshes the PawnIO driver installation status.
        /// Called after installation attempt to update the UI.
        /// </summary>
        public void RefreshPawnIOInstalledStatus()
        {
            CheckPawnIODriverInstalled();
            pawnIOInstalledProperty?.SetAvailable(pawnIOInstalled);
            Logger.Info($"PawnIO driver installation status refreshed: {pawnIOInstalled}");
        }

        /// <summary>
        /// Forces a refresh of all hardware sensors, particularly useful after resume from hibernation.
        /// LibreHardwareMonitor caches values that can become stale after hibernation.
        /// </summary>
        public void ForceRefreshHardware()
        {
            if (computer == null)
                return;

            Logger.Info("ForceRefreshHardware: Forcing refresh of all hardware sensors after resume");

            try
            {
                // Force update all hardware
                foreach (IHardware hardware in computer.Hardware)
                {
                    hardware.Update();

                    // Also update sub-hardware (some sensors are nested)
                    foreach (IHardware subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                    }

                    // Log battery values for debugging
                    if (hardware.HardwareType == HardwareType.Battery)
                    {
                        foreach (ISensor sensor in hardware.Sensors)
                        {
                            Logger.Info($"ForceRefreshHardware: Battery sensor '{sensor.Name}' = {sensor.Value}");
                        }
                    }
                }

                // Accept visitor to ensure all values are propagated
                computer.Accept(updateVisitor);

                // Log Windows API battery value for comparison
                try
                {
                    int windowsBatteryPercent = PowerManager.RemainingChargePercent;
                    Logger.Info($"ForceRefreshHardware: Windows API Battery = {windowsBatteryPercent}%");
                }
                catch (Exception apiEx)
                {
                    Logger.Warn($"ForceRefreshHardware: Failed to get Windows API battery: {apiEx.Message}");
                }

                Logger.Info("ForceRefreshHardware: Hardware refresh complete");
            }
            catch (Exception ex)
            {
                Logger.Error($"ForceRefreshHardware: Error refreshing hardware: {ex.Message}");
            }
        }

        public override void Update()
        {
            base.Update();

            /*if (ryzenAdjHandle != IntPtr.Zero)
            {
                Logger.Info($"get_core_clk={RyzenAdj.get_core_clk(ryzenAdjHandle, 0)} get_core_power={RyzenAdj.get_core_power(ryzenAdjHandle, 0)} get_fclk={RyzenAdj.get_fclk(ryzenAdjHandle)} get_gfx_clk={RyzenAdj.get_gfx_clk(ryzenAdjHandle)} get_soc_power={RyzenAdj.get_soc_power(ryzenAdjHandle)} get_socket_power={RyzenAdj.get_socket_power(ryzenAdjHandle)}");
                var setMaxResult = RyzenAdj.set_max_gfxclk_freq(ryzenAdjHandle, 2000);
                var setMinResult = RyzenAdj.set_min_gfxclk_freq(ryzenAdjHandle, 1000);
                //var nan2 = float.NaN;
                //var setResult = RyzenAdj.set_gfx_clk(ryzenAdjHandle, (uint)nan2);

                Logger.Info($"set_max={setMaxResult} set_min={setMinResult} set={"123"}");
            }*/

            // Track which sensors have received valid values this update cycle
            // This allows us to prefer valid values over N/A when multiple GPUs exist (MUX switch scenarios)
            var sensorsWithValidValues = new HashSet<HardwareSensor>();

            foreach (var hardwareSensor in hardwareSensors)
            {
                hardwareSensor.Value = -1.0f;
            }

            if (computer == null)
                return;

            computer.Accept(updateVisitor);
            foreach (IHardware hardware in computer.Hardware)
            {
                // Process sensors for this hardware
                ProcessHardwareSensors(hardware);

                // Also process sub-hardware sensors (some sensors like GPU temp are nested)
                foreach (IHardware subHardware in hardware.SubHardware)
                {
                    ProcessHardwareSensors(subHardware);
                }
            }

            // Override battery level with Windows API value
            // LibreHardwareMonitor calculates battery % from capacity values which can be stale after sleep
            // Windows API returns the battery controller's actual reported percentage
            try
            {
                int windowsBatteryPercent = PowerManager.RemainingChargePercent;
                if (windowsBatteryPercent >= 0 && windowsBatteryPercent <= 100)
                {
                    BatteryLevel.Value = windowsBatteryPercent;
                }
            }
            catch
            {
                // Fallback to LibreHardwareMonitor value if Windows API fails
            }

            // Calculate battery remaining time from capacity and discharge rate
            // LibreHardwareMonitor doesn't always report the "Remaining Time (Estimated)" sensor
            float calculatedTimeRemaining = BatteryTimeRemaining;
            if (calculatedTimeRemaining >= 0)
            {
                BatteryRemainingTime.Value = calculatedTimeRemaining;
            }
        }

        /// <summary>
        /// Processes sensors for a given hardware device and updates matching HardwareSensor values.
        /// </summary>
        private void ProcessHardwareSensors(IHardware hardware)
        {
            foreach (ISensor sensor in hardware.Sensors)
            {
                // Match ALL sensors that match this hardware sensor (don't break on first)
                // This allows sensors like GPUTemperature and VRMTemperature to share the same reading
                float newValue = sensor.Value ?? -1;
                foreach (var hardwareSensor in hardwareSensors)
                {
                    if (hardwareSensor.MatchesHardwareType(hardware.HardwareType) &&
                        hardwareSensor.SensorType == sensor.SensorType &&
                        hardwareSensor.MatchesSensorName(sensor.Name))
                    {
                        // Prefer non-zero valid values over zero or invalid values
                        // This handles dual-GPU scenarios (iGPU + dGPU) where iGPU may report 0W
                        // while dGPU has the actual power reading
                        bool currentIsValid = hardwareSensor.Value >= 0;
                        bool currentIsNonZero = hardwareSensor.Value > 0;
                        bool newIsValid = newValue >= 0;
                        bool newIsNonZero = newValue > 0;

                        // Update if:
                        // 1. Current value is invalid (-1), OR
                        // 2. New value is non-zero (prefer actual readings over 0)
                        // Don't overwrite a non-zero valid value with zero
                        if (!currentIsValid || newIsNonZero || (!currentIsNonZero && newIsValid))
                        {
                            hardwareSensor.Value = newValue;
                        }
                    }
                }
            }
        }

        public int GetTDP()
        {
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Logger.Info("RyzenAdj not initialized");
                return 10;
            }

            RyzenAdj.refresh_table(ryzenAdjHandle);
            return (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
        }

        public void SetTDP(int tdp)
        {
            // Debounce rapid TDP changes to prevent queue buildup
            // Only the final value will be applied after the debounce delay
            lock (debounceLock)
            {
                pendingTDP = tdp;

                // Cancel existing timer and start a new one
                tdpDebounceTimer?.Dispose();
                tdpDebounceTimer = new System.Threading.Timer(
                    _ => ApplyPendingTDP(),
                    null,
                    TDPDebounceDelayMs,
                    System.Threading.Timeout.Infinite // Don't repeat
                );

                Logger.Debug($"SetTDP: Debouncing TDP change to {tdp}W (will apply in {TDPDebounceDelayMs}ms if no new changes)");
            }
        }

        /// <summary>
        /// Called by the debounce timer to apply the pending TDP value.
        /// </summary>
        private void ApplyPendingTDP()
        {
            int tdpToApply;
            lock (debounceLock)
            {
                if (pendingTDP < 0)
                {
                    return; // No pending TDP
                }
                tdpToApply = pendingTDP;
                pendingTDP = -1; // Clear pending
            }

            ApplyTDPInternal(tdpToApply);
        }

        /// <summary>
        /// Internal method that actually applies the TDP to hardware.
        /// Called after debouncing completes.
        /// </summary>
        private void ApplyTDPInternal(int tdp)
        {
            // Lock to prevent race conditions from multiple sources calling SetTDP simultaneously
            // (widget slider, AutoTDP, TDP Boost changes, profile switches)
            lock (tdpLock)
            {
                var settingsManager = SettingsManager.GetInstance();
                TdpMethod tdpMethod = settingsManager?.TdpMethod?.Method ?? TdpMethod.ManufacturerWMI;
                bool legionDetected = legionManager?.LegionGoDetected?.Value ?? false;

                // Calculate actual TDP values based on TDP Boost settings
                // When boost is enabled: SPPT = TDP + boost_sppt, FPPT = TDP + boost_fppt
                // SPL/STAPM stays at base TDP value
                int spl = tdp;
                int sppt = tdp;
                int fppt = tdp;

                if (tdpBoostEnabled?.Value == true)
                {
                    int spptBoost = tdpBoostSPPT?.Value ?? 1;
                    int fpptBoost = tdpBoostFPPT?.Value ?? 3;
                    sppt = tdp + spptBoost;
                    fppt = tdp + fpptBoost;
                    Logger.Info($"TDP Boost enabled: SPL={spl}W, SPPT={sppt}W (+{spptBoost}), FPPT={fppt}W (+{fpptBoost})");
                }

                // Note: CurrentSPL/SPPT/FPPT are now updated from actual hardware values
                // in UpdateCurrentTDP, which is called via ScheduleVerificationRead after SetTDP

                Logger.Info($"SetTDP: method={tdpMethod}, legionDetected={legionDetected}, pawnIOAvailable={pawnIOAvailable}");

                // Use the selected TDP method
                switch (tdpMethod)
                {
                    case TdpMethod.ManufacturerWMI:
                        if (legionDetected && legionManager != null)
                        {
                            // Note: SetCustomTDP will automatically switch to Custom mode (255) if needed
                            Logger.Info($"Using Legion WMI to set TDP (SPL={spl}W, SPPL={sppt}W, FPPT={fppt}W)");
                            legionManager.SetCustomTDP(spl, sppt, fppt);
                            ScheduleVerificationRead();
                            return;
                        }
                        // Legion not detected - fall through to PawnIO
                        Logger.Warn("ManufacturerWMI selected but Legion not detected, trying PawnIO");
                        goto case TdpMethod.PawnIO;

                    case TdpMethod.PawnIO:
                        if (pawnIOAvailable && ryzenSmuService != null && ryzenSmuService.IsInitialized)
                        {
                            Logger.Info($"Using PawnIO/RyzenSMU to set TDP (SPL={spl}W, SPPT={sppt}W, FPPT={fppt}W)");
                            if (ryzenSmuService.SetAllLimits(spl, sppt, fppt))
                            {
                                Logger.Info($"PawnIO: TDP set successfully");
                                // Update current limits for OSD display (PawnIO can't read back values)
                                CurrentSPL = spl;
                                CurrentSPPT = sppt;
                                CurrentFPPT = fppt;
                                // Update the currentTdp property for widget display
                                var newTdpString = $"SPL:{spl}W SPPT:{sppt}W FPPT:{fppt}W";
                                if (newTdpString != lastTdpString)
                                {
                                    currentTdp.SetValue(newTdpString);
                                    lastTdpString = newTdpString;
                                }
                                return;
                            }
                            Logger.Warn("PawnIO: Failed to set TDP");
                        }
                        else
                        {
                            Logger.Warn("PawnIO not available");
                        }
                        Logger.Warn("SetTDP: No TDP control method available");
                        return;

                    // WinRing0 removed - deprecated TDP method, no longer bundled
                    // case TdpMethod.WinRing0:
                    //     // RyzenAdj (deprecated - WinRing0 no longer bundled)
                    //     if (!EnsureRyzenAdjInitialized())
                    //     {
                    //         Logger.Warn("SetTDP: WinRing0/RyzenAdj unavailable");
                    //         return;
                    //     }
                    //     Logger.Info($"Using RyzenAdj to set TDP (STAPM={spl}W, SLOW={sppt}W, FAST={fppt}W) (deprecated)");
                    //     RyzenAdj.set_fast_limit(ryzenAdjHandle, (uint)(fppt * 1000));
                    //     RyzenAdj.set_slow_limit(ryzenAdjHandle, (uint)(sppt * 1000));
                    //     RyzenAdj.set_stapm_limit(ryzenAdjHandle, (uint)(spl * 1000));
                    // #if DEBUG
                    //     RyzenAdj.refresh_table(ryzenAdjHandle);
                    //     Logger.Info($"Set TDP, current fast limit is {RyzenAdj.get_fast_limit(ryzenAdjHandle)}");
                    // #endif
                    //     break;
                }
            }

            // Schedule a verification read after a short delay to confirm the TDP was applied
            ScheduleVerificationRead();
        }

        /// <summary>
        /// Schedules a delayed verification read of TDP values after SetTDP.
        /// This allows the hardware time to apply the new values before reading back.
        /// </summary>
        private void ScheduleVerificationRead()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(VerificationDelayMs);
                    UpdateCurrentTDP(null, null);
                    Logger.Debug("ScheduleVerificationRead: Verification read completed");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"ScheduleVerificationRead: Error during verification read: {ex.Message}");
                }
            });
        }

        // WinRing0 removed - deprecated TDP method, no longer bundled
        // /// <summary>
        // /// Lazy-loads RyzenAdj when needed. RyzenAdj is deprecated as WinRing0 is no longer bundled.
        // /// This will only succeed if the user has WinRing0 files in C:\GoTweaks.
        // /// Copies libryzenadj.dll from app bundle to C:\GoTweaks so all files are together.
        // /// </summary>
        // /// <returns>True if RyzenAdj is available</returns>
        // private bool EnsureRyzenAdjInitialized()
        // {
        //     // Lock to prevent double-initialization race condition when multiple threads
        //     // try to initialize RyzenAdj simultaneously
        //     lock (ryzenAdjInitLock)
        //     {
        //         if (ryzenAdjInitialized)
        //             return ryzenAdjHandle != IntPtr.Zero;
        //
        //         if (ryzenAdjInitAttempted)
        //             return false; // Already tried and failed
        //
        //         ryzenAdjInitAttempted = true;
        //
        //         // Check if WinRing0 files are bundled
        //         if (!winRing0Available)
        //         {
        //             Logger.Warn("EnsureRyzenAdjInitialized: WinRing0 files not bundled");
        //             return false;
        //         }
        //
        //         try
        //         {
        //             // Load from helper's bundled directory (all files are together)
        //             string helperDir = AppDomain.CurrentDomain.BaseDirectory;
        //             RyzenAdj.LoadFromFolder(helperDir);
        //             ryzenAdjHandle = RyzenAdj.init_ryzenadj();
        //
        //             if (ryzenAdjHandle == IntPtr.Zero)
        //             {
        //                 // Get WinRing0 status for diagnostics
        //                 uint finalStatus = RyzenAdj.GetLastWinRing0Status();
        //                 string statusDesc = RyzenAdj.GetWinRing0StatusDescription(finalStatus);
        //                 Logger.Warn($"RyzenAdj initialization failed - WinRing0 status: {finalStatus} ({statusDesc})");
        //                 return false;
        //             }
        //
        //             RyzenAdj.refresh_table(ryzenAdjHandle);
        //             var stapm = (int)RyzenAdj.get_stapm_limit(ryzenAdjHandle);
        //             var fast = (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
        //             var slow = (int)RyzenAdj.get_slow_limit(ryzenAdjHandle);
        //
        //             if (stapm > 0 && fast > 0 && slow > 0 &&
        //                 stapm != int.MinValue && fast != int.MinValue && slow != int.MinValue)
        //             {
        //                 Logger.Info($"RyzenAdj initialized successfully - STAPM:{stapm}W FAST:{fast}W SLOW:{slow}W");
        //                 ryzenAdjInitialized = true;
        //                 return true;
        //             }
        //             else
        //             {
        //                 Logger.Warn($"RyzenAdj returned invalid values - STAPM:{stapm}W FAST:{fast}W SLOW:{slow}W");
        //                 return false;
        //             }
        //         }
        //         catch (Exception ex)
        //         {
        //             Logger.Error($"RyzenAdj initialization failed: {ex.Message}");
        //             ryzenAdjHandle = IntPtr.Zero;
        //             return false;
        //         }
        //     }
        // }

        // WinRing0 removed - deprecated TDP method, no longer bundled
        // private void ReinitializeRyzenAdj()
        // {
        //     // Lock to ensure thread-safe reinitialization
        //     lock (ryzenAdjInitLock)
        //     {
        //         Logger.Info("ReinitializeRyzenAdj: Attempting to reinitialize RyzenAdj handle");
        //
        //         // Clean up old handle if it exists
        //         if (ryzenAdjHandle != IntPtr.Zero)
        //         {
        //             try
        //             {
        //                 RyzenAdj.cleanup_ryzenadj(ryzenAdjHandle);
        //                 Logger.Info("ReinitializeRyzenAdj: Old handle cleaned up");
        //             }
        //             catch (Exception ex)
        //             {
        //                 Logger.Warn($"ReinitializeRyzenAdj: Error cleaning up old handle: {ex.Message}");
        //             }
        //         }
        //
        //         // Reset flags and try again
        //         ryzenAdjInitialized = false;
        //         ryzenAdjInitAttempted = false;
        //     }
        //
        //     // Call outside lock since EnsureRyzenAdjInitialized acquires the same lock
        //     if (EnsureRyzenAdjInitialized())
        //     {
        //         consecutiveReadFailures = 0;
        //     }
        // }
        //
        // private (int stapm, int fast, int slow) TryReadTdpValues()
        // {
        //     RyzenAdj.refresh_table(ryzenAdjHandle);
        //     var stapm = (int)RyzenAdj.get_stapm_limit(ryzenAdjHandle);
        //     var fast = (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
        //     var slow = (int)RyzenAdj.get_slow_limit(ryzenAdjHandle);
        //     return (stapm, fast, slow);
        // }
        //
        // private bool AreValuesValid(int stapm, int fast, int slow)
        // {
        //     return stapm > 0 && fast > 0 && slow > 0 &&
        //            stapm != int.MinValue && fast != int.MinValue && slow != int.MinValue;
        // }

        private void UpdateCurrentTDP(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // Check selected TDP method
                var settingsManager = SettingsManager.GetInstance();
                TdpMethod tdpMethod = settingsManager?.TdpMethod?.Method ?? TdpMethod.ManufacturerWMI;
                bool legionDetected = legionManager?.LegionGoDetected?.Value ?? false;

                // Priority 1: Legion WMI (when ManufacturerWMI selected and Legion is detected)
                if (tdpMethod == TdpMethod.ManufacturerWMI && legionDetected && legionManager != null)
                {
                    // Check current performance mode - if not Custom, show mode name instead of TDP values
                    int performanceMode = legionManager.CurrentPerformanceMode;
                    if (performanceMode != 255) // Not Custom mode
                    {
                        string modeName = LegionManager.GetPerformanceModeName(performanceMode);
                        if (modeName != lastTdpString)
                        {
                            Logger.Info($"UpdateCurrentTDP: Mode changed to '{modeName}', sending update");
                            currentTdp.SetValue(modeName);
                            lastTdpString = modeName;
                        }
                        return;
                    }

                    // Custom mode - use Legion WMI to get TDP values
                    var (slow, fast, peak) = legionManager.GetCurrentTDPValues();

                    if (slow.HasValue && fast.HasValue && peak.HasValue)
                    {
                        // Update actual hardware limits for OSD display
                        CurrentSPL = slow.Value;
                        CurrentSPPT = fast.Value;
                        CurrentFPPT = peak.Value;

                        var newTdpString = $"SPL:{slow}W SPPL:{fast}W FPPT:{peak}W";
                        Logger.Debug($"UpdateCurrentTDP (Legion WMI): Read values - {newTdpString}");

                        if (newTdpString != lastTdpString)
                        {
                            Logger.Info($"UpdateCurrentTDP: Value changed from '{lastTdpString}' to '{newTdpString}', sending update");
                            currentTdp.SetValue(newTdpString);
                            lastTdpString = newTdpString;
                        }
                    }
                    else
                    {
                        Logger.Debug("UpdateCurrentTDP (Legion WMI): Could not read all TDP values");
                    }
                    return;
                }

                // WinRing0 removed - deprecated TDP method, no longer bundled
                // RyzenAdj/WinRing0 TDP reading is no longer available
                return;

                // // Only use RyzenAdj when WinRing0 method is explicitly selected
                // if (tdpMethod != TdpMethod.WinRing0)
                // {
                //     Logger.Debug("UpdateCurrentTDP: TDP method is not WinRing0, skipping RyzenAdj");
                //     return;
                // }
                //
                // // Initialize RyzenAdj (lazy-load, copies WinRing0 files from C:\GoTweaks)
                // if (!EnsureRyzenAdjInitialized())
                // {
                //     Logger.Debug("UpdateCurrentTDP: RyzenAdj not available");
                //     return;
                // }
                //
                // Logger.Debug("UpdateCurrentTDP: Reading TDP limits from hardware");
                //
                // // Try reading values with retry
                // var (stapm, fastVal, slowVal) = TryReadTdpValues();
                //
                // // If first read fails, retry up to 2 more times with small delay
                // int retryCount = 0;
                // while (!AreValuesValid(stapm, fastVal, slowVal) && retryCount < 2)
                // {
                //     retryCount++;
                //     Logger.Debug($"UpdateCurrentTDP: Read attempt {retryCount + 1} failed, retrying...");
                //     System.Threading.Thread.Sleep(100); // Brief delay before retry
                //     (stapm, fastVal, slowVal) = TryReadTdpValues();
                // }
                //
                // // Check for invalid values (int.MinValue indicates read failure)
                // if (!AreValuesValid(stapm, fastVal, slowVal))
                // {
                //     consecutiveReadFailures++;
                //     Logger.Debug($"UpdateCurrentTDP: Invalid values read (STAPM:{stapm}, FAST:{fastVal}, SLOW:{slowVal}), failure count: {consecutiveReadFailures}");
                //
                //     // Backoff timer to reduce CPU usage during failures
                //     if (currentTdpTimer != null && currentTdpTimer.Interval != BackoffTimerInterval)
                //     {
                //         currentTdpTimer.Interval = BackoffTimerInterval;
                //         Logger.Info($"UpdateCurrentTDP: Backing off timer to {BackoffTimerInterval}ms due to failures");
                //     }
                //
                //     // If we've had too many consecutive failures, try reinitializing
                //     if (consecutiveReadFailures >= MaxConsecutiveFailuresBeforeReinit)
                //     {
                //         Logger.Warn($"UpdateCurrentTDP: {consecutiveReadFailures} consecutive read failures, reinitializing RyzenAdj");
                //         ReinitializeRyzenAdj();
                //     }
                //     return;
                // }
                //
                // // Reset failure counter on successful read
                // if (consecutiveReadFailures > 0)
                // {
                //     Logger.Info($"UpdateCurrentTDP: Read succeeded after {consecutiveReadFailures} failures");
                //     consecutiveReadFailures = 0;
                //
                //     // Restore normal timer interval
                //     if (currentTdpTimer != null && currentTdpTimer.Interval != NormalTimerInterval)
                //     {
                //         currentTdpTimer.Interval = NormalTimerInterval;
                //         Logger.Info($"UpdateCurrentTDP: Restored timer to {NormalTimerInterval}ms");
                //     }
                // }
                //
                // // Update actual hardware limits for OSD display
                // // RyzenAdj: STAPM=SPL (base), SLOW=SPPT, FAST=FPPT
                // CurrentSPL = stapm;
                // CurrentSPPT = slowVal;
                // CurrentFPPT = fastVal;
                //
                // // Only show limits (power consumption methods not working on this hardware)
                // var newTdpStringRyzen = $"STAPM:{stapm}W FAST:{fastVal}W SLOW:{slowVal}W";
                // Logger.Debug($"UpdateCurrentTDP: Read values - {newTdpStringRyzen}");
                //
                // // Only update if value has changed to reduce IPC traffic
                // if (newTdpStringRyzen != lastTdpString)
                // {
                //     Logger.Info($"UpdateCurrentTDP: Value changed from '{lastTdpString}' to '{newTdpStringRyzen}', sending update");
                //     currentTdp.SetValue(newTdpStringRyzen);
                //     lastTdpString = newTdpStringRyzen;
                // }
                // else
                // {
                //     Logger.Debug($"UpdateCurrentTDP: Value unchanged ({newTdpStringRyzen}), skipping update");
                // }
            }
            catch (Exception ex)
            {
                consecutiveReadFailures++;
                Logger.Error($"Error updating current TDP: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");

                // WinRing0 removed - RyzenAdj no longer used
                // if (consecutiveReadFailures >= MaxConsecutiveFailuresBeforeReinit)
                // {
                //     Logger.Warn($"UpdateCurrentTDP: {consecutiveReadFailures} consecutive failures with exceptions, reinitializing RyzenAdj");
                //     ReinitializeRyzenAdj();
                // }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("PerformanceManager: Disposing resources");

                // Stop and dispose the timer
                if (currentTdpTimer != null)
                {
                    currentTdpTimer.Stop();
                    currentTdpTimer.Elapsed -= UpdateCurrentTDP;
                    currentTdpTimer.Dispose();
                    currentTdpTimer = null;
                    Logger.Info("PerformanceManager: Timer disposed");
                }

                // Clean up PawnIO/RyzenSMU
                if (ryzenSmuService != null)
                {
                    try
                    {
                        ryzenSmuService.Dispose();
                        Logger.Info("PerformanceManager: PawnIO/RyzenSMU disposed");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"PerformanceManager: Error disposing PawnIO: {ex.Message}");
                    }
                    ryzenSmuService = null;
                    pawnIOAvailable = false;
                }

                // Clean up RyzenAdj handle
                if (ryzenAdjHandle != IntPtr.Zero)
                {
                    try
                    {
                        RyzenAdj.cleanup_ryzenadj(ryzenAdjHandle);
                        Logger.Info("PerformanceManager: RyzenAdj handle cleaned up");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"PerformanceManager: Error cleaning up RyzenAdj: {ex.Message}");
                    }
                    ryzenAdjHandle = IntPtr.Zero;
                }

                // Dispose LibreHardwareMonitor computer
                if (computer != null)
                {
                    try
                    {
                        computer.Close();
                        Logger.Info("PerformanceManager: LibreHardwareMonitor closed");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"PerformanceManager: Error closing LibreHardwareMonitor: {ex.Message}");
                    }
                    computer = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
