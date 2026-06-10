using LibreHardwareMonitor.Hardware;
using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
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

        /// <summary>
        /// Returns true if the device is in Custom TDP mode (Legion mode 255).
        /// AutoTDP should only manage TDP when this is true.
        /// </summary>
        public bool IsInCustomMode
        {
            get
            {
                // If no Legion manager, assume custom mode (legacy devices)
                if (legionManager == null)
                    return true;

                // Check if Legion Go is detected
                if (!(legionManager.LegionGoDetected?.Value ?? false))
                    return true;

                // Check if in Custom mode (255)
                return legionManager.CurrentPerformanceMode == 255;
            }
        }

        // Quick Metrics push timer (pushes bundled sensor data to widget when enabled)
        private System.Timers.Timer quickMetricsTimer;
        private bool quickMetricsEnabled;

        /// <summary>
        /// Gets or sets whether Quick Metrics push is enabled.
        /// When enabled, pushes bundled sensor data (battery, CPU, GPU usage) to the widget every second.
        /// </summary>
        public bool QuickMetricsEnabled
        {
            get => quickMetricsEnabled;
            set
            {
                if (quickMetricsEnabled == value) return;
                quickMetricsEnabled = value;
                UpdateQuickMetricsTimerState();
            }
        }

        private System.Timers.Timer currentTdpTimer;
        private string lastTdpString = "";
        private int consecutiveReadFailures = 0;

        // Legion Go support for manufacturer WMI TDP
        private LegionManager legionManager;

        // PawnIO availability (RyzenSMU TDP control removed — AMD-only)
        private bool pawnIOAvailable;

        // WinRing0 removed - deprecated TDP method, no longer bundled
        // private bool winRing0Available;
        // private const string WinRing0BackupFolder = @"C:\GoTweaks";

        // PawnIO driver installation status
        private bool pawnIOInstalled;

        // Intel KX TDP service (Lunar Lake MCHBAR PL1/PL2) — ported 1:1 from HC KX.cs
        private Intel.KX kx;
        // Cached Intel TDP values (last written, shown in OSD until next write)
        private int intelLastPL1 = -1;
        private int intelLastPL2 = -1;

        // Thread synchronization locks to prevent race conditions
        private readonly object tdpLock = new object();

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
            // PawnIO/RyzenSMU TDP control was AMD-only and has been removed.
            // Intel devices use MSI ACPI WMI / kx.exe for TDP instead.
            pawnIOAvailable = false;
            pawnIOAvailableProperty?.SetAvailable(false);
            Logger.Info("PawnIO/RyzenSMU TDP control removed (AMD-only); using WMI/kx.exe on Intel.");
        }
        /// <summary>
        /// Initializes the Intel KX.exe TDP service for Lunar Lake (Core Ultra 200V) devices.
        /// Safe to call on any device — silently unavailable when kx.exe is absent.
        /// </summary>
        public void InitializeIntelTDP()
        {
            try
            {
                kx = new Intel.KX();
                bool ok = kx.init();
                Logger.Info($"Intel KX: init={ok}, available={kx.IsAvailable}");
            }
            catch (Exception ex)
            {
                Logger.Error($"InitializeIntelTDP failed: {ex.Message}");
                kx = null;
            }
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

                    // Log sensors for key hardware types (mirrors IntelGameBar LibreHardwareProvider diagnostic)
                    if (hardware.HardwareType == HardwareType.Cpu ||
                        hardware.HardwareType == HardwareType.Battery ||
                        hardware.HardwareType == HardwareType.GpuNvidia ||
                        hardware.HardwareType == HardwareType.GpuIntel ||
                        hardware.HardwareType == HardwareType.GpuAmd)
                    {
                        hardware.Update();
                        foreach (ISensor sensor in hardware.Sensors)
                        {
                            Logger.Info($"  [LHM] {hardware.HardwareType}/{hardware.Name} → Sensor '{sensor.Name}' ({sensor.SensorType}) = {sensor.Value}");
                        }
                        // Also log sub-hardware sensors (Intel iGPU may appear as CPU sub-hardware on Lunar Lake)
                        foreach (IHardware subHardware in hardware.SubHardware)
                        {
                            subHardware.Update();
                            foreach (ISensor sensor in subHardware.Sensors)
                            {
                                Logger.Info($"  [LHM] {hardware.HardwareType}/{hardware.Name}/{subHardware.Name} → Sensor '{sensor.Name}' ({sensor.SensorType}) = {sensor.Value}");
                            }
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

                    // Log GPU sensor inventory so we can diagnose OSD "GPU 71% N/A N/A N/A"
                    // patterns (LibreHardwareMonitor not exposing temp/power/clock on
                    // certain AMD APUs). This is the only diagnostic visible after a
                    // hibernate resume — the original startup enumeration may have rolled
                    // off the log file by the time the user reports the issue.
                    if (hardware.HardwareType == HardwareType.GpuAmd ||
                        hardware.HardwareType == HardwareType.GpuNvidia ||
                        hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        Logger.Info($"ForceRefreshHardware: {hardware.HardwareType} '{hardware.Name}' sensor inventory:");
                        foreach (ISensor sensor in hardware.Sensors)
                        {
                            Logger.Info($"  {hardware.HardwareType} Sensor: Name='{sensor.Name}', Type={sensor.SensorType}, Value={sensor.Value}");
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

                // After accept-visitor has populated hardwareSensors, log the registered
                // GPU sensor objects' current values. -1 means LibreHardwareMonitor never
                // matched the expected name+type for that slot, which is exactly the
                // "OSD shows N/A" condition. Cross-reference with the sensor inventory
                // above to find what name LHM uses on this hardware.
                Logger.Info("ForceRefreshHardware: Registered GPU/VRAM sensor values after refresh:");
                Logger.Info($"  GPUUsage         = {GPUUsage.Value} (looking for SensorType=Load on GpuAmd/GpuNvidia/GpuIntel, names: GPU Core / D3D 3D / GPU)");
                Logger.Info($"  GPUClock         = {GPUClock.Value} (looking for SensorType=Clock, names: GPU Core)");
                Logger.Info($"  GPUWattage       = {GPUWattage.Value} (looking for SensorType=Power, names: GPU Core / GPU Package / GPU Power)");
                Logger.Info($"  GPUTemperature   = {GPUTemperature.Value} (looking for SensorType=Temperature, names: GPU VR SoC / GPU Core)");
                Logger.Info($"  GPUMemoryUsed    = {GPUMemoryUsed.Value}");
                Logger.Info($"  GPUMemoryFree    = {GPUMemoryFree.Value}");
                Logger.Info($"  GPUMemoryClock   = {GPUMemoryClock.Value} (looking for SensorType=Clock)");
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

            if (computer == null)
                return;

            // Reset sensors to -1 in a local set, then swap atomically after processing.
            // This prevents PushQuickMetrics (on a separate timer) from reading -1 mid-update.
            var pendingValues = new Dictionary<HardwareSensor, float>();
            foreach (var hardwareSensor in hardwareSensors)
            {
                pendingValues[hardwareSensor] = -1.0f;
            }

            computer.Accept(updateVisitor);
            foreach (IHardware hardware in computer.Hardware)
            {
                // Process sensors for this hardware
                ProcessHardwareSensors(hardware, pendingValues);

                // Also process sub-hardware sensors (some sensors like GPU temp are nested)
                foreach (IHardware subHardware in hardware.SubHardware)
                {
                    ProcessHardwareSensors(subHardware, pendingValues);
                }
            }

            // ADLX fallback for GPU metrics that LibreHardwareMonitor didn't fill.
            // On AMD APUs where LHM doesn't expose Power/Temperature/Clock sensor
            // names (Z2 series, observed on Mute's Legion Go 2), ADLX still provides
            // the values via IADLXGPUMetrics. Fill any -1 GPU/VRAM slot from ADLX
            // before we commit pendingValues to the public sensor.Value fields.
            FillGpuSensorsFromAdlxFallback(pendingValues);

            // Apply all values at once so PushQuickMetrics never sees partially-reset state
            foreach (var kvp in pendingValues)
            {
                kvp.Key.Value = kvp.Value;
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
        /// For GPU/VRAM sensors that LibreHardwareMonitor didn't fill (still -1 after
        /// the per-hardware processing loop), query ADLX's IADLXGPUMetrics and use its
        /// reading instead. This makes the OSD GPU line work on AMD APUs where LHM
        /// exposes Load + memory but not Power/Temperature/Clock — see Mute's Legion
        /// Go 2 Z2-series report 2026-05-04. ADLX-only metrics also stay at -1 on
        /// systems where ADLX itself can't provide them, so the OSD still falls back
        /// to "N/A" when neither source has the value.
        /// </summary>
        // ADLX (AMD GPU library) removed — Intel MSI Claw does not use AMD GPU APIs.
        // GPU metrics are sourced entirely from LibreHardwareMonitor.
        private void FillGpuSensorsFromAdlxFallback(Dictionary<HardwareSensor, float> pendingValues)
        {
            // No-op: ADLX not available on Intel hardware.
        }

        private static bool IsMissing(Dictionary<HardwareSensor, float> pending, HardwareSensor key)
        {
            return pending.TryGetValue(key, out float v) && v < 0;
        }

        /// <summary>
        /// Processes sensors for a given hardware device and updates pending values dictionary.
        /// Values are written to sensors atomically after all processing completes.
        /// </summary>
        private void ProcessHardwareSensors(IHardware hardware, Dictionary<HardwareSensor, float> pendingValues)
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
                        float currentValue = pendingValues[hardwareSensor];
                        bool currentIsValid = currentValue >= 0;
                        bool currentIsNonZero = currentValue > 0;
                        bool newIsValid = newValue >= 0;
                        bool newIsNonZero = newValue > 0;

                        // Update if:
                        // 1. Current value is invalid (-1), OR
                        // 2. New value is non-zero (prefer actual readings over 0)
                        // Don't overwrite a non-zero valid value with zero
                        if (!currentIsValid || newIsNonZero || (!currentIsNonZero && newIsValid))
                        {
                            pendingValues[hardwareSensor] = newValue;
                        }
                    }
                }
            }
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
                    // PL2-Boost is an absolute PL2 target value (not an offset).
                    // Enforce PL2 >= PL1+1 so hardware constraint is always met.
                    int fpptTarget = tdpBoostFPPT?.Value ?? (tdp + 3);
                    fppt = Math.Max(fpptTarget, tdp + 1);
                    sppt = fppt; // SPPT = FPPT for MSI Claw (single burst limit)
                    Logger.Info($"TDP Boost enabled: PL1={spl}W, PL2={fppt}W (target={fpptTarget}W)");
                }

                // Note: CurrentSPL/SPPT/FPPT are now updated from actual hardware values
                // in UpdateCurrentTDP, which is called via ScheduleVerificationRead after SetTDP

                Logger.Info($"SetTDP: method={tdpMethod}, legionDetected={legionDetected}, pawnIOAvailable={pawnIOAvailable}");

                // ── MSI Claw: always use MSI ACPI WMI regardless of selected TDP method ──
                // 1:1 port from HC ClawA1M.set_long_limit / set_short_limit.
                // kx.exe (IntelKxExe) targets a different MCHBAR interface and does NOT
                // work on the MSI Claw 8 AI. PawnIO/RyzenSMU is AMD-only. WMI is the
                // only working path on Lunar Lake MSI Claw.
                if (Devices.DeviceDetector.DetectDevice().DeviceType == Shared.Enums.DeviceType.MSIClaw)
                {
                    // PL2 must always be > PL1. Clamp to [spl+1 … 37W] (Lunar Lake max PL2).
                    // When TDP Boost is off, fppt == spl → msiPl2 = spl+1 (minimum burst).
                    // When PL2-Boost is active, fppt = spl + boost, capped at 37W.
                    const int MSI_CLAW_MAX_PL2 = 37;
                    int msiPl2 = Math.Min(Math.Max(fppt, spl + 1), MSI_CLAW_MAX_PL2);
                    bool ok = ApplyMsiClawTdp(spl, msiPl2);
                    if (ok)
                    {
                        intelLastPL1 = spl;
                        intelLastPL2 = msiPl2;
                        CurrentSPL  = spl;
                        CurrentSPPT = spl;   // No separate SPPT on MSI Claw (PL1 = sustained)
                        CurrentFPPT = msiPl2;
                        var newTdpString = $"PL1:{spl}W PL2:{msiPl2}W";
                        if (newTdpString != lastTdpString)
                        {
                            currentTdp.SetValue(newTdpString);
                            lastTdpString = newTdpString;
                        }
                    }
                    ScheduleVerificationRead();
                    return;
                }

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
                        // PawnIO/RyzenSMU was the AMD TDP path (removed). Fall through to Intel KX.exe.
                        Logger.Warn("PawnIO/RyzenSMU removed (AMD) — using Intel KX.exe");
                        goto case TdpMethod.IntelKxExe;

                    case TdpMethod.IntelKxExe:
                        if (kx?.IsAvailable == true)
                        {
                            // 1:1 from HC IntelProcessor.SetTDPLimit:
                            //   PowerType.Slow (sustained/PL1) → set_long_limit  → 0x59A0
                            //   PowerType.Fast (turbo/PL2)     → set_short_limit → 0x59A4
                            Logger.Info($"Using Intel KX to set TDP (long/PL1={spl}W, short/PL2={fppt}W)");
                            int r1 = kx.set_long_limit(spl);   // sustained (PL1, 0x59A0)
                            int r2 = kx.set_short_limit(fppt); // turbo     (PL2, 0x59A4)
                            if (r1 == 0 && r2 == 0)
                            {
                                intelLastPL1 = spl;
                                intelLastPL2 = fppt;
                                CurrentSPL  = intelLastPL1;
                                CurrentSPPT = intelLastPL1; // No separate SPPT on Intel
                                CurrentFPPT = intelLastPL2;
                                var newTdpString = $"PL1:{intelLastPL1}W PL2:{intelLastPL2}W";
                                if (newTdpString != lastTdpString)
                                {
                                    currentTdp.SetValue(newTdpString);
                                    lastTdpString = newTdpString;
                                }
                                return;
                            }
                            Logger.Warn($"Intel KX: Failed to set TDP (r1={r1} r2={r2})");
                        }
                        else
                        {
                            Logger.Warn("Intel KX: kx.exe not available or MCHBAR not probed");
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

        /// <summary>
        /// Applies PL1/PL2 on the MSI Claw. Prefers the Intel MCHBAR path (KX) — exactly what HC
        /// does by default, because the MSI EC's WMI silently clamps the sustained limit (PL1) back
        /// to ~15W on Lunar Lake. Falls back to the MSI ACPI WMI only when KX is unavailable.
        /// </summary>
        private bool ApplyMsiClawTdp(int pl1, int pl2)
        {
            // The Claw is controlled via the MSI ACPI WMI (exactly like HC's WMI/OEM mode, which the
            // user confirmed holds high TDP without dropping). The one-time MsiClawTdpUnlock() below
            // does what HC does at device Open — deactivate MSI's auto power-shift and raise the EC
            // ceiling — which is what lets the WMI hold > ~15W (that unlock is cleared on reboot, so
            // HC re-does it every launch; we never did, which is why high TDP dropped).
            EnsureMsiClawTdpUnlock();

            bool wmiOk = SetMsiAcpiTDP(pl1, pl2);
            if (!wmiOk) Logger.Warn("[MSIClaw] MSI ACPI WMI: Failed to set TDP — ensure MSI Center M is stopped");
            else Logger.Info($"[MSIClaw] SetTDP via MSI ACPI WMI: PL1/SPL={pl1}W, PL2/sPPT={pl2}W");

            // Mirror HC's PowerProfileManager_Applied: after writing PL1/PL2, set the MSI power-shift
            // to the performance scenario the EC should sustain. HC uses Deactive only transiently at
            // Open(); during actual use it sets a *concrete* shift type per profile, and for any custom
            // / high-perf profile that is SportMode. Without this the EC reverts the sustained limit to
            // its conservative default (~15W) once the turbo window (Tau) elapses — exactly our drop.
            // On battery HC uses ShiftType.None. We re-assert this on every apply (incl. the 20s timer).
            SetMsiPowerShiftForTdp(pl1);

            return wmiOk;
        }

        /// <summary>
        /// Sets the MSI power-shift scenario (EC dataBlock 210) to match HC's
        /// PowerProfileManager_Applied. HC maps profile→shift: BestPerformance/custom→SportMode,
        /// BetterPerformance→GreenMode, BetterBattery→ECO, and ShiftType.None on battery (DC).
        ///
        /// HC's SetShiftMode(ChangeToCurrentShiftType, type) resolves deterministically to a fixed
        /// EC value: ((cur &amp; 195) | 192) &amp; 252 == 0xC0 base, then + the per-type offset
        /// (Sport+4, Comfort+0, Green+1, ECO+2, User+3). So:
        ///   Sport=0xC4(196) Comfort=0xC0(192) Green=0xC1(193) ECO=0xC2(194) None=0xC0(192).
        /// We write the value directly (the base bits are forced regardless of the current value, so
        /// no Get_Data round-trip is needed — the same reason our Deactive write worked).
        /// </summary>
        private void SetMsiPowerShiftForTdp(int pl1)
        {
            try
            {
                const string scope = "root\\WMI";
                const string path  = "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'";

                bool onBattery = IsOnBattery();
                int shiftValue;
                string label;
                if (onBattery)            { shiftValue = 0xC0; label = "None (DC)"; }     // HC: ShiftType.None on battery
                else if (pl1 <= 12)       { shiftValue = 0xC2; label = "ECO"; }           // BetterBattery
                else if (pl1 <= 20)       { shiftValue = 0xC1; label = "GreenMode"; }     // BetterPerformance
                else                      { shiftValue = 0xC4; label = "SportMode"; }     // BestPerformance / custom high TDP

                // Only write when the scenario actually changes. Re-writing block 210 on every TDP
                // apply (which happens often) makes the EC re-grab the fan with the scenario's own
                // aggressive curve, fighting our software fan table. HC writes the shift once per
                // profile change, not per TDP tick — match that.
                if (shiftValue == lastMsiShiftValue) return;

                bool ok = SetMsiCpuPowerLimit(scope, path, 210, shiftValue);
                Logger.Info($"[MSIClaw] PowerShift set to {label} (0x{shiftValue:X2}, pl1={pl1}W) — ok={ok}, like HC PowerProfileManager_Applied");
                if (!ok) return;
                lastMsiShiftValue = shiftValue;

                // Activating a shift scenario makes the EC re-assert its own fan profile, overriding
                // our software fan table. HC always (re)applies the fan table + control together with
                // the shift; do the same here so our curve wins. No-op in firmware fan mode.
                XboxGamingBarHelper.Program.ReassertMsiFanAfterShift();
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MSIClaw] SetMsiPowerShiftForTdp failed: {ex.Message}");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS { public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte SystemStatusFlag; public uint BatteryLifeTime; public uint BatteryFullLifeTime; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

        /// <summary>True when running on battery (DC). Defaults to false (AC) if the status is unknown.</summary>
        private static bool IsOnBattery()
        {
            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps))
                    return sps.ACLineStatus == 0; // 0 = offline/battery, 1 = online/AC, 255 = unknown
            }
            catch { }
            return false;
        }

        private bool msiClawUnlockDone;
        // Last MSI power-shift value written to EC block 210. -1 = none yet. Used to avoid re-writing
        // the shift (and thereby re-triggering the EC's scenario fan) on every TDP apply.
        private int lastMsiShiftValue = -1;

        /// <summary>
        /// Replicates HC ClawA1M/A2VM.Open()'s runtime TDP unlock (once per helper run; the EC clears
        /// it on reboot so it must be re-applied each launch):
        ///   1. SetShiftMode(Deactive) — disable MSI's auto power-shift so manual limits are honoured.
        ///      HC: v = (GetShiftValue() &amp; 195) | 128 &amp; 191; SetShiftValue(v) → dataBlock 210.
        ///      For the "deactive" case this resolves to 0x80 for the normal shift states, so we write
        ///      0x80 directly (no fragile Get_Data round-trip).
        ///   2. set_long_limit(35) + set_short_limit(37) — raise the EC's power-limit ceiling
        ///      ("unlock TDP for Lunar Lake" in HC). The user's actual value is applied right after.
        /// </summary>
        private void EnsureMsiClawTdpUnlock()
        {
            if (msiClawUnlockDone) return;
            msiClawUnlockDone = true;
            try
            {
                // 0. Enable the MSI OverBoost support flag (UEFI MsiDCVarData box[1]=1), like HC's
                //    InitOverBoost(true) at Open(). Persistent firmware NV — without it the EC clamps
                //    sustained power to ~15W no matter what PL1/PL2 we push.
                Devices.MSIClaw.MsiOverBoost.EnsureOverBoostEnabled();

                // 1. Raise the ceiling (PL1=35, PL2=37 — HC's A2VM "unlock TDP for Lunar Lake").
                //    The concrete power-shift (Sport/Green/ECO) is set per-apply in SetMsiPowerShiftForTdp,
                //    mirroring HC: HC only uses Deactive transiently at Open() and immediately overwrites
                //    it with a real shift type on the first profile apply. Forcing Deactive permanently
                //    was our bug — it lets the EC fall back to ~15W sustained after Tau.
                bool unlockOk = SetMsiAcpiTDP(35, 37);
                Logger.Info($"[MSIClaw] TDP unlock applied (ceiling35/37={unlockOk}) — like HC Open()");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MSIClaw] TDP unlock failed: {ex.Message}");
            }
        }

        // ── MSI ACPI WMI TDP helpers ───────────────────────────────────────────────
        // 1:1 port from HC ClawA1M.SetCPUPowerLimit + HandheldCompanion.WMI.Set.
        // HC reference:
        //   ClawA1M.set_long_limit  → SetCPUPowerLimit(80, limit) → PL1/SPL (sustained)
        //   ClawA1M.set_short_limit → SetCPUPowerLimit(81, limit) → PL2/sPPT (boost)
        //   WMI.Set(scope, path, "Set_Data", fullPackage)

        /// <summary>
        /// Sets PL1 (sustained/slow) and PL2 (boost/fast) via MSI ACPI WMI.
        /// 1:1 from HC ClawA1M.set_long_limit / set_short_limit.
        /// </summary>
        private bool SetMsiAcpiTDP(int pl1, int pl2)
        {
            const string scope = "root\\WMI";
            const string path  = "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'";
            bool ok1 = SetMsiCpuPowerLimit(scope, path, 80, pl1);  // dataBlock 80 = PL1/SPL
            bool ok2 = SetMsiCpuPowerLimit(scope, path, 81, pl2);  // dataBlock 81 = PL2/sPPT
            return ok1 && ok2;
        }

        /// <summary>
        /// Sends a single MSI_ACPI Set_Data WMI call.
        /// 1:1 from HC WMI.Set() called by ClawA1M.SetCPUPowerLimit().
        ///
        /// Package layout (32 bytes):
        ///   [0] = dataBlockIndex  (80=PL1, 81=PL2)
        ///   [1] = watts
        ///   [2..31] = 0x00
        /// </summary>
        private bool SetMsiCpuPowerLimit(string scope, string path, int dataBlockIndex, int watts)
        {
            try
            {
                byte[] fullPackage = new byte[32];
                fullPackage[0] = (byte)dataBlockIndex;
                fullPackage[1] = (byte)watts;
                // bytes [2..31] are already 0x00

                using (var obj = new ManagementObject(scope, path, null))
                {
                    ManagementBaseObject inParams     = null;
                    ManagementBaseObject inParamsData = null;
                    bool parametersAvailable          = false;

                    try
                    {
                        inParams     = obj.GetMethodParameters("Set_Data");
                        inParamsData = inParams?["Data"] as ManagementBaseObject;
                        parametersAvailable = (inParams != null && inParamsData != null);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"[MSIClaw] SetMsiCpuPowerLimit({dataBlockIndex},{watts}): GetMethodParameters failed: {ex.Message}");
                    }

                    if (!parametersAvailable)
                    {
                        // HC fallback path — try Get_WMI to obtain in-parameters
                        try
                        {
                            inParams     = obj.InvokeMethod("Get_WMI", null, null);
                            inParamsData = inParams?["Data"] as ManagementBaseObject;
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"[MSIClaw] SetMsiCpuPowerLimit({dataBlockIndex},{watts}): Get_WMI fallback failed: {ex.Message}");
                        }
                    }

                    if (inParams == null || inParamsData == null)
                    {
                        Logger.Warn($"[MSIClaw] SetMsiCpuPowerLimit({dataBlockIndex},{watts}): Could not obtain WMI parameters");
                        return false;
                    }

                    inParamsData.SetPropertyValue("Bytes", fullPackage);
                    inParams.SetPropertyValue("Data", inParamsData);
                    obj.InvokeMethod("Set_Data", inParams, null);

                    Logger.Info($"[MSIClaw] SetMsiCpuPowerLimit: dataBlock={dataBlockIndex}, watts={watts} — OK");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MSIClaw] SetMsiCpuPowerLimit({dataBlockIndex},{watts}): {ex.Message}");
                return false;
            }
        }

        private void UpdateCurrentTDP(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // MSI Claw: always show PL1/PL2 from last SetMsiAcpiTDP call.
                // Bypasses the Legion WMI path (which would show AMD SPL/SPPL/FPPT labels
                // and may return stale/wrong values from MSI ACPI WMI namespace).
                if (Devices.DeviceDetector.DetectDevice().DeviceType == Shared.Enums.DeviceType.MSIClaw)
                {
                    if (intelLastPL1 >= 0)
                    {
                        var msiTdpString = $"PL1:{intelLastPL1}W PL2:{intelLastPL2}W";
                        if (msiTdpString != lastTdpString)
                        {
                            currentTdp.SetValue(msiTdpString);
                            lastTdpString = msiTdpString;
                        }
                    }
                    return;
                }

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

                // Intel KX.exe: show cached values from last SetPowerLimits call
                // (spawning kx.exe every 2 seconds is too expensive for a read-back)
                if (intelLastPL1 >= 0)
                {
                    var newTdpString = $"PL1:{intelLastPL1}W PL2:{intelLastPL2}W";
                    if (newTdpString != lastTdpString)
                    {
                        currentTdp.SetValue(newTdpString);
                        lastTdpString = newTdpString;
                    }
                }
                return;

                // WinRing0 removed - deprecated TDP method, no longer bundled
                // RyzenAdj/WinRing0 TDP reading is no longer available

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

        /// <summary>
        /// Updates the Quick Metrics timer state based on QuickMetricsEnabled.
        /// </summary>
        private void UpdateQuickMetricsTimerState()
        {
            if (quickMetricsEnabled)
            {
                if (quickMetricsTimer == null)
                {
                    quickMetricsTimer = new System.Timers.Timer(1000); // 1 second interval
                    quickMetricsTimer.Elapsed += PushQuickMetrics;
                    quickMetricsTimer.AutoReset = true;
                }
                quickMetricsTimer.Start();
                Logger.Info("Quick Metrics push timer started");
            }
            else
            {
                if (quickMetricsTimer != null)
                {
                    quickMetricsTimer.Stop();
                    Logger.Info("Quick Metrics push timer stopped");
                }
            }
        }

        /// <summary>
        /// Pushes bundled metrics data to the widget.
        /// </summary>
        private void PushQuickMetrics(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // Build JSON with all sensor values for flexible display
                float batteryDrain = BatteryDischargeRate.Value > 0 ? BatteryDischargeRate.Value : -BatteryChargeRate.Value;
                float cpuUsage = CPUUsage.Value;
                float gpuUsage = GPUUsage.Value;
                float cpuTemp = CPUTemperature.Value;
                float gpuTemp = GPUTemperature.Value;
                float cpuWattage = CPUWattage.Value;
                float gpuWattage = GPUWattage.Value;
                float memoryUsage = MemoryUsage.Value;
                float batteryLevel = BatteryLevel.Value;
                float timeRemaining = BatteryTimeRemaining;
                float timeToFull = BatteryTimeToFull;
                bool isCharging = BatteryChargeRate.Value > 0;

                // Format as JSON with all metrics
                string json = $"{{" +
                    $"\"batteryDrain\":{batteryDrain:F1}," +
                    $"\"cpuUsage\":{cpuUsage:F0}," +
                    $"\"gpuUsage\":{gpuUsage:F0}," +
                    $"\"cpuTemp\":{cpuTemp:F0}," +
                    $"\"gpuTemp\":{gpuTemp:F0}," +
                    $"\"cpuWattage\":{cpuWattage:F1}," +
                    $"\"gpuWattage\":{gpuWattage:F1}," +
                    $"\"memoryUsage\":{memoryUsage:F0}," +
                    $"\"batteryLevel\":{batteryLevel:F0}," +
                    $"\"timeRemaining\":{timeRemaining:F0}," +
                    $"\"timeToFull\":{timeToFull:F0}," +
                    $"\"isCharging\":{(isCharging ? "true" : "false")}}}";

                // Send via named pipe
                var message = new Shared.IPC.PipeMessage
                {
                    Command = Shared.Enums.Command.Response,
                    Function = Shared.Enums.Function.QuickMetrics,
                    Content = json
                };

                Program.SendPipeMessage(message);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error pushing Quick Metrics: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("PerformanceManager: Disposing resources");

                // Stop and dispose Quick Metrics timer
                if (quickMetricsTimer != null)
                {
                    quickMetricsTimer.Stop();
                    quickMetricsTimer.Elapsed -= PushQuickMetrics;
                    quickMetricsTimer.Dispose();
                    quickMetricsTimer = null;
                    Logger.Info("PerformanceManager: Quick Metrics timer disposed");
                }

                // Stop and dispose the timer
                if (currentTdpTimer != null)
                {
                    currentTdpTimer.Stop();
                    currentTdpTimer.Elapsed -= UpdateCurrentTDP;
                    currentTdpTimer.Dispose();
                    currentTdpTimer = null;
                    Logger.Info("PerformanceManager: Timer disposed");
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
