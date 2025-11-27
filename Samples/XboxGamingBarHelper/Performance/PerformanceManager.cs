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

        /// <summary>
        /// Sets the Legion Manager reference for WMI TDP support.
        /// Must be called after LegionManager is initialized.
        /// </summary>
        public void SetLegionManager(LegionManager manager)
        {
            legionManager = manager;
            Logger.Info($"LegionManager reference set. Legion detected: {manager?.LegionGoDetected?.Value ?? false}");
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
                BatteryLevel,
                BatteryRemainingTime,
                BatteryDischargeRate,
                BatteryChargeRate,
                NetworkDownload,
                NetworkUpload,
            };

            ryzenAdjHandle = RyzenAdj.init_ryzenadj();
            var initialTDP = 25;
            var initialCurrentTDP = "-- W";
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Logger.Error("Failed to initialize RyzenAdj");
            }
            else
            {
                RyzenAdj.refresh_table(ryzenAdjHandle);
                // RyzenAdj.set_fast_limit(ryzenAdjHandle, 30000);
                var stapm = (int)RyzenAdj.get_stapm_limit(ryzenAdjHandle);
                var fast = (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
                var slow = (int)RyzenAdj.get_slow_limit(ryzenAdjHandle);

                // Check for valid values before using them
                if (fast > 0 && fast != int.MinValue)
                {
                    initialTDP = fast;
                }
                if (stapm > 0 && fast > 0 && slow > 0 &&
                    stapm != int.MinValue && fast != int.MinValue && slow != int.MinValue)
                {
                    initialCurrentTDP = $"STAPM:{stapm}W FAST:{fast}W SLOW:{slow}W";
                    Logger.Info($"RyzenAdj initialized successfully - Stapm: {stapm}W, Fast: {fast}W, Slow: {slow}W");
                }
                else
                {
                    Logger.Warn($"RyzenAdj returned invalid values - Stapm: {stapm}W, Fast: {fast}W, Slow: {slow}W");
                }
            }

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
            // Check if we should use manufacturer WMI TDP instead of RyzenAdj
            var settingsManager = SettingsManager.GetInstance();
            bool useManufacturerWMI = settingsManager?.UseManufacturerWMI?.Value ?? false;
            bool legionDetected = legionManager?.LegionGoDetected?.Value ?? false;

            if (useManufacturerWMI && legionDetected && legionManager != null)
            {
                // Use Legion WMI method for TDP control
                // SetCustomTDP switches to Custom performance mode first if needed
                // then sets all three TDP values to the same value
                Logger.Info($"Using Legion WMI to set TDP to {tdp}W (SPL={tdp}, SPPL={tdp}, FPPT={tdp})");
                legionManager.SetCustomTDP(tdp, tdp, tdp);
                return;
            }

            // Fall back to RyzenAdj
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Logger.Info("RyzenAdj not initialized");
                return;
            }
            //RyzenAdj.refresh_table(ryzenAdjHandle);
            RyzenAdj.set_fast_limit(ryzenAdjHandle, (uint)(tdp * 1000));
            RyzenAdj.set_slow_limit(ryzenAdjHandle, (uint)(tdp * 1000));
            RyzenAdj.set_stapm_limit(ryzenAdjHandle, (uint)(tdp * 1000));
#if DEBUG
            RyzenAdj.refresh_table(ryzenAdjHandle);
            Logger.Info($"Set TDP to {tdp}, current TDP is {RyzenAdj.get_fast_limit(ryzenAdjHandle)}");
#endif
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

            // Initialize new handle
            ryzenAdjHandle = RyzenAdj.init_ryzenadj();
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Logger.Error("ReinitializeRyzenAdj: Failed to reinitialize RyzenAdj");
            }
            else
            {
                Logger.Info("ReinitializeRyzenAdj: Successfully reinitialized RyzenAdj");
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
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Logger.Debug("UpdateCurrentTDP: RyzenAdj handle is null, attempting reinit");
                ReinitializeRyzenAdj();
                if (ryzenAdjHandle == IntPtr.Zero)
                {
                    return;
                }
            }

            try
            {
                Logger.Debug("UpdateCurrentTDP: Reading TDP limits from hardware");

                // Try reading values with retry
                var (stapm, fast, slow) = TryReadTdpValues();

                // If first read fails, retry up to 2 more times with small delay
                int retryCount = 0;
                while (!AreValuesValid(stapm, fast, slow) && retryCount < 2)
                {
                    retryCount++;
                    Logger.Debug($"UpdateCurrentTDP: Read attempt {retryCount + 1} failed, retrying...");
                    System.Threading.Thread.Sleep(100); // Brief delay before retry
                    (stapm, fast, slow) = TryReadTdpValues();
                }

                // Check for invalid values (int.MinValue indicates read failure)
                if (!AreValuesValid(stapm, fast, slow))
                {
                    consecutiveReadFailures++;
                    Logger.Debug($"UpdateCurrentTDP: Invalid values read (STAPM:{stapm}, FAST:{fast}, SLOW:{slow}), failure count: {consecutiveReadFailures}");

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
                var newTdpString = $"STAPM:{stapm}W FAST:{fast}W SLOW:{slow}W";
                Logger.Debug($"UpdateCurrentTDP: Read values - {newTdpString}");

                // Only update if value has changed to reduce IPC traffic
                if (newTdpString != lastTdpString)
                {
                    Logger.Info($"UpdateCurrentTDP: Value changed from '{lastTdpString}' to '{newTdpString}', sending update");
                    currentTdp.SetValue(newTdpString);
                    lastTdpString = newTdpString;
                }
                else
                {
                    Logger.Debug($"UpdateCurrentTDP: Value unchanged ({newTdpString}), skipping update");
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
