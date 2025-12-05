using LegionGoLibrary;
using NLog;
using Shared.Enums;
using System;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Legion
{
    /// <summary>
    /// Manager for Legion Go hardware features including controller RGB, touchpad, and performance modes.
    /// </summary>
    internal class LegionManager : Manager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Legion Go services
        private LegionControllerService controllerService;
        private LenovoWMIService wmiService;

        // Device detection
        private bool isLegionGoDetected = false;
        private bool isControllerConnected = false;

        // Current settings state (cached)
        private bool touchpadEnabled = true;
        private int lightMode = 1; // Solid
        private string lightColor = "#FFFFFF";
        private int lightBrightness = 100;
        private int lightSpeed = 50; // 0-100, default 50%
        private int performanceMode = 2; // Balanced
        private int customTDPSlow = 15;
        private int customTDPFast = 25;
        private int customTDPPeak = 35;
        private bool fanFullSpeed = false;
        private bool gyroEnabled = true;
        private int vibrationLevel = 2; // Medium
        private bool powerLightEnabled = true;
        private bool chargeLimitEnabled = false;

        // Fan speed (RPM)
        private int cpuFanSpeed = 0;

        // TDP reapply timer (used when switching to Custom mode)
        private System.Timers.Timer tdpReapplyTimer;
        private int pendingTdpSlow;
        private int pendingTdpFast;
        private int pendingTdpPeak;

        // Properties
        public readonly LegionGoDetectedProperty LegionGoDetected;
        public readonly LegionTouchpadEnabledProperty LegionTouchpadEnabled;
        public readonly LegionLightModeProperty LegionLightMode;
        public readonly LegionLightColorProperty LegionLightColor;
        public readonly LegionLightBrightnessProperty LegionLightBrightness;
        public readonly LegionLightSpeedProperty LegionLightSpeed;
        public readonly LegionPerformanceModeProperty LegionPerformanceMode;
        public readonly LegionCustomTDPSlowProperty LegionCustomTDPSlow;
        public readonly LegionCustomTDPFastProperty LegionCustomTDPFast;
        public readonly LegionCustomTDPPeakProperty LegionCustomTDPPeak;
        public readonly LegionFanFullSpeedProperty LegionFanFullSpeed;
        public readonly LegionGyroEnabledProperty LegionGyroEnabled;
        public readonly LegionVibrationProperty LegionVibration;
        public readonly LegionPowerLightProperty LegionPowerLight;
        public readonly LegionChargeLimitProperty LegionChargeLimit;

        public LegionManager(AppServiceConnection connection) : base(connection)
        {
            Logger.Info("Initializing Legion Manager...");

            // Try to detect Legion Go device
            DetectLegionGo();

            // Initialize properties (pass this as manager)
            LegionGoDetected = new LegionGoDetectedProperty(isLegionGoDetected, this);
            LegionTouchpadEnabled = new LegionTouchpadEnabledProperty(touchpadEnabled, this);
            LegionLightMode = new LegionLightModeProperty(lightMode, this);
            LegionLightColor = new LegionLightColorProperty(lightColor, this);
            LegionLightBrightness = new LegionLightBrightnessProperty(lightBrightness, this);
            LegionLightSpeed = new LegionLightSpeedProperty(lightSpeed, this);
            LegionPerformanceMode = new LegionPerformanceModeProperty(performanceMode, this);
            LegionCustomTDPSlow = new LegionCustomTDPSlowProperty(customTDPSlow, this);
            LegionCustomTDPFast = new LegionCustomTDPFastProperty(customTDPFast, this);
            LegionCustomTDPPeak = new LegionCustomTDPPeakProperty(customTDPPeak, this);
            LegionFanFullSpeed = new LegionFanFullSpeedProperty(fanFullSpeed, this);
            LegionGyroEnabled = new LegionGyroEnabledProperty(gyroEnabled, this);
            LegionVibration = new LegionVibrationProperty(vibrationLevel, this);
            LegionPowerLight = new LegionPowerLightProperty(powerLightEnabled, this);
            LegionChargeLimit = new LegionChargeLimitProperty(chargeLimitEnabled, this);

            if (isLegionGoDetected)
            {
                // Read current performance mode and TDP values
                ReadCurrentPerformanceMode();
                ReadCurrentTDPValues();

                // Update properties with the values read from device
                // Use silent update to avoid triggering WMI calls back
                LegionPerformanceMode.SetValueSilent(performanceMode);
                LegionCustomTDPSlow.SetValueSilent(customTDPSlow);
                LegionCustomTDPFast.SetValueSilent(customTDPFast);
                LegionCustomTDPPeak.SetValueSilent(customTDPPeak);
                LegionFanFullSpeed.SetValueSilent(fanFullSpeed);
            }

            Logger.Info($"Legion Manager initialized. Legion Go detected: {isLegionGoDetected}");
        }

        private void DetectLegionGo()
        {
            try
            {
                // Try WMI detection first (works even without controllers attached)
                wmiService = new LenovoWMIService();
                var classes = wmiService.ListWMIClasses();

                if (classes.Success && classes.Classes != null)
                {
                    // Check for Legion Go specific WMI classes
                    foreach (var className in classes.Classes)
                    {
                        if (className.Contains("GAMEZONE") || className.Contains("LEGION"))
                        {
                            Logger.Info($"Found Lenovo WMI class: {className}");
                            isLegionGoDetected = true;
                        }
                    }
                }

                // Try to connect to controller service
                controllerService = new LegionControllerService();
                var connectResult = controllerService.Connect();

                if (connectResult.Success)
                {
                    isControllerConnected = true;
                    isLegionGoDetected = true;
                    Logger.Info($"Legion Go controller connected: {connectResult.Message}");
                }
                else
                {
                    Logger.Info($"Legion Go controller not connected: {connectResult.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error detecting Legion Go: {ex.Message}");
                isLegionGoDetected = false;
            }
        }

        private void ReadCurrentPerformanceMode()
        {
            try
            {
                if (wmiService != null)
                {
                    var result = wmiService.GetSmartFanMode();
                    if (result.Success && result.Result.HasValue)
                    {
                        performanceMode = (int)result.Result.Value;
                        Logger.Info($"Current performance mode: {result.Result.Value}");
                    }

                    var fanFullResult = wmiService.GetFanFullSpeed();
                    if (fanFullResult.Success && fanFullResult.Result.HasValue)
                    {
                        fanFullSpeed = fanFullResult.Result.Value == 1;
                        Logger.Info($"Fan full speed: {fanFullSpeed}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading performance mode: {ex.Message}");
            }
        }

        private void ReadCurrentTDPValues()
        {
            try
            {
                if (wmiService != null)
                {
                    var slowResult = wmiService.GetCPUShortTermPowerLimit();
                    if (slowResult.Success && slowResult.Result.HasValue)
                    {
                        customTDPSlow = slowResult.Result.Value;
                        Logger.Info($"Current Slow TDP (SPL): {customTDPSlow}W");
                    }

                    var fastResult = wmiService.GetCPULongTermPowerLimit();
                    if (fastResult.Success && fastResult.Result.HasValue)
                    {
                        customTDPFast = fastResult.Result.Value;
                        Logger.Info($"Current Fast TDP (SPPL): {customTDPFast}W");
                    }

                    var peakResult = wmiService.GetCPUPeakPowerLimit();
                    if (peakResult.Success && peakResult.Result.HasValue)
                    {
                        customTDPPeak = peakResult.Result.Value;
                        Logger.Info($"Current Peak TDP (FPPT): {customTDPPeak}W");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading TDP values: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current TDP values from Legion WMI.
        /// Returns (slow/SPL, fast/SPPL, peak/FPPT) or null values if not available.
        /// </summary>
        public (int? slow, int? fast, int? peak) GetCurrentTDPValues()
        {
            int? slow = null, fast = null, peak = null;

            try
            {
                if (wmiService != null)
                {
                    var slowResult = wmiService.GetCPUShortTermPowerLimit();
                    if (slowResult.Success && slowResult.Result.HasValue)
                    {
                        slow = slowResult.Result.Value;
                    }

                    var fastResult = wmiService.GetCPULongTermPowerLimit();
                    if (fastResult.Success && fastResult.Result.HasValue)
                    {
                        fast = fastResult.Result.Value;
                    }

                    var peakResult = wmiService.GetCPUPeakPowerLimit();
                    if (peakResult.Success && peakResult.Result.HasValue)
                    {
                        peak = peakResult.Result.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting TDP values: {ex.Message}");
            }

            return (slow, fast, peak);
        }

        public void SetTouchpadEnabled(bool enabled)
        {
            if (!isControllerConnected || controllerService == null)
            {
                Logger.Warn("Cannot set touchpad: controller not connected");
                return;
            }

            try
            {
                var result = controllerService.SetTouchpadEnabled(enabled);
                if (result.Success)
                {
                    touchpadEnabled = enabled;
                    Logger.Info($"Touchpad {(enabled ? "enabled" : "disabled")}");
                }
                else
                {
                    Logger.Error($"Failed to set touchpad: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting touchpad: {ex.Message}");
            }
        }

        public void SetLightMode(int mode)
        {
            if (!isControllerConnected || controllerService == null)
            {
                Logger.Warn("Cannot set light mode: controller not connected");
                return;
            }

            try
            {
                // Parse current color
                ParseHexColor(lightColor, out byte r, out byte g, out byte b);

                RgbMode rgbMode = (RgbMode)mode;
                var result = controllerService.SetStickLightMode(rgbMode, r, g, b, lightBrightness / 100f, lightSpeed / 100f);
                if (result.Success)
                {
                    lightMode = mode;
                    Logger.Info($"Light mode set to {rgbMode}");
                }
                else
                {
                    Logger.Error($"Failed to set light mode: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting light mode: {ex.Message}");
            }
        }

        public void SetLightColor(string hexColor)
        {
            if (!isControllerConnected || controllerService == null)
            {
                Logger.Warn("Cannot set light color: controller not connected");
                return;
            }

            try
            {
                ParseHexColor(hexColor, out byte r, out byte g, out byte b);

                RgbMode rgbMode = (RgbMode)lightMode;
                var result = controllerService.SetStickLightMode(rgbMode, r, g, b, lightBrightness / 100f, lightSpeed / 100f);
                if (result.Success)
                {
                    lightColor = hexColor;
                    Logger.Info($"Light color set to {hexColor}");
                }
                else
                {
                    Logger.Error($"Failed to set light color: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting light color: {ex.Message}");
            }
        }

        private void ParseHexColor(string hexColor, out byte r, out byte g, out byte b)
        {
            // Default to white
            r = 255; g = 255; b = 255;

            if (string.IsNullOrEmpty(hexColor)) return;

            // Remove # if present
            string hex = hexColor.TrimStart('#');

            if (hex.Length == 6)
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
            }
        }

        public void SetLightBrightness(int brightness)
        {
            if (!isControllerConnected || controllerService == null)
            {
                Logger.Warn("Cannot set light brightness: controller not connected");
                return;
            }

            try
            {
                brightness = Math.Max(0, Math.Min(100, brightness));
                ParseHexColor(lightColor, out byte r, out byte g, out byte b);

                RgbMode rgbMode = (RgbMode)lightMode;
                var result = controllerService.SetStickLightMode(rgbMode, r, g, b, brightness / 100f, lightSpeed / 100f);
                if (result.Success)
                {
                    lightBrightness = brightness;
                    Logger.Info($"Light brightness set to {brightness}%");
                }
                else
                {
                    Logger.Error($"Failed to set light brightness: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting light brightness: {ex.Message}");
            }
        }

        public void SetLightSpeed(int speed)
        {
            if (!isControllerConnected || controllerService == null)
            {
                Logger.Warn("Cannot set light speed: controller not connected");
                return;
            }

            try
            {
                speed = Math.Max(0, Math.Min(100, speed));
                ParseHexColor(lightColor, out byte r, out byte g, out byte b);

                RgbMode rgbMode = (RgbMode)lightMode;
                var result = controllerService.SetStickLightMode(rgbMode, r, g, b, lightBrightness / 100f, speed / 100f);
                if (result.Success)
                {
                    lightSpeed = speed;
                    Logger.Info($"Light speed set to {speed}%");
                }
                else
                {
                    Logger.Error($"Failed to set light speed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting light speed: {ex.Message}");
            }
        }

        public void SetPerformanceMode(int mode)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set performance mode: WMI service not available");
                return;
            }

            try
            {
                TdpMode tdpMode = (TdpMode)mode;
                var result = wmiService.SetSmartFanMode(tdpMode);
                if (result.Success)
                {
                    performanceMode = mode;
                    Logger.Info($"Performance mode set to {tdpMode}");
                }
                else
                {
                    Logger.Error($"Failed to set performance mode: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting performance mode: {ex.Message}");
            }
        }

        public void SetCustomTDP(int slow, int fast, int peak)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set custom TDP: WMI service not available");
                return;
            }

            try
            {
                bool modeChanged = false;

                // Set to Custom mode first
                if (performanceMode != 255)
                {
                    SetPerformanceMode(255);
                    // Sync performance mode change to widget
                    LegionPerformanceMode.SetValueSilent(255);
                    LegionPerformanceMode.SyncToRemote();
                    Logger.Info("Performance mode switched to Custom (255) and synced to widget");
                    modeChanged = true;
                }

                // Apply TDP values immediately
                ApplyTDPValues(slow, fast, peak);

                // If mode was changed, schedule a reapply after 5 seconds
                // This ensures TDP limits are properly applied after the mode switch settles
                if (modeChanged)
                {
                    ScheduleTDPReapply(slow, fast, peak);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting custom TDP: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies TDP values via WMI
        /// </summary>
        private void ApplyTDPValues(int slow, int fast, int peak)
        {
            var slowResult = wmiService.SetCPUShortTermPowerLimit(slow);
            if (slowResult.Success)
            {
                customTDPSlow = slow;
                Logger.Info($"Slow TDP (SPL) set to {slow}W");
            }
            else
            {
                Logger.Error($"Failed to set Slow TDP: {slowResult.Message}");
            }

            var fastResult = wmiService.SetCPULongTermPowerLimit(fast);
            if (fastResult.Success)
            {
                customTDPFast = fast;
                Logger.Info($"Fast TDP (SPPL) set to {fast}W");
            }
            else
            {
                Logger.Error($"Failed to set Fast TDP: {fastResult.Message}");
            }

            var peakResult = wmiService.SetCPUPeakPowerLimit(peak);
            if (peakResult.Success)
            {
                customTDPPeak = peak;
                Logger.Info($"Peak TDP (FPPT) set to {peak}W");
            }
            else
            {
                Logger.Error($"Failed to set Peak TDP: {peakResult.Message}");
            }
        }

        /// <summary>
        /// Schedules a TDP reapply after 5 seconds (used when switching to Custom mode)
        /// </summary>
        private void ScheduleTDPReapply(int slow, int fast, int peak)
        {
            // Store pending values
            pendingTdpSlow = slow;
            pendingTdpFast = fast;
            pendingTdpPeak = peak;

            // Cancel any existing timer
            if (tdpReapplyTimer != null)
            {
                tdpReapplyTimer.Stop();
                tdpReapplyTimer.Dispose();
            }

            // Create new timer for 5 seconds
            tdpReapplyTimer = new System.Timers.Timer(5000);
            tdpReapplyTimer.AutoReset = false;
            tdpReapplyTimer.Elapsed += (sender, e) =>
            {
                try
                {
                    Logger.Info($"Reapplying TDP after mode change: SPL={pendingTdpSlow}W, SPPL={pendingTdpFast}W, FPPT={pendingTdpPeak}W");
                    ApplyTDPValues(pendingTdpSlow, pendingTdpFast, pendingTdpPeak);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error reapplying TDP: {ex.Message}");
                }
                finally
                {
                    tdpReapplyTimer?.Dispose();
                    tdpReapplyTimer = null;
                }
            };
            tdpReapplyTimer.Start();
            Logger.Info("Scheduled TDP reapply in 5 seconds");
        }

        /// <summary>
        /// Apply individual Slow TDP (SPL) value
        /// </summary>
        public void ApplyCustomTDPSlow(int slow)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set custom TDP Slow: WMI service not available");
                return;
            }

            try
            {
                var result = wmiService.SetCPUShortTermPowerLimit(slow);
                if (result.Success)
                {
                    customTDPSlow = slow;
                    Logger.Info($"Slow TDP (SPL) set to {slow}W");
                }
                else
                {
                    Logger.Error($"Failed to set Slow TDP: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting Slow TDP: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply individual Fast TDP (SPPL) value
        /// </summary>
        public void ApplyCustomTDPFast(int fast)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set custom TDP Fast: WMI service not available");
                return;
            }

            try
            {
                var result = wmiService.SetCPULongTermPowerLimit(fast);
                if (result.Success)
                {
                    customTDPFast = fast;
                    Logger.Info($"Fast TDP (SPPL) set to {fast}W");
                }
                else
                {
                    Logger.Error($"Failed to set Fast TDP: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting Fast TDP: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply individual Peak TDP (FPPT) value
        /// </summary>
        public void ApplyCustomTDPPeak(int peak)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set custom TDP Peak: WMI service not available");
                return;
            }

            try
            {
                var result = wmiService.SetCPUPeakPowerLimit(peak);
                if (result.Success)
                {
                    customTDPPeak = peak;
                    Logger.Info($"Peak TDP (FPPT) set to {peak}W");
                }
                else
                {
                    Logger.Error($"Failed to set Peak TDP: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting Peak TDP: {ex.Message}");
            }
        }

        public void SetFanFullSpeed(bool enabled)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set fan full speed: WMI service not available");
                return;
            }

            try
            {
                var result = wmiService.SetFanFullSpeed(enabled);
                if (result.Success)
                {
                    fanFullSpeed = enabled;
                    Logger.Info($"Fan full speed {(enabled ? "enabled" : "disabled")}");
                }
                else
                {
                    Logger.Error($"Failed to set fan full speed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting fan full speed: {ex.Message}");
            }
        }

        public void SetGyroEnabled(bool enabled)
        {
            // WIP - Gyro control not working currently
            Logger.Warn("Gyro control is WIP - not implemented");
            gyroEnabled = enabled;
        }

        public void SetVibration(int level)
        {
            if (!isControllerConnected || controllerService == null)
            {
                Logger.Warn("Cannot set vibration: controller not connected");
                return;
            }

            try
            {
                var vibLevel = (LegionGoLibrary.ControllerVibrationLevel)level;
                var result = controllerService.SetBothControllersVibration(vibLevel);
                if (result.Success)
                {
                    vibrationLevel = level;
                    Logger.Info($"Vibration set to level {level} ({result.Message})");
                }
                else
                {
                    Logger.Error($"Failed to set vibration: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting vibration: {ex.Message}");
            }
        }

        public void SetPowerLight(bool enabled)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set power light: WMI service not available");
                return;
            }

            try
            {
                var result = wmiService.SetPowerLight(enabled);
                if (result.Success)
                {
                    powerLightEnabled = enabled;
                    Logger.Info($"Power light {(enabled ? "enabled" : "disabled")}");
                }
                else
                {
                    Logger.Error($"Failed to set power light: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting power light: {ex.Message}");
            }
        }

        public void SetChargeLimit(bool enabled)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set charge limit: WMI service not available");
                return;
            }

            try
            {
                var result = wmiService.SetBatteryChargeLimit(enabled);
                if (result.Success)
                {
                    chargeLimitEnabled = enabled;
                    Logger.Info($"Battery charge limit (80%) {(enabled ? "enabled" : "disabled")}");
                }
                else
                {
                    Logger.Error($"Failed to set charge limit: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting charge limit: {ex.Message}");
            }
        }

        public override void Update()
        {
            // Periodically check for controller connection if not connected
            if (!isControllerConnected && controllerService != null)
            {
                try
                {
                    var connectResult = controllerService.Connect();
                    if (connectResult.Success)
                    {
                        isControllerConnected = true;
                        isLegionGoDetected = true;
                        LegionGoDetected.SetDetected(true);
                        Logger.Info($"Legion Go controller reconnected: {connectResult.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Controller reconnection attempt failed: {ex.Message}");
                }
            }

            // Periodically refresh TDP values and fan speed from device (only if Legion detected)
            if (isLegionGoDetected && wmiService != null)
            {
                RefreshTDPValuesFromDevice();
                RefreshFanSpeed();
            }
        }

        /// <summary>
        /// Reads current CPU fan speed from device
        /// </summary>
        private void RefreshFanSpeed()
        {
            try
            {
                var fanResult = wmiService.GetCpuFanSpeed();
                if (fanResult.Success && fanResult.Result.HasValue)
                {
                    cpuFanSpeed = fanResult.Result.Value;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error reading fan speed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current CPU fan speed in RPM. Returns 0 if Legion Go is not detected.
        /// </summary>
        public int GetCpuFanSpeed()
        {
            return isLegionGoDetected ? cpuFanSpeed : 0;
        }

        /// <summary>
        /// Reads current TDP values from device and updates properties silently (without triggering WMI set calls)
        /// </summary>
        private void RefreshTDPValuesFromDevice()
        {
            try
            {
                var slowResult = wmiService.GetCPUShortTermPowerLimit();
                if (slowResult.Success && slowResult.Result.HasValue && slowResult.Result.Value != customTDPSlow)
                {
                    customTDPSlow = slowResult.Result.Value;
                    LegionCustomTDPSlow.SetValueSilent(customTDPSlow);
                    LegionCustomTDPSlow.SyncToRemote();
                    Logger.Debug($"Refreshed Slow TDP (SPL): {customTDPSlow}W");
                }

                var fastResult = wmiService.GetCPULongTermPowerLimit();
                if (fastResult.Success && fastResult.Result.HasValue && fastResult.Result.Value != customTDPFast)
                {
                    customTDPFast = fastResult.Result.Value;
                    LegionCustomTDPFast.SetValueSilent(customTDPFast);
                    LegionCustomTDPFast.SyncToRemote();
                    Logger.Debug($"Refreshed Fast TDP (SPPL): {customTDPFast}W");
                }

                var peakResult = wmiService.GetCPUPeakPowerLimit();
                if (peakResult.Success && peakResult.Result.HasValue && peakResult.Result.Value != customTDPPeak)
                {
                    customTDPPeak = peakResult.Result.Value;
                    LegionCustomTDPPeak.SetValueSilent(customTDPPeak);
                    LegionCustomTDPPeak.SyncToRemote();
                    Logger.Debug($"Refreshed Peak TDP (FPPT): {customTDPPeak}W");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error refreshing TDP values: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("Disposing Legion Manager...");

                try
                {
                    // Dispose TDP reapply timer
                    if (tdpReapplyTimer != null)
                    {
                        tdpReapplyTimer.Stop();
                        tdpReapplyTimer.Dispose();
                        tdpReapplyTimer = null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error disposing TDP reapply timer: {ex.Message}");
                }

                try
                {
                    controllerService?.Dispose();
                    controllerService = null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error disposing controller service: {ex.Message}");
                }

                wmiService = null;
                Logger.Info("Legion Manager disposed.");
            }

            base.Dispose(disposing);
        }
    }
}
