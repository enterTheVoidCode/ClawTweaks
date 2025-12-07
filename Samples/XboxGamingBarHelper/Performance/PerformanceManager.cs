using LibreHardwareMonitor.Hardware;
using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Legion;
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

        private System.Timers.Timer currentTdpTimer;
        private string lastTdpString = "";
        private int consecutiveReadFailures = 0;

        // Legion Go support for manufacturer WMI TDP
        private LegionManager legionManager;

        // PawnIO/RyzenSMU support for anti-cheat compatible TDP control
        private RyzenSmuService ryzenSmuService;
        private bool pawnIOAvailable;

        // RyzenAdj lazy loading - only load when user disables Manufacturer WMI
        private bool ryzenAdjInitialized = false;
        private bool ryzenAdjInitAttempted = false;

        /// <summary>
        /// Gets whether PawnIO is available for TDP control.
        /// </summary>
        public bool IsPawnIOAvailable => pawnIOAvailable;

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
        }
        private const int MaxConsecutiveFailuresBeforeReinit = 5;
        private const double NormalTimerInterval = 3000; // 3 seconds
        private const double BackoffTimerInterval = 10000; // 10 seconds during failures

        internal PerformanceManager(AppServiceConnection connection) : base(connection)
        {
            // Initialize the computer sensors
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsBatteryEnabled = true,
            };
            updateVisitor = new UpdateVisitor();
            computer.Open();
            computer.Accept(updateVisitor);

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
            }

            // Initialize hardware sensors
            Logger.Info("Initializing hardware sensors...");
            CPUClock = new CPUClockSensor();
            CPUUsage = new CPUUsageSensor();
            CPUWattage = new CPUWattageSensor();
            CPUTemperature = new CPUTemperatureSensor();
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
            NetworkDownload = new NetworkDownloadSensor();
            NetworkUpload = new NetworkUploadSensor();

            hardwareSensors = new List<HardwareSensor>()
            {
                CPUClock,
                CPUUsage,
                CPUWattage,
                CPUTemperature,
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
                NetworkDownload,
                NetworkUpload,
            };

            // RyzenAdj is NOT initialized at startup to avoid loading WinRing0 driver
            // WinRing0 may trigger anti-cheat systems like EAC
            // RyzenAdj will be lazy-loaded only when user disables Manufacturer WMI TDP
            Logger.Info("RyzenAdj initialization deferred (will load on demand if Manufacturer WMI is disabled)");
            var initialTDP = 25;
            var initialCurrentTDP = "-- W";

            Logger.Info("Creating TDP properties...");
            tdp = new TDPProperty(initialTDP, null, this);
            currentTdp = new CurrentTDPProperty(initialCurrentTDP, null, this);
            lastTdpString = initialCurrentTDP;

            // Set up timer to update current TDP every 3 seconds
            currentTdpTimer = new System.Timers.Timer(NormalTimerInterval);
            currentTdpTimer.Elapsed += UpdateCurrentTDP;
            currentTdpTimer.AutoReset = true;
            currentTdpTimer.Start();
            Logger.Info("CurrentTDP timer started, updating every 3 seconds");
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

            foreach (var hardwareSensor in hardwareSensors)
            {
                hardwareSensor.Value = -1.0f;
            }

            if (computer == null)
                return;

            computer.Accept(updateVisitor);
            foreach (IHardware hardware in computer.Hardware)
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    //Logger.Info("[2] Hardware {3} Sensor: {0}, value: {1}, type: {2}", sensor.Name, sensor.Value, sensor.SensorType.ToString(), hardware.Name);

                    HardwareSensor hardwareSensorFound = null;
                    foreach (var hardwareSensor in hardwareSensors)
                    {
                        if (hardwareSensor.HardwareType == hardware.HardwareType && hardwareSensor.SensorType == sensor.SensorType && hardwareSensor.SensorName == sensor.Name)
                        {
                            hardwareSensorFound = hardwareSensor;
                            break;
                        }
                    }
                    if (hardwareSensorFound != null)
                    {
                        hardwareSensorFound.Value = sensor.Value ?? -1;
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
            var settingsManager = SettingsManager.GetInstance();
            bool useManufacturerWMI = settingsManager?.UseManufacturerWMI?.Value ?? true;
            bool legionDetected = legionManager?.LegionGoDetected?.Value ?? false;

            // Priority 1: Legion WMI (anti-cheat compatible, works on Legion Go/Go S)
            // Only use if the setting is enabled
            if (useManufacturerWMI && legionDetected && legionManager != null)
            {
                Logger.Info($"Using Legion WMI to set TDP to {tdp}W (SPL={tdp}, SPPL={tdp}, FPPT={tdp})");
                legionManager.SetCustomTDP(tdp, tdp, tdp);
                return;
            }

            // Priority 2: PawnIO/RyzenSMU (anti-cheat compatible, for non-Legion devices)
            // Note: Currently disabled - waiting for CPU support in RyzenSMU module
            // if (pawnIOAvailable && ryzenSmuService != null && ryzenSmuService.IsInitialized)
            // {
            //     Logger.Info($"Using PawnIO/RyzenSMU to set TDP to {tdp}W");
            //     if (ryzenSmuService.SetAllLimits(tdp, tdp, tdp))
            //     {
            //         Logger.Info($"PawnIO: TDP set to {tdp}W successfully");
            //         return;
            //     }
            //     else
            //     {
            //         Logger.Warn("PawnIO: Failed to set TDP, falling back to RyzenAdj");
            //     }
            // }

            // Priority 3: RyzenAdj (fallback - loads WinRing0 which may trigger anti-cheat)
            // Lazy-load RyzenAdj only when actually needed
            if (!EnsureRyzenAdjInitialized())
            {
                Logger.Warn("SetTDP: No TDP control method available (Legion WMI disabled, RyzenAdj failed to initialize)");
                return;
            }

            Logger.Info($"Using RyzenAdj to set TDP to {tdp}W (WinRing0 loaded - may trigger anti-cheat)");
            RyzenAdj.set_fast_limit(ryzenAdjHandle, (uint)(tdp * 1000));
            RyzenAdj.set_slow_limit(ryzenAdjHandle, (uint)(tdp * 1000));
            RyzenAdj.set_stapm_limit(ryzenAdjHandle, (uint)(tdp * 1000));
#if DEBUG
            RyzenAdj.refresh_table(ryzenAdjHandle);
            Logger.Info($"Set TDP to {tdp}, current TDP is {RyzenAdj.get_fast_limit(ryzenAdjHandle)}");
#endif
        }

        /// <summary>
        /// Lazy-loads RyzenAdj when needed. This loads WinRing0 driver which may trigger anti-cheat.
        /// Only call this when user explicitly disables Manufacturer WMI TDP.
        /// </summary>
        /// <returns>True if RyzenAdj is available</returns>
        private bool EnsureRyzenAdjInitialized()
        {
            if (ryzenAdjInitialized)
                return ryzenAdjHandle != IntPtr.Zero;

            if (ryzenAdjInitAttempted)
                return false; // Already tried and failed

            ryzenAdjInitAttempted = true;
            Logger.Info("EnsureRyzenAdjInitialized: Loading RyzenAdj (WinRing0 driver will be loaded - may trigger anti-cheat)");

            try
            {
                ryzenAdjHandle = RyzenAdj.init_ryzenadj();
                if (ryzenAdjHandle == IntPtr.Zero)
                {
                    Logger.Warn("RyzenAdj initialization failed");
                    return false;
                }

                RyzenAdj.refresh_table(ryzenAdjHandle);
                var stapm = (int)RyzenAdj.get_stapm_limit(ryzenAdjHandle);
                var fast = (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
                var slow = (int)RyzenAdj.get_slow_limit(ryzenAdjHandle);

                if (stapm > 0 && fast > 0 && slow > 0 &&
                    stapm != int.MinValue && fast != int.MinValue && slow != int.MinValue)
                {
                    Logger.Info($"RyzenAdj initialized successfully - STAPM:{stapm}W FAST:{fast}W SLOW:{slow}W");
                    ryzenAdjInitialized = true;
                    return true;
                }
                else
                {
                    Logger.Warn($"RyzenAdj returned invalid values - STAPM:{stapm}W FAST:{fast}W SLOW:{slow}W");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"RyzenAdj initialization failed: {ex.Message}");
                ryzenAdjHandle = IntPtr.Zero;
                return false;
            }
        }

        private void ReinitializeRyzenAdj()
        {
            Logger.Info("ReinitializeRyzenAdj: Attempting to reinitialize RyzenAdj handle");

            // Clean up old handle if it exists
            if (ryzenAdjHandle != IntPtr.Zero)
            {
                try
                {
                    RyzenAdj.cleanup_ryzenadj(ryzenAdjHandle);
                    Logger.Info("ReinitializeRyzenAdj: Old handle cleaned up");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"ReinitializeRyzenAdj: Error cleaning up old handle: {ex.Message}");
                }
            }

            // Reset flags and try again
            ryzenAdjInitialized = false;
            ryzenAdjInitAttempted = false;

            if (EnsureRyzenAdjInitialized())
            {
                consecutiveReadFailures = 0;
            }
        }

        private (int stapm, int fast, int slow) TryReadTdpValues()
        {
            RyzenAdj.refresh_table(ryzenAdjHandle);
            var stapm = (int)RyzenAdj.get_stapm_limit(ryzenAdjHandle);
            var fast = (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
            var slow = (int)RyzenAdj.get_slow_limit(ryzenAdjHandle);
            return (stapm, fast, slow);
        }

        private bool AreValuesValid(int stapm, int fast, int slow)
        {
            return stapm > 0 && fast > 0 && slow > 0 &&
                   stapm != int.MinValue && fast != int.MinValue && slow != int.MinValue;
        }

        private void UpdateCurrentTDP(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // Check if manufacturer WMI is enabled
                var settingsManager = SettingsManager.GetInstance();
                bool useManufacturerWMI = settingsManager?.UseManufacturerWMI?.Value ?? true;
                bool legionDetected = legionManager?.LegionGoDetected?.Value ?? false;

                // Priority 1: Legion WMI (when enabled and Legion is detected)
                if (useManufacturerWMI && legionDetected && legionManager != null)
                {
                    // Use Legion WMI to get TDP values
                    var (slow, fast, peak) = legionManager.GetCurrentTDPValues();

                    if (slow.HasValue && fast.HasValue && peak.HasValue)
                    {
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

                // Fall back to RyzenAdj (lazy-load if not initialized)
                if (!EnsureRyzenAdjInitialized())
                {
                    Logger.Debug("UpdateCurrentTDP: RyzenAdj not available");
                    return;
                }

                Logger.Debug("UpdateCurrentTDP: Reading TDP limits from hardware");

                // Try reading values with retry
                var (stapm, fastVal, slowVal) = TryReadTdpValues();

                // If first read fails, retry up to 2 more times with small delay
                int retryCount = 0;
                while (!AreValuesValid(stapm, fastVal, slowVal) && retryCount < 2)
                {
                    retryCount++;
                    Logger.Debug($"UpdateCurrentTDP: Read attempt {retryCount + 1} failed, retrying...");
                    System.Threading.Thread.Sleep(100); // Brief delay before retry
                    (stapm, fastVal, slowVal) = TryReadTdpValues();
                }

                // Check for invalid values (int.MinValue indicates read failure)
                if (!AreValuesValid(stapm, fastVal, slowVal))
                {
                    consecutiveReadFailures++;
                    Logger.Debug($"UpdateCurrentTDP: Invalid values read (STAPM:{stapm}, FAST:{fastVal}, SLOW:{slowVal}), failure count: {consecutiveReadFailures}");

                    // Backoff timer to reduce CPU usage during failures
                    if (currentTdpTimer != null && currentTdpTimer.Interval != BackoffTimerInterval)
                    {
                        currentTdpTimer.Interval = BackoffTimerInterval;
                        Logger.Info($"UpdateCurrentTDP: Backing off timer to {BackoffTimerInterval}ms due to failures");
                    }

                    // If we've had too many consecutive failures, try reinitializing
                    if (consecutiveReadFailures >= MaxConsecutiveFailuresBeforeReinit)
                    {
                        Logger.Warn($"UpdateCurrentTDP: {consecutiveReadFailures} consecutive read failures, reinitializing RyzenAdj");
                        ReinitializeRyzenAdj();
                    }
                    return;
                }

                // Reset failure counter on successful read
                if (consecutiveReadFailures > 0)
                {
                    Logger.Info($"UpdateCurrentTDP: Read succeeded after {consecutiveReadFailures} failures");
                    consecutiveReadFailures = 0;

                    // Restore normal timer interval
                    if (currentTdpTimer != null && currentTdpTimer.Interval != NormalTimerInterval)
                    {
                        currentTdpTimer.Interval = NormalTimerInterval;
                        Logger.Info($"UpdateCurrentTDP: Restored timer to {NormalTimerInterval}ms");
                    }
                }

                // Only show limits (power consumption methods not working on this hardware)
                var newTdpStringRyzen = $"STAPM:{stapm}W FAST:{fastVal}W SLOW:{slowVal}W";
                Logger.Debug($"UpdateCurrentTDP: Read values - {newTdpStringRyzen}");

                // Only update if value has changed to reduce IPC traffic
                if (newTdpStringRyzen != lastTdpString)
                {
                    Logger.Info($"UpdateCurrentTDP: Value changed from '{lastTdpString}' to '{newTdpStringRyzen}', sending update");
                    currentTdp.SetValue(newTdpStringRyzen);
                    lastTdpString = newTdpStringRyzen;
                }
                else
                {
                    Logger.Debug($"UpdateCurrentTDP: Value unchanged ({newTdpStringRyzen}), skipping update");
                }
            }
            catch (Exception ex)
            {
                consecutiveReadFailures++;
                Logger.Error($"Error updating current TDP: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");

                if (consecutiveReadFailures >= MaxConsecutiveFailuresBeforeReinit)
                {
                    Logger.Warn($"UpdateCurrentTDP: {consecutiveReadFailures} consecutive failures with exceptions, reinitializing RyzenAdj");
                    ReinitializeRyzenAdj();
                }
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
