using LegionGo;
using LegionGoLibrary;
using NLog;
using Shared.Enums;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Performance;

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

        /// <summary>
        /// Gets the current performance mode (Quiet=1, Balanced=2, Performance=3, Custom=255).
        /// </summary>
        public int CurrentPerformanceMode => performanceMode;

        /// <summary>
        /// Gets the display name for a performance mode value.
        /// </summary>
        public static string GetPerformanceModeName(int mode)
        {
            return mode switch
            {
                1 => "Quiet Mode",
                2 => "Balanced Mode",
                3 => "Performance Mode",
                255 => "Custom",
                _ => $"Mode {mode}"
            };
        }
        private int customTDPSlow = 15;
        private int customTDPFast = 25;
        private int customTDPPeak = 35;
        private bool fanFullSpeed = false;
        private ushort[] fanCurve = new ushort[10] { 44, 48, 55, 60, 71, 79, 87, 87, 100, 100 }; // Legion Go default curve
        private bool gyroEnabled = true;
        private int vibrationLevel = 2; // Medium
        private bool powerLightEnabled = true;
        private bool chargeLimitEnabled = false;

        // Controller remapping state (cached)
        private bool nintendoLayoutEnabled = false;
        private int vibrationMode = 1; // FPS

        // Controller battery monitoring (uses controllerService, not a separate connection)
        private int leftControllerBattery = -1;
        private int rightControllerBattery = -1;
        private bool leftControllerCharging = false;
        private bool rightControllerCharging = false;

        // Fan speed (RPM)
        private int cpuFanSpeed = 0;

        // Fan curve visibility (widget tells us when to push temp/RPM updates)
        private bool fanCurveVisible = false;

        // TDP reapply timer (used when switching to Custom mode)
        private System.Timers.Timer tdpReapplyTimer;
        private int pendingTdpSlow;
        private int pendingTdpFast;
        private int pendingTdpPeak;

        // Cooldown to prevent RefreshTDPValuesFromDevice from overwriting recently-set values
        private DateTime lastTdpSetTime = DateTime.MinValue;
        private const int TDP_REFRESH_COOLDOWN_MS = 3000; // 3 seconds cooldown after setting TDP

        // Rate-limiting for fan speed refresh (WMI calls are CPU-expensive)
        private DateTime lastFanSpeedRefresh = DateTime.MinValue;
        private const int FAN_SPEED_REFRESH_INTERVAL_MS = 2000; // Refresh fan speed every 2 seconds

        // Startup grace period to ignore LegionCustomTDP slider sync during widget startup
        // The main TDP slider calls SetCustomTDP which syncs all 3 values, so we don't want
        // the widget's stale cached values to override them
        private DateTime startupTime;
        private const int STARTUP_GRACE_PERIOD_MS = 5000; // 5 seconds after manager init

        // Performance mode debouncing to prevent queue buildup from rapid mode changes
        private System.Threading.Timer performanceModeDebounceTimer;
        private int pendingPerformanceMode = -1; // -1 means no pending mode
        private readonly object performanceModeDebounceLock = new object();
        private const int PERFORMANCE_MODE_DEBOUNCE_MS = 150;

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
        public readonly LegionFanCurveDataProperty LegionFanCurveData;
        public readonly LegionCPUCurrentTempProperty LegionCPUCurrentTemp;
        public readonly LegionCPUFanRPMProperty LegionCPUFanRPM;
        public readonly LegionFanCurveVisibleProperty LegionFanCurveVisible;
        public readonly LegionGyroEnabledProperty LegionGyroEnabled;
        public readonly LegionVibrationProperty LegionVibration;
        public readonly LegionPowerLightProperty LegionPowerLight;
        public readonly LegionChargeLimitProperty LegionChargeLimit;

        // Controller remapping properties
        public readonly LegionButtonY1Property LegionButtonY1;
        public readonly LegionButtonY2Property LegionButtonY2;
        public readonly LegionButtonY3Property LegionButtonY3;
        public readonly LegionButtonM1Property LegionButtonM1;
        public readonly LegionButtonM2Property LegionButtonM2;
        public readonly LegionButtonM3Property LegionButtonM3;
        public readonly LegionButtonDesktopProperty LegionButtonDesktop;
        public readonly LegionButtonPageProperty LegionButtonPage;
        public readonly LegionNintendoLayoutProperty LegionNintendoLayout;
        public readonly LegionVibrationModeProperty LegionVibrationMode;
        public readonly LegionControllerProfileEnabledProperty LegionControllerProfileEnabled;

        // Gyro settings properties (per-game profile)
        public readonly LegionGyroTargetProperty LegionGyroTarget;
        public readonly LegionGyroSensitivityXProperty LegionGyroSensitivityX;
        public readonly LegionGyroSensitivityYProperty LegionGyroSensitivityY;
        public readonly LegionGyroInvertXProperty LegionGyroInvertX;
        public readonly LegionGyroInvertYProperty LegionGyroInvertY;
        public readonly LegionGyroMappingTypeProperty LegionGyroMappingType;
        public readonly LegionGyroActivationModeProperty LegionGyroActivationMode;
        public readonly LegionGyroActivationButtonProperty LegionGyroActivationButton;

        // Advanced gyro properties (per-game profile)
        public readonly LegionGyroDeadzoneProperty LegionGyroDeadzone;

        // Stick deadzone properties (per-game profile)
        public readonly LegionLeftStickDeadzoneProperty LegionLeftStickDeadzone;
        public readonly LegionRightStickDeadzoneProperty LegionRightStickDeadzone;

        // Trigger travel properties (per-game profile)
        public readonly LegionLeftTriggerStartProperty LegionLeftTriggerStart;
        public readonly LegionLeftTriggerEndProperty LegionLeftTriggerEnd;
        public readonly LegionRightTriggerStartProperty LegionRightTriggerStart;
        public readonly LegionRightTriggerEndProperty LegionRightTriggerEnd;
        public readonly LegionHairTriggersProperty LegionHairTriggers;

        // Touchpad vibration property (GLOBAL setting)
        public readonly LegionTouchpadVibrationProperty LegionTouchpadVibration;

        // Joystick as mouse properties (per-game profile)
        public readonly LegionJoystickAsMouseModeProperty LegionJoystickAsMouseMode;
        public readonly LegionJoystickMouseSensProperty LegionJoystickMouseSens;

        // Gamepad button mapping (per-game profile)
        public readonly LegionGamepadMappingProperty LegionGamepadMapping;

        // Desktop controls preset (per-game profile state tracking)
        public readonly LegionDesktopControlsProperty LegionDesktopControls;

        // Controller battery properties
        public readonly ControllerBatteryLeftProperty ControllerBatteryLeft;
        public readonly ControllerBatteryRightProperty ControllerBatteryRight;
        public readonly ControllerChargingLeftProperty ControllerChargingLeft;
        public readonly ControllerChargingRightProperty ControllerChargingRight;

        // Reference to PerformanceManager for LibreHardwareMonitor sensor access
        private PerformanceManager performanceManager;

        /// <summary>
        /// Sets the PerformanceManager reference for CPU temperature sensor access.
        /// Called from Program.cs after both managers are initialized.
        /// </summary>
        public void SetPerformanceManager(PerformanceManager manager)
        {
            performanceManager = manager;
            Logger.Info($"PerformanceManager reference set, CPUTemperature sensor available: {manager?.CPUTemperature != null}");
        }

        public LegionManager(AppServiceConnection connection) : base(connection)
        {
            Logger.Info("Initializing Legion Manager...");

            // Record startup time for grace period
            startupTime = DateTime.Now;

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

            // Initialize fan curve from device only if Legion Go is detected
            if (isLegionGoDetected)
            {
                Logger.Info("Reading fan curve from device...");
                const int fanCurveTimeoutMs = 5000;
                var fanCurveTask = Task.Run(() =>
                {
                    try
                    {
                        return wmiService?.GetFanCurve();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"GetFanCurve exception: {ex.Message}");
                        return null;
                    }
                });

                if (fanCurveTask.Wait(fanCurveTimeoutMs))
                {
                    var curveResult = fanCurveTask.Result;
                    if (curveResult.HasValue && curveResult.Value.Success && curveResult.Value.FanSpeeds?.Length == 10)
                    {
                        fanCurve = curveResult.Value.FanSpeeds;
                        Logger.Info($"Fan curve loaded from device: {string.Join(",", fanCurve)}");
                    }
                    else
                    {
                        Logger.Warn($"Failed to load fan curve from device (wmiService={wmiService != null}, hasValue={curveResult.HasValue}, success={curveResult?.Success}, message={curveResult?.Message}), using defaults: {string.Join(",", fanCurve)}");
                    }
                }
                else
                {
                    Logger.Warn($"GetFanCurve timed out after {fanCurveTimeoutMs}ms, using defaults: {string.Join(",", fanCurve)}");
                }
            }
            string fanCurveString = string.Join(",", fanCurve.Select(v => (int)v));
            LegionFanCurveData = new LegionFanCurveDataProperty(fanCurveString, this);
            LegionCPUCurrentTemp = new LegionCPUCurrentTempProperty(0, this);
            LegionCPUFanRPM = new LegionCPUFanRPMProperty(0, this);
            LegionFanCurveVisible = new LegionFanCurveVisibleProperty(false, this);

            LegionGyroEnabled = new LegionGyroEnabledProperty(gyroEnabled, this);
            LegionVibration = new LegionVibrationProperty(vibrationLevel, this);
            LegionPowerLight = new LegionPowerLightProperty(powerLightEnabled, this);
            LegionChargeLimit = new LegionChargeLimitProperty(chargeLimitEnabled, this);

            // Initialize controller remapping properties (JSON ButtonMapping format)
            LegionButtonY1 = new LegionButtonY1Property("", this);
            LegionButtonY2 = new LegionButtonY2Property("", this);
            LegionButtonY3 = new LegionButtonY3Property("", this);
            LegionButtonM1 = new LegionButtonM1Property("", this);
            LegionButtonM2 = new LegionButtonM2Property("", this);
            LegionButtonM3 = new LegionButtonM3Property("", this);
            LegionButtonDesktop = new LegionButtonDesktopProperty("", this);
            LegionButtonPage = new LegionButtonPageProperty("", this);
            LegionNintendoLayout = new LegionNintendoLayoutProperty(nintendoLayoutEnabled, this);
            LegionVibrationMode = new LegionVibrationModeProperty(vibrationMode, this);
            LegionControllerProfileEnabled = new LegionControllerProfileEnabledProperty(false, this);

            // Initialize gyro settings properties
            LegionGyroTarget = new LegionGyroTargetProperty(gyroTarget, this);
            LegionGyroSensitivityX = new LegionGyroSensitivityXProperty(gyroSensitivityX, this);
            LegionGyroSensitivityY = new LegionGyroSensitivityYProperty(gyroSensitivityY, this);
            LegionGyroInvertX = new LegionGyroInvertXProperty(gyroInvertX, this);
            LegionGyroInvertY = new LegionGyroInvertYProperty(gyroInvertY, this);
            LegionGyroMappingType = new LegionGyroMappingTypeProperty(gyroMappingType, this);
            LegionGyroActivationMode = new LegionGyroActivationModeProperty(gyroActivationMode, this);
            LegionGyroActivationButton = new LegionGyroActivationButtonProperty(gyroActivationButton, this);

            // Initialize advanced gyro properties
            LegionGyroDeadzone = new LegionGyroDeadzoneProperty(gyroDeadzone, this);

            // Initialize stick deadzone properties
            LegionLeftStickDeadzone = new LegionLeftStickDeadzoneProperty(leftStickDeadzone, this);
            LegionRightStickDeadzone = new LegionRightStickDeadzoneProperty(rightStickDeadzone, this);

            // Initialize trigger travel properties
            LegionLeftTriggerStart = new LegionLeftTriggerStartProperty(0, this);
            LegionLeftTriggerEnd = new LegionLeftTriggerEndProperty(0, this);
            LegionRightTriggerStart = new LegionRightTriggerStartProperty(0, this);
            LegionRightTriggerEnd = new LegionRightTriggerEndProperty(0, this);
            LegionHairTriggers = new LegionHairTriggersProperty(false, this);

            // Initialize touchpad vibration property (GLOBAL setting)
            LegionTouchpadVibration = new LegionTouchpadVibrationProperty(touchpadVibrationLevel, this);

            // Initialize joystick as mouse properties (per-game profile)
            LegionJoystickAsMouseMode = new LegionJoystickAsMouseModeProperty(joystickAsMouseMode, this);
            LegionJoystickMouseSens = new LegionJoystickMouseSensProperty(joystickMouseSens, this);

            // Initialize gamepad button mapping property (per-game profile)
            LegionGamepadMapping = new LegionGamepadMappingProperty("", this);

            // Initialize desktop controls property (per-game profile state tracking)
            LegionDesktopControls = new LegionDesktopControlsProperty(false, this);

            // Initialize controller battery properties
            ControllerBatteryLeft = new ControllerBatteryLeftProperty(-1, this);
            ControllerBatteryRight = new ControllerBatteryRightProperty(-1, this);
            ControllerChargingLeft = new ControllerChargingLeftProperty(false, this);
            ControllerChargingRight = new ControllerChargingRightProperty(false, this);

            // NOTE: Battery monitoring is started from Program.cs AFTER widget connection
            // is established. Starting it here blocks the AppService connection.

            if (isLegionGoDetected)
            {
                // Read current performance mode and TDP values (with timeouts to prevent hangs)
                const int wmiReadTimeoutMs = 5000;

                Logger.Info("Reading current performance mode...");
                var perfModeTask = Task.Run(() =>
                {
                    try { ReadCurrentPerformanceMode(); }
                    catch (Exception ex) { Logger.Warn($"ReadCurrentPerformanceMode exception: {ex.Message}"); }
                });
                if (!perfModeTask.Wait(wmiReadTimeoutMs))
                {
                    Logger.Warn($"ReadCurrentPerformanceMode timed out after {wmiReadTimeoutMs}ms");
                }

                Logger.Info("Reading current TDP values...");
                var tdpTask = Task.Run(() =>
                {
                    try { ReadCurrentTDPValues(); }
                    catch (Exception ex) { Logger.Warn($"ReadCurrentTDPValues exception: {ex.Message}"); }
                });
                if (!tdpTask.Wait(wmiReadTimeoutMs))
                {
                    Logger.Warn($"ReadCurrentTDPValues timed out after {wmiReadTimeoutMs}ms");
                }

                // Update properties with the values read from device
                // Use silent update to avoid triggering WMI calls back
                LegionPerformanceMode.SetValueSilent(performanceMode);
                LegionCustomTDPSlow.SetValueSilent(customTDPSlow);
                LegionCustomTDPFast.SetValueSilent(customTDPFast);
                LegionCustomTDPPeak.SetValueSilent(customTDPPeak);
                LegionFanFullSpeed.SetValueSilent(fanFullSpeed);
            }

            // Enable fan curve device writes after startup grace period (only for Legion Go)
            // This prevents the widget's sync from overwriting the device's fan curve on startup
            if (isLegionGoDetected)
            {
                var fanCurveEnableTimer = new System.Timers.Timer(STARTUP_GRACE_PERIOD_MS);
                fanCurveEnableTimer.Elapsed += (s, e) =>
                {
                    fanCurveEnableTimer.Stop();
                    fanCurveEnableTimer.Dispose();
                    LegionFanCurveData?.EnableDeviceWrites();
                };
                fanCurveEnableTimer.AutoReset = false;
                fanCurveEnableTimer.Start();
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
        /// Gets the current TDP values.
        /// When in Custom mode (255), returns our cached values since WMI returns preset values.
        /// Returns (slow/SPL, fast/SPPL, peak/FPPT) or null values if not available.
        /// </summary>
        public (int? slow, int? fast, int? peak) GetCurrentTDPValues()
        {
            // In Custom mode, return our cached values since WMI GetFeatureValue
            // returns the preset TDP limits, not the custom values we've applied
            if (performanceMode == 255)
            {
                return (customTDPSlow, customTDPFast, customTDPPeak);
            }

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
            // Debounce rapid mode changes to prevent queue buildup
            lock (performanceModeDebounceLock)
            {
                pendingPerformanceMode = mode;

                // Update cached mode immediately so SetCustomTDP knows our intended mode
                // This prevents race conditions where TDP is skipped because mode hasn't changed yet
                performanceMode = mode;

                // Cancel existing timer and start a new one
                performanceModeDebounceTimer?.Dispose();
                performanceModeDebounceTimer = new System.Threading.Timer(
                    _ => ApplyPendingPerformanceMode(),
                    null,
                    PERFORMANCE_MODE_DEBOUNCE_MS,
                    System.Threading.Timeout.Infinite // Don't repeat
                );

                Logger.Debug($"SetPerformanceMode: Debouncing mode change to {mode} (will apply in {PERFORMANCE_MODE_DEBOUNCE_MS}ms if no new changes)");
            }
        }

        /// <summary>
        /// Called by the debounce timer to apply the pending performance mode.
        /// </summary>
        private void ApplyPendingPerformanceMode()
        {
            int modeToApply;
            lock (performanceModeDebounceLock)
            {
                if (pendingPerformanceMode < 0)
                {
                    return; // No pending mode
                }
                modeToApply = pendingPerformanceMode;
                pendingPerformanceMode = -1; // Clear pending
            }

            ApplyPerformanceModeInternal(modeToApply);
        }

        /// <summary>
        /// Internal method that actually applies the performance mode via WMI.
        /// Called after debouncing completes.
        /// Note: performanceMode is already set optimistically in SetPerformanceMode.
        /// We only need to revert it here if the WMI call fails.
        /// </summary>
        private void ApplyPerformanceModeInternal(int mode)
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
                    // Mode was already set optimistically, just log success
                    Logger.Info($"Performance mode set to {tdpMode}");
                }
                else
                {
                    Logger.Error($"Failed to set performance mode: {result.Message}");
                    // Note: We don't revert performanceMode here because rapid toggling
                    // means the cached value might be for a different pending change
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
                // Only apply TDP if in Custom mode (255)
                // Don't automatically switch modes - preset modes manage TDP via hardware
                // The widget should explicitly switch to Custom mode first if needed
                if (performanceMode != 255)
                {
                    Logger.Info($"Skipping SetCustomTDP({slow}, {fast}, {peak}) - not in Custom mode (current mode: {performanceMode})");
                    return;
                }

                // If there's a pending Custom mode change waiting on debounce, apply it immediately
                // This ensures the hardware is in Custom mode before we try to set TDP values
                bool flushedModeChange = false;
                lock (performanceModeDebounceLock)
                {
                    if (pendingPerformanceMode == 255)
                    {
                        Logger.Info("Flushing pending Custom mode change before applying TDP");
                        performanceModeDebounceTimer?.Dispose();
                        performanceModeDebounceTimer = null;
                        pendingPerformanceMode = -1;

                        // Apply mode change synchronously
                        TdpMode tdpMode = (TdpMode)255;
                        var modeResult = wmiService.SetSmartFanMode(tdpMode);
                        if (modeResult.Success)
                        {
                            Logger.Info($"Performance mode set to {tdpMode}");
                            flushedModeChange = true;
                        }
                        else
                        {
                            Logger.Error($"Failed to set performance mode: {modeResult.Message}");
                            return; // Don't try to set TDP if mode change failed
                        }
                    }
                }

                // If we just flushed a mode change, wait for hardware to confirm it's in Custom mode
                // The WMI call returns quickly but hardware takes ~400ms to actually switch modes
                if (flushedModeChange)
                {
                    const int maxAttempts = 10; // ~500ms max wait
                    const int delayMs = 50;
                    bool hardwareInCustomMode = false;

                    for (int i = 0; i < maxAttempts; i++)
                    {
                        var result = wmiService.GetSmartFanMode();
                        if (result.Success && result.Result == TdpMode.Custom)
                        {
                            hardwareInCustomMode = true;
                            Logger.Info($"Hardware confirmed Custom mode after {(i + 1) * delayMs}ms");
                            break;
                        }
                        System.Threading.Thread.Sleep(delayMs);
                    }

                    if (!hardwareInCustomMode)
                    {
                        Logger.Warn("Hardware did not confirm Custom mode within 500ms, applying TDP anyway and scheduling reapply");
                        // Apply TDP values now and schedule a reapply to ensure they stick
                        ApplyTDPValues(slow, fast, peak);
                        ScheduleTDPReapply(slow, fast, peak);
                        return;
                    }
                }

                // Apply TDP values
                ApplyTDPValues(slow, fast, peak);

                // Set cooldown to prevent RefreshTDPValuesFromDevice from overwriting these values
                lastTdpSetTime = DateTime.Now;

                // Sync the Legion Custom TDP sliders to match the applied values
                // This ensures the Legion tab sliders reflect what was set (especially when using main TDP slider)
                LegionCustomTDPSlow.SetValueSilent(slow);
                LegionCustomTDPSlow.SyncToRemote();
                LegionCustomTDPFast.SetValueSilent(fast);
                LegionCustomTDPFast.SyncToRemote();
                LegionCustomTDPPeak.SetValueSilent(peak);
                LegionCustomTDPPeak.SyncToRemote();
                Logger.Info($"Synced Legion Custom TDP sliders: Slow={slow}W, Fast={fast}W, Peak={peak}W");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting custom TDP: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies TDP values via WMI with rollback on partial failure.
        /// All three values are applied atomically - if any fails, previous values are restored.
        /// </summary>
        private void ApplyTDPValues(int slow, int fast, int peak)
        {
            // Store previous values for rollback on partial failure
            int previousSlow = customTDPSlow;
            int previousFast = customTDPFast;
            int previousPeak = customTDPPeak;

            // Step 1: Set Slow TDP (SPL)
            var slowResult = wmiService.SetCPUShortTermPowerLimit(slow);
            if (!slowResult.Success)
            {
                Logger.Error($"Failed to set Slow TDP: {slowResult.Message}");
                return; // Nothing to rollback yet
            }
            Logger.Info($"Slow TDP (SPL) set to {slow}W");

            // Step 2: Set Fast TDP (SPPL)
            var fastResult = wmiService.SetCPULongTermPowerLimit(fast);
            if (!fastResult.Success)
            {
                Logger.Error($"Failed to set Fast TDP: {fastResult.Message}");
                // Rollback Slow TDP
                Logger.Warn($"Rolling back Slow TDP to {previousSlow}W due to Fast TDP failure");
                wmiService.SetCPUShortTermPowerLimit(previousSlow);
                return;
            }
            Logger.Info($"Fast TDP (SPPL) set to {fast}W");

            // Step 3: Set Peak TDP (FPPT)
            var peakResult = wmiService.SetCPUPeakPowerLimit(peak);
            if (!peakResult.Success)
            {
                Logger.Error($"Failed to set Peak TDP: {peakResult.Message}");
                // Rollback both Slow and Fast TDP
                Logger.Warn($"Rolling back Slow TDP to {previousSlow}W and Fast TDP to {previousFast}W due to Peak TDP failure");
                wmiService.SetCPUShortTermPowerLimit(previousSlow);
                wmiService.SetCPULongTermPowerLimit(previousFast);
                return;
            }
            Logger.Info($"Peak TDP (FPPT) set to {peak}W");

            // All succeeded - update cached values
            customTDPSlow = slow;
            customTDPFast = fast;
            customTDPPeak = peak;
            Logger.Info($"All TDP values applied successfully: SPL={slow}W, SPPL={fast}W, FPPT={peak}W");
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
            // Skip during startup grace period to prevent widget sync from overriding main TDP slider
            if ((DateTime.Now - startupTime).TotalMilliseconds < STARTUP_GRACE_PERIOD_MS)
            {
                Logger.Info($"Skipping ApplyCustomTDPSlow({slow}W) - still in startup grace period");
                return;
            }

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
            // Skip during startup grace period to prevent widget sync from overriding main TDP slider
            if ((DateTime.Now - startupTime).TotalMilliseconds < STARTUP_GRACE_PERIOD_MS)
            {
                Logger.Info($"Skipping ApplyCustomTDPFast({fast}W) - still in startup grace period");
                return;
            }

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
            // Skip during startup grace period to prevent widget sync from overriding main TDP slider
            if ((DateTime.Now - startupTime).TotalMilliseconds < STARTUP_GRACE_PERIOD_MS)
            {
                Logger.Info($"Skipping ApplyCustomTDPPeak({peak}W) - still in startup grace period");
                return;
            }

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

        /// <summary>
        /// Sets the fan curve from a comma-separated string of 10 values.
        /// Only applies when in Custom TDP mode (255).
        /// </summary>
        public void SetFanCurveFromString(string curveData)
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot set fan curve: WMI service not available");
                return;
            }

            if (string.IsNullOrEmpty(curveData))
            {
                Logger.Warn("Empty fan curve data received");
                return;
            }

            try
            {
                var parts = curveData.Split(',');
                if (parts.Length != 10)
                {
                    Logger.Warn($"Invalid fan curve data: expected 10 values, got {parts.Length}");
                    return;
                }

                for (int i = 0; i < 10; i++)
                {
                    if (int.TryParse(parts[i].Trim(), out int value))
                    {
                        fanCurve[i] = (ushort)Math.Max(0, Math.Min(100, value));
                    }
                    else
                    {
                        Logger.Warn($"Invalid fan curve value at index {i}: {parts[i]}");
                        return;
                    }
                }

                Logger.Info($"Fan curve parsed: [{string.Join(", ", fanCurve)}]%");

                // Apply the fan curve (it's saved on the controller)
                ApplyFanCurve();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing fan curve data: {ex.Message}");
            }
        }

        private void ApplyFanCurve()
        {
            if (wmiService == null)
            {
                Logger.Warn("Cannot apply fan curve: WMI service not available");
                return;
            }

            try
            {
                var result = wmiService.SetFanCurve(fanCurve);
                if (result.Success)
                {
                    Logger.Info($"Fan curve applied: [{string.Join(", ", fanCurve)}]%");
                }
                else
                {
                    Logger.Error($"Failed to apply fan curve: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying fan curve: {ex.Message}");
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

        /// <summary>
        /// Sets the button mapping for a remappable button (legacy gamepad-only method).
        /// </summary>
        /// <param name="buttonIndex">Button index: 0=Y1, 1=Y2, 2=Y3, 3=M1, 4=M2, 5=M3</param>
        /// <param name="actionIndex">Action index matching RemapAction enum (0-28)</param>
        public void SetButtonMapping(int buttonIndex, int actionIndex)
        {
            // Forward to advanced method with gamepad type
            SetButtonMappingAdvanced(buttonIndex, 0, new int[] { actionIndex });
        }

        /// <summary>
        /// Sets the button mapping with type support (Gamepad, Keyboard, Mouse).
        /// </summary>
        /// <param name="buttonIndex">Button index: 0=Y1, 1=Y2, 2=Y3, 3=M1, 4=M2, 5=M3</param>
        /// <param name="mappingType">0=Gamepad, 1=Keyboard, 2=Mouse</param>
        /// <param name="values">Mapping values (gamepad action, keyboard keys[], or mouse button)</param>
        public void SetButtonMappingAdvanced(int buttonIndex, int mappingType, int[] values)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set button mapping: controller not connected");
                    return;
                }

                // Map button index to RemappableButton enum
                LegionGo.RemappableButton remapButton = buttonIndex switch
                {
                    0 => LegionGo.RemappableButton.Y1,
                    1 => LegionGo.RemappableButton.Y2,
                    2 => LegionGo.RemappableButton.Y3,
                    3 => LegionGo.RemappableButton.M1,
                    4 => LegionGo.RemappableButton.M2,
                    5 => LegionGo.RemappableButton.M3,
                    _ => throw new ArgumentException($"Invalid button index: {buttonIndex}")
                };

                // Log the button details for debugging
                var ctrl = LegionGo.LegionGoController.GetControllerForButton(remapButton);
                Logger.Info($"SetButtonMappingAdvanced: buttonIndex={buttonIndex}, button={remapButton}(0x{(byte)remapButton:X2}), controller={ctrl}(0x{(byte)ctrl:X2}), mappingType={mappingType}, values=[{string.Join(",", values ?? Array.Empty<int>())}]");

                bool success;
                if (mappingType == 0 && (values == null || values.Length == 0 || values[0] == 0))
                {
                    // Clear mapping (disabled)
                    success = controller.ClearButtonMapping(remapButton);
                    Logger.Info($"Button {remapButton} mapping cleared (HID: 05 00 12 0A {(byte)ctrl:X2} 01 11 01 {(byte)remapButton:X2} 01)");
                }
                else
                {
                    // Set mapping with type
                    var type = (LegionGo.MappingType)(mappingType + 1);  // 0→1, 1→2, 2→3
                    byte[] mappings;

                    if (mappingType == 0)
                    {
                        // Gamepad: use RemapAction
                        var action = LegionGo.RemapActionHelper.GetByIndex(values[0]);
                        mappings = new byte[] { (byte)action };
                    }
                    else
                    {
                        // Keyboard or Mouse: use raw values
                        mappings = values.Select(v => (byte)v).ToArray();
                    }

                    success = controller.SetButtonMappingAdvanced(remapButton, type, mappings);
                    var hidBytes = $"05 00 12 0A {(byte)ctrl:X2} 01 11 01 {(byte)remapButton:X2} {(byte)type:X2} {string.Join(" ", mappings.Select(b => b.ToString("X2")))}";
                    Logger.Info($"Button {remapButton} mapped to {type} with mappings=[{string.Join(",", mappings.Select(b => $"0x{b:X2}"))}] (HID: {hidBytes})");
                }

                if (!success)
                {
                    Logger.Error($"Failed to set button mapping for {remapButton}");
                }

                // Delay to allow controller firmware to process command before next one
                System.Threading.Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting button mapping: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets mapping for Legion-specific buttons (Desktop, Page) which use GamepadButton codes.
        /// </summary>
        /// <param name="button">The GamepadButton to map (DesktopButton=0x25, PageButton=0x26)</param>
        /// <param name="mappingType">0=Gamepad, 1=Keyboard, 2=Mouse</param>
        /// <param name="values">Mapping values (key codes, button codes, etc.)</param>
        public void SetLegionButtonMapping(LegionGo.GamepadButton button, int mappingType, int[] values)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn($"Cannot set {button} mapping: controller not connected");
                    return;
                }

                var type = (LegionGo.MappingType)(mappingType + 1); // 0->Gamepad(1), 1->Keyboard(2), 2->Mouse(3)
                byte[] mappings;

                if (mappingType == 0)
                {
                    // Gamepad: use RemapActionHelper to convert to HID button code
                    if (values.Length > 0 && values[0] == 0)
                    {
                        // Disabled - clear the mapping
                        controller.ClearGamepadButtonMapping(button);
                        Logger.Info($"{button} mapping cleared (disabled)");
                        return;
                    }
                    var action = LegionGo.RemapActionHelper.GetByIndex(values.Length > 0 ? values[0] : 0);
                    mappings = new byte[] { (byte)action };
                }
                else
                {
                    // Keyboard or Mouse: use raw values
                    mappings = values.Select(v => (byte)v).ToArray();
                }

                bool success = controller.SetGamepadButtonMappingAdvanced(button, type, mappings);
                var hidBytes = $"05 00 12 0A 03 01 11 01 {(byte)button:X2} {(byte)type:X2} {string.Join(" ", mappings.Select(b => b.ToString("X2")))}";
                Logger.Info($"{button} mapped to {type} with mappings=[{string.Join(",", mappings.Select(b => $"0x{b:X2}"))}] (HID: {hidBytes})");

                if (!success)
                {
                    Logger.Error($"Failed to set {button} mapping");
                }

                System.Threading.Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting {button} mapping: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the Nintendo layout mode (swaps A↔B and X↔Y face buttons).
        /// </summary>
        public void SetNintendoLayout(bool enabled)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set Nintendo layout: controller not connected");
                    return;
                }

                bool success = controller.SetNintendoLayout(enabled);
                if (success)
                {
                    nintendoLayoutEnabled = enabled;
                    Logger.Info($"Nintendo layout {(enabled ? "enabled" : "disabled")}");
                }
                else
                {
                    Logger.Error("Failed to set Nintendo layout");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting Nintendo layout: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the vibration mode preset (game-specific vibration patterns).
        /// </summary>
        /// <param name="mode">Vibration mode: 1=FPS, 2=Racing, 3=AVG, 4=SPG, 5=RPG</param>
        public void SetVibrationMode(int mode)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set vibration mode: controller not connected");
                    return;
                }

                LegionGo.VibrationMode vibMode = (LegionGo.VibrationMode)mode;
                bool success = controller.SetVibrationMode(vibMode);
                if (success)
                {
                    vibrationMode = mode;
                    Logger.Info($"Vibration mode set to {vibMode}");
                }
                else
                {
                    Logger.Error($"Failed to set vibration mode to {vibMode}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting vibration mode: {ex.Message}");
            }
        }

        #region Gyro Settings

        private int gyroTarget = 0;
        private int gyroSensitivityX = 50;
        private int gyroSensitivityY = 50;
        private bool gyroInvertX = false;
        private bool gyroInvertY = false;
        private int gyroMappingType = 0;
        private int gyroActivationMode = 0;
        private int gyroActivationButton = 0;

        // Advanced gyro settings
        private int gyroDeadzone = 10;         // 1-100

        /// <summary>
        /// Sets the gyro target output (0=Disabled, 1=LeftStick, 2=RightStick, 3=Mouse).
        /// </summary>
        public void SetGyroTarget(int target)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set gyro target: controller not connected");
                    return;
                }

                // Map index to GyroTarget enum (0=Disabled maps to 0x01, etc.)
                LegionGo.GyroTarget gyroTargetValue = target switch
                {
                    0 => LegionGo.GyroTarget.Disabled,
                    1 => LegionGo.GyroTarget.LeftStick,
                    2 => LegionGo.GyroTarget.RightStick,
                    3 => LegionGo.GyroTarget.Mouse,
                    _ => LegionGo.GyroTarget.Disabled
                };

                bool success = controller.SetGyroTarget(gyroTargetValue);
                if (success)
                {
                    gyroTarget = target;
                    Logger.Info($"Gyro target set to {gyroTargetValue}");
                }
                else
                {
                    Logger.Error($"Failed to set gyro target to {gyroTargetValue}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting gyro target: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the gyro X-axis sensitivity (1-100).
        /// </summary>
        public void SetGyroSensitivityX(int sensitivity)
        {
            gyroSensitivityX = sensitivity;
            ApplyGyroSettings();
        }

        /// <summary>
        /// Sets the gyro Y-axis sensitivity (1-100).
        /// </summary>
        public void SetGyroSensitivityY(int sensitivity)
        {
            gyroSensitivityY = sensitivity;
            ApplyGyroSettings();
        }

        /// <summary>
        /// Sets the gyro X-axis inversion.
        /// </summary>
        public void SetGyroInvertX(bool invert)
        {
            gyroInvertX = invert;
            ApplyGyroSettings();
        }

        /// <summary>
        /// Sets the gyro Y-axis inversion.
        /// </summary>
        public void SetGyroInvertY(bool invert)
        {
            gyroInvertY = invert;
            ApplyGyroSettings();
        }

        /// <summary>
        /// Sets the gyro mapping type (0=Instant, 1=Continuous).
        /// </summary>
        public void SetGyroMappingType(int mappingType)
        {
            gyroMappingType = mappingType;
            ApplyGyroSettings();
        }

        /// <summary>
        /// Applies all gyro settings at once.
        /// </summary>
        private void ApplyGyroSettings()
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot apply gyro settings: controller not connected");
                    return;
                }

                LegionGo.GyroMappingType mappingTypeValue = gyroMappingType switch
                {
                    0 => LegionGo.GyroMappingType.Instant,
                    1 => LegionGo.GyroMappingType.Continuous,
                    _ => LegionGo.GyroMappingType.Instant
                };

                bool success = controller.SetGyroSettings(mappingTypeValue, gyroSensitivityX, gyroSensitivityY, gyroInvertX, gyroInvertY);
                if (success)
                {
                    Logger.Info($"Gyro settings applied: MappingType={mappingTypeValue}, SensX={gyroSensitivityX}, SensY={gyroSensitivityY}, InvX={gyroInvertX}, InvY={gyroInvertY}");
                }
                else
                {
                    Logger.Error("Failed to apply gyro settings");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying gyro settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the gyro activation mode (0=Hold, 1=Toggle).
        /// </summary>
        public void SetGyroActivationMode(int mode)
        {
            gyroActivationMode = mode;
            ApplyGyroActivation();
        }

        /// <summary>
        /// Sets the gyro activation button (0-8: None, LB, LT, RB, RT, Y1, Y2, M2, M3).
        /// </summary>
        public void SetGyroActivationButton(int button)
        {
            gyroActivationButton = button;
            ApplyGyroActivation();
        }

        /// <summary>
        /// Applies gyro activation settings.
        /// </summary>
        private void ApplyGyroActivation()
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot apply gyro activation: controller not connected");
                    return;
                }

                // If button is 0 (None), reset to always-on mode
                if (gyroActivationButton == 0)
                {
                    bool resetSuccess = controller.ResetGyroActivation();
                    if (resetSuccess)
                    {
                        Logger.Info("Gyro activation reset to always-on mode");
                    }
                    else
                    {
                        Logger.Error("Failed to reset gyro activation");
                    }
                    return;
                }

                // Map index to GyroActivationMode
                LegionGo.GyroActivationMode modeValue = gyroActivationMode switch
                {
                    0 => LegionGo.GyroActivationMode.Hold,
                    1 => LegionGo.GyroActivationMode.Toggle,
                    _ => LegionGo.GyroActivationMode.Hold
                };

                // Map index to GyroActivationButton (0=None is handled above)
                // 1=LB, 2=LT, 3=RB, 4=RT, 5=Y1, 6=Y2, 7=M2, 8=M3
                LegionGo.GyroActivationButton buttonValue = gyroActivationButton switch
                {
                    1 => LegionGo.GyroActivationButton.LB,
                    2 => LegionGo.GyroActivationButton.LT,
                    3 => LegionGo.GyroActivationButton.RB,
                    4 => LegionGo.GyroActivationButton.RT,
                    5 => LegionGo.GyroActivationButton.Y1,
                    6 => LegionGo.GyroActivationButton.Y2,
                    7 => LegionGo.GyroActivationButton.M2,
                    8 => LegionGo.GyroActivationButton.M3,
                    _ => LegionGo.GyroActivationButton.None
                };

                bool success = controller.SetGyroActivationButtons(modeValue, buttonValue);
                if (success)
                {
                    Logger.Info($"Gyro activation set: Mode={modeValue}, Button={buttonValue}");
                }
                else
                {
                    Logger.Error($"Failed to set gyro activation");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying gyro activation: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the gyro deadzone (1-100, suppresses small motions near center).
        /// </summary>
        public void SetGyroDeadzone(int deadzone)
        {
            gyroDeadzone = deadzone;
            ApplyAdvancedGyroSetting("Deadzone", deadzone, controller => controller.SetGyroDeadzone(deadzone));
        }

        /// <summary>
        /// Helper to apply advanced gyro settings with common logging/error handling.
        /// </summary>
        private void ApplyAdvancedGyroSetting(string settingName, int value, Func<LegionGo.LegionGoController, bool> applyAction)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn($"Cannot apply gyro {settingName}: controller not connected");
                    return;
                }

                bool success = applyAction(controller);
                if (success)
                {
                    Logger.Info($"Gyro {settingName} set to {value}");
                }
                else
                {
                    Logger.Error($"Failed to set gyro {settingName} to {value}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying gyro {settingName}: {ex.Message}");
            }
        }

        #endregion

        #region Stick Deadzone

        private int leftStickDeadzone = 4;
        private int rightStickDeadzone = 4;

        /// <summary>
        /// Sets the left stick deadzone (0-50%).
        /// </summary>
        public void SetLeftStickDeadzone(int percent)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set left stick deadzone: controller not connected");
                    return;
                }

                bool success = controller.SetStickDeadzone(LegionGo.Controller.Left, percent);
                if (success)
                {
                    leftStickDeadzone = percent;
                    Logger.Info($"Left stick deadzone set to {percent}%");
                }
                else
                {
                    Logger.Error($"Failed to set left stick deadzone to {percent}%");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting left stick deadzone: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the right stick deadzone (0-50%).
        /// </summary>
        public void SetRightStickDeadzone(int percent)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set right stick deadzone: controller not connected");
                    return;
                }

                bool success = controller.SetStickDeadzone(LegionGo.Controller.Right, percent);
                if (success)
                {
                    rightStickDeadzone = percent;
                    Logger.Info($"Right stick deadzone set to {percent}%");
                }
                else
                {
                    Logger.Error($"Failed to set right stick deadzone to {percent}%");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting right stick deadzone: {ex.Message}");
            }
        }

        #endregion

        #region Trigger Travel

        private int leftTriggerStart = 0;
        private int leftTriggerEnd = 0;
        private int rightTriggerStart = 0;
        private int rightTriggerEnd = 0;

        /// <summary>
        /// Sets the left trigger travel range.
        /// </summary>
        /// <param name="start">Percentage where trigger starts registering (0-100).</param>
        /// <param name="end">Percentage from end where trigger reports full (0-100).</param>
        public void SetLeftTriggerTravel(int start, int end)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set left trigger travel: controller not connected");
                    return;
                }

                bool success = controller.SetTriggerTravel(LegionGo.Controller.Left, start, end);
                if (success)
                {
                    leftTriggerStart = start;
                    leftTriggerEnd = end;
                    Logger.Info($"Left trigger travel set to start={start}%, end={end}%");
                }
                else
                {
                    Logger.Error($"Failed to set left trigger travel");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting left trigger travel: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the right trigger travel range.
        /// </summary>
        /// <param name="start">Percentage where trigger starts registering (0-100).</param>
        /// <param name="end">Percentage from end where trigger reports full (0-100).</param>
        public void SetRightTriggerTravel(int start, int end)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set right trigger travel: controller not connected");
                    return;
                }

                bool success = controller.SetTriggerTravel(LegionGo.Controller.Right, start, end);
                if (success)
                {
                    rightTriggerStart = start;
                    rightTriggerEnd = end;
                    Logger.Info($"Right trigger travel set to start={start}%, end={end}%");
                }
                else
                {
                    Logger.Error($"Failed to set right trigger travel");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting right trigger travel: {ex.Message}");
            }
        }

        #endregion

        #region Touchpad Vibration

        private int touchpadVibrationLevel = 3;  // 1=Off, 2=Low, 3=Medium, 4=High

        /// <summary>
        /// Sets the touchpad vibration (haptic feedback) level.
        /// This is a GLOBAL setting, not per-game.
        /// </summary>
        /// <param name="level">1=Off, 2=Low, 3=Medium, 4=High</param>
        public void SetTouchpadVibration(int level)
        {
            try
            {
                if (level < 1 || level > 4)
                {
                    Logger.Warn($"Invalid touchpad vibration level: {level}");
                    return;
                }

                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set touchpad vibration: controller not connected");
                    return;
                }

                var vibLevel = (LegionGo.TouchpadVibrationLevel)level;
                bool success = controller.SetTouchpadVibration(vibLevel);
                if (success)
                {
                    touchpadVibrationLevel = level;
                    string levelName = level switch
                    {
                        1 => "Off",
                        2 => "Low",
                        3 => "Medium",
                        4 => "High",
                        _ => "Unknown"
                    };
                    Logger.Info($"Touchpad vibration set to {levelName}");
                }
                else
                {
                    Logger.Error($"Failed to set touchpad vibration to level {level}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting touchpad vibration: {ex.Message}");
            }
        }

        #endregion

        #region Joystick as Mouse

        private int joystickAsMouseMode = 0;  // 0=Disabled, 1=Left Stick, 2=Right Stick
        private int joystickMouseSens = 50;   // 10-100

        /// <summary>
        /// Sets joystick as mouse mode (0=Disabled, 1=Left Stick, 2=Right Stick).
        /// </summary>
        public void SetJoystickAsMouseMode(int mode)
        {
            int previousMode = joystickAsMouseMode;
            joystickAsMouseMode = mode;

            // Disable previous stick if it was enabled
            if (previousMode == 1)
            {
                ApplyJoystickAsMouse(true, false, joystickMouseSens);
            }
            else if (previousMode == 2)
            {
                ApplyJoystickAsMouse(false, false, joystickMouseSens);
            }

            // Enable new stick if not disabled
            if (mode == 1)
            {
                ApplyJoystickAsMouse(true, true, joystickMouseSens);
            }
            else if (mode == 2)
            {
                ApplyJoystickAsMouse(false, true, joystickMouseSens);
            }
        }

        /// <summary>
        /// Sets joystick mouse sensitivity (10-100).
        /// </summary>
        public void SetJoystickMouseSens(int sensitivity)
        {
            joystickMouseSens = sensitivity;
            // Only apply if a joystick is active
            if (joystickAsMouseMode == 1)
            {
                ApplyJoystickAsMouse(true, true, sensitivity);
            }
            else if (joystickAsMouseMode == 2)
            {
                ApplyJoystickAsMouse(false, true, sensitivity);
            }
        }

        /// <summary>
        /// Applies joystick as mouse setting via HID command.
        /// </summary>
        private void ApplyJoystickAsMouse(bool isLeft, bool enabled, int sensitivity)
        {
            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set joystick as mouse: controller not connected");
                    return;
                }

                var ctrl = isLeft ? LegionGo.Controller.Left : LegionGo.Controller.Right;
                bool success = controller.SetJoystickAsMouse(ctrl, enabled, sensitivity);
                if (success)
                {
                    Logger.Info($"{(isLeft ? "Left" : "Right")} joystick as mouse: enabled={enabled}, sensitivity={sensitivity}");
                }
                else
                {
                    Logger.Error($"Failed to set {(isLeft ? "left" : "right")} joystick as mouse");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting joystick as mouse: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies gamepad button mappings from JSON.
        /// JSON format: {"LSClick":{"Type":1,"GamepadAction":3,"KeyboardKeys":[],"MouseButton":0},...}
        /// </summary>
        public void ApplyGamepadButtonMappings(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Logger.Debug("No gamepad button mappings to apply");
                return;
            }

            try
            {
                using var controller = new LegionGo.LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot apply gamepad button mappings: controller not connected");
                    return;
                }

                // Parse the outer JSON to get button->mapping entries
                // Format: {"LSClick":{...},"A":{...},...}
                var buttonMatches = System.Text.RegularExpressions.Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*(\\{[^}]+\\})");

                foreach (System.Text.RegularExpressions.Match match in buttonMatches)
                {
                    string buttonName = match.Groups[1].Value;
                    string mappingJson = match.Groups[2].Value;

                    // Try to parse the button name to GamepadButton enum
                    if (!Enum.TryParse<LegionGo.GamepadButton>(buttonName, out var button))
                    {
                        Logger.Warn($"Unknown gamepad button: {buttonName}");
                        continue;
                    }

                    // Parse the mapping using existing ButtonMappingParser
                    var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(mappingJson);

                    // Apply the mapping
                    if (type == 0 && gamepadAction == 0)
                    {
                        // Reset button to default: first clear, then remap to itself
                        // Step 1: Clear the existing mapping
                        controller.ClearGamepadButtonMapping(button);
                        System.Threading.Thread.Sleep(50); // Delay between clear and remap

                        // Step 2: Map button to itself (default behavior)
                        // This is needed for all buttons including sticks to restore axis properly
                        controller.SetGamepadButtonMappingAdvanced(button, LegionGo.MappingType.Gamepad, new byte[] { (byte)button });
                        System.Threading.Thread.Sleep(50); // Delay after remap before next button (fixes stick range issues)
                        Logger.Info($"Reset gamepad button {buttonName} to default (cleared then mapped to self: 0x{(byte)button:X2})");
                    }
                    else
                    {
                        var mappingType = (LegionGo.MappingType)(type + 1); // 0->Gamepad(1), 1->Keyboard(2), 2->Mouse(3)
                        byte[] mappings;

                        if (type == 0)
                        {
                            // Gamepad: use RemapActionHelper to convert dropdown index to HID button code
                            var action = LegionGo.RemapActionHelper.GetByIndex(gamepadAction);
                            mappings = new byte[] { (byte)action };
                        }
                        else if (type == 1)
                        {
                            // Keyboard: use raw key codes
                            mappings = keyboardKeys?.Select(k => (byte)k).ToArray() ?? Array.Empty<byte>();
                        }
                        else
                        {
                            // Mouse: convert dropdown index (0=Left, 1=Right, 2=Middle) to HID code (0x01, 0x02, 0x03)
                            mappings = new byte[] { (byte)(mouseButton + 1) };
                        }

                        if (mappings.Length > 0)
                        {
                            controller.SetGamepadButtonMappingAdvanced(button, mappingType, mappings);
                            System.Threading.Thread.Sleep(50); // Delay after each mapping for firmware to process
                            Logger.Info($"Applied gamepad button {buttonName} mapping: type={mappingType}, values=[{string.Join(",", mappings.Select(b => $"0x{b:X2}"))}]");
                        }
                    }
                }

                Logger.Info($"Applied gamepad button mappings successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying gamepad button mappings: {ex.Message}");
            }
        }

        #endregion

        #region Controller Battery Monitoring

        /// <summary>
        /// Starts battery monitoring for the controllers using the existing controllerService connection.
        /// </summary>
        private void StartBatteryMonitoring()
        {
            try
            {
                if (controllerService != null && isControllerConnected)
                {
                    controllerService.BatteryUpdated += OnControllerBatteryUpdated;
                    controllerService.StartBatteryMonitoring();
                    Logger.Info("Controller battery monitoring started");
                }
                else
                {
                    Logger.Info("Controller not connected, battery monitoring not started");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error starting battery monitoring: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops battery monitoring.
        /// </summary>
        private void StopBatteryMonitoring()
        {
            if (controllerService != null)
            {
                try
                {
                    controllerService.BatteryUpdated -= OnControllerBatteryUpdated;
                    controllerService.StopBatteryMonitoring();
                }
                catch { }
                Logger.Info("Controller battery monitoring stopped");
            }
        }

        /// <summary>
        /// Public method to start battery monitoring if controller is connected.
        /// Called from Program.cs after widget connection is established.
        /// </summary>
        public void StartBatteryMonitoringIfConnected()
        {
            if (isControllerConnected)
            {
                StartBatteryMonitoring();
            }
        }

        /// <summary>
        /// Handler for battery updates from the controller.
        /// </summary>
        private void OnControllerBatteryUpdated(object sender, LegionGoLibrary.ControllerServiceBatteryEventArgs e)
        {
            Logger.Debug($"Battery update received: L={e.LeftBattery}% ({(e.LeftCharging ? "charging" : "discharging")}), R={e.RightBattery}% ({(e.RightCharging ? "charging" : "discharging")})");

            // Update cached values
            leftControllerBattery = e.LeftBattery;
            rightControllerBattery = e.RightBattery;
            leftControllerCharging = e.LeftCharging;
            rightControllerCharging = e.RightCharging;

            // Update properties and sync to widget
            // Wrap in try/catch because SyncToRemote is async void and can crash the process
            // if the AppService connection is broken (e.g., widget closed or stale connection)
            try
            {
                ControllerBatteryLeft.SetValueAndSync(e.LeftBattery);
                ControllerBatteryRight.SetValueAndSync(e.RightBattery);
                ControllerChargingLeft.SetValueAndSync(e.LeftCharging);
                ControllerChargingRight.SetValueAndSync(e.RightCharging);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to sync battery status to widget: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the left controller battery percentage (1-100), or -1 if unavailable.
        /// </summary>
        public int GetLeftControllerBattery() => leftControllerBattery;

        /// <summary>
        /// Gets the right controller battery percentage (1-100), or -1 if unavailable.
        /// </summary>
        public int GetRightControllerBattery() => rightControllerBattery;

        /// <summary>
        /// Gets whether the left controller is charging.
        /// </summary>
        public bool IsLeftControllerCharging() => leftControllerCharging;

        /// <summary>
        /// Gets whether the right controller is charging.
        /// </summary>
        public bool IsRightControllerCharging() => rightControllerCharging;

        #endregion

        public override void Update()
        {
            // Detect if controller service has disconnected (e.g., controller removed)
            if (isControllerConnected && controllerService != null && !controllerService.IsConnected)
            {
                isControllerConnected = false;
                Logger.Info("Controller disconnected - will attempt reconnection");
                // Battery monitoring will stop automatically when connection is lost
            }

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

                        // Restart battery monitoring on reconnection
                        StartBatteryMonitoring();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Controller reconnection attempt failed: {ex.Message}");
                }
            }

            // Rate-limit fan speed refresh to reduce WMI call overhead (every 2 seconds instead of every update)
            // Note: TDP refresh is disabled to prevent conflicts with user-set values
            if (isLegionGoDetected && wmiService != null)
            {
                var now = DateTime.Now;
                if ((now - lastFanSpeedRefresh).TotalMilliseconds >= FAN_SPEED_REFRESH_INTERVAL_MS)
                {
                    RefreshFanSpeed();
                    lastFanSpeedRefresh = now;
                }
            }
        }

        /// <summary>
        /// Reads current CPU fan speed from device for internal use (OSD display).
        /// Also pushes temp/RPM updates to widget when fan curve is visible.
        /// </summary>
        private void RefreshFanSpeed()
        {
            try
            {
                // Read fan RPM for internal use (OSD)
                var fanResult = wmiService.GetCpuFanSpeed();
                if (fanResult.Success && fanResult.Result.HasValue)
                {
                    cpuFanSpeed = fanResult.Result.Value;
                }

                // Only push temp/RPM to widget when fan curve is visible
                if (fanCurveVisible)
                {
                    LegionCPUFanRPM.UpdateRPM(cpuFanSpeed);

                    // Get CPU temperature from LibreHardwareMonitor
                    if (performanceManager?.CPUTemperature != null)
                    {
                        int cpuTemp = (int)performanceManager.CPUTemperature.Value;
                        LegionCPUCurrentTemp.UpdateTemp(cpuTemp);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error reading fan speed: {ex.Message}");
            }
        }

        /// <summary>
        /// Called by LegionFanCurveVisibleProperty when widget sets visibility.
        /// </summary>
        public void SetFanCurveVisible(bool visible)
        {
            fanCurveVisible = visible;
            Logger.Debug($"Fan curve visibility set to: {visible}");

            // Immediately push current values when fan curve becomes visible
            if (visible)
            {
                LegionCPUFanRPM.UpdateRPM(cpuFanSpeed);
                if (performanceManager?.CPUTemperature != null)
                {
                    int cpuTemp = (int)performanceManager.CPUTemperature.Value;
                    LegionCPUCurrentTemp.UpdateTemp(cpuTemp);
                }
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
            // Skip refresh during cooldown period after TDP was set
            // This prevents stale WMI reads from overwriting recently-set values
            if ((DateTime.Now - lastTdpSetTime).TotalMilliseconds < TDP_REFRESH_COOLDOWN_MS)
            {
                return;
            }

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
                    // Stop battery monitoring
                    StopBatteryMonitoring();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error stopping battery monitoring: {ex.Message}");
                }

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
