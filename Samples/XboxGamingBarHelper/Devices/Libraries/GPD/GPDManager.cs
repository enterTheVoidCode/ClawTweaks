using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.Devices.Libraries.GPD
{
    /// <summary>
    /// Manager for GPD device-specific features (Win Mini, Win 4, Win 5, etc.).
    /// Handles device detection, HID controller communication, and GPD-specific functionality.
    /// </summary>
    internal class GPDManager : Manager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Device detection
        private DeviceInfo deviceInfo;
        private bool isGPDDetected = false;
        private bool isWin5Detected = false;

        // Win 5 HID controller
        private GPDWin5Controller _win5Controller;
        private bool _win5ControllerConnected = false;
        private Thread _connectionMonitorThread;
        private volatile bool _monitoring = false;

        // Fan controller
        private GPDFanController _fanController;
        private DateTime _lastFanRPMUpdate = DateTime.MinValue;
        private const int FAN_RPM_UPDATE_INTERVAL_MS = 2000; // Update RPM every 2 seconds

        // Software fan curve
        private PerformanceManager performanceManager;
        private bool fanCurveEnabled = false;
        private int[] fanCurveValues = { 0, 30, 35, 45, 55, 65, 75, 85, 95, 100 };
        private bool fanCurveVisible = false;
        private int lastAppliedFanSpeed = -1;
        private static readonly int[] FanCurveTemps = { 30, 38, 46, 54, 62, 70, 78, 86, 94, 100 };
        private readonly Dictionary<int, ushort> _win5Mappings = new Dictionary<int, ushort>();
        private ushort _win5R4Keycode = 0x002B;

        /// <summary>
        /// Property indicating if a GPD device is detected. Synced to widget.
        /// </summary>
        public readonly GPDDetectedProperty GPDDetected;

        /// <summary>
        /// Property indicating if Win 5 HID controller is connected.
        /// </summary>
        public readonly GPDWin5ConnectedProperty Win5Connected;

        /// <summary>
        /// Device display name (e.g., "GPD Win 5") based on SMBIOS detection.
        /// </summary>
        public readonly GPDDeviceNameProperty DeviceName;

        /// <summary>
        /// Property synced to widget indicating if device supports fan control.
        /// </summary>
        public readonly GPDSupportsFanControlProperty SupportsFanControlProp;

        /// <summary>
        /// Trigger property to restore default button mappings on Win 5.
        /// </summary>
        public readonly GPDRestoreDefaultsProperty RestoreDefaults;

        // GPD Win 5 Button Remapping Properties
        public readonly GPDButtonAProperty ButtonA;
        public readonly GPDButtonBProperty ButtonB;
        public readonly GPDButtonXProperty ButtonX;
        public readonly GPDButtonYProperty ButtonY;
        public readonly GPDButtonDPadUpProperty ButtonDPadUp;
        public readonly GPDButtonDPadDownProperty ButtonDPadDown;
        public readonly GPDButtonDPadLeftProperty ButtonDPadLeft;
        public readonly GPDButtonDPadRightProperty ButtonDPadRight;
        public readonly GPDButtonL3Property ButtonL3;
        public readonly GPDButtonR3Property ButtonR3;
        public readonly GPDButtonL4Property ButtonL4;
        public readonly GPDButtonR4Property ButtonR4;
        public readonly GPDButtonLSUpProperty ButtonLSUp;
        public readonly GPDButtonLSDownProperty ButtonLSDown;
        public readonly GPDButtonLSLeftProperty ButtonLSLeft;
        public readonly GPDButtonLSRightProperty ButtonLSRight;

        // GPD Fan Control Properties
        public readonly GPDFanSpeedProperty FanSpeed;
        public readonly GPDFanRPMProperty FanRPM;
        public readonly GPDFanModeProperty FanMode;

        // GPD Software Fan Curve Properties
        public readonly GPDFanCurveEnabledProperty FanCurveEnabled;
        public readonly GPDFanCurveDataProperty FanCurveData;
        public readonly GPDFanCurveVisibleProperty FanCurveVisibleProp;
        public readonly GPDCPUTempProperty CPUTemp;

        /// <summary>
        /// Gets all properties managed by this manager for registration with the sync system.
        /// </summary>
        public IEnumerable<IProperty> Properties
        {
            get
            {
                yield return GPDDetected;
                yield return Win5Connected;
                yield return DeviceName;
                yield return SupportsFanControlProp;
                yield return RestoreDefaults;
                // Button remapping properties
                yield return ButtonA;
                yield return ButtonB;
                yield return ButtonX;
                yield return ButtonY;
                yield return ButtonDPadUp;
                yield return ButtonDPadDown;
                yield return ButtonDPadLeft;
                yield return ButtonDPadRight;
                yield return ButtonL3;
                yield return ButtonR3;
                yield return ButtonL4;
                yield return ButtonR4;
                yield return ButtonLSUp;
                yield return ButtonLSDown;
                yield return ButtonLSLeft;
                yield return ButtonLSRight;
                // Fan control properties
                yield return FanSpeed;
                yield return FanRPM;
                yield return FanMode;
                // Software fan curve properties
                yield return FanCurveEnabled;
                yield return FanCurveData;
                yield return FanCurveVisibleProp;
                yield return CPUTemp;
            }
        }

        /// <summary>
        /// Gets whether Win 5 HID features are available.
        /// </summary>
        public bool IsWin5 => isWin5Detected;

        /// <summary>
        /// Gets the Win 5 controller instance (may be null if not Win 5 device).
        /// </summary>
        public GPDWin5Controller Win5Controller => _win5Controller;

        public GPDManager()
        {
            Logger.Info("=== GPDManager Initialization Started ===");

            // Detect device using the device detection system
            deviceInfo = DeviceDetector.DetectDevice();

            // Check if this is a GPD device
            isGPDDetected = IsGPDDevice(deviceInfo.DeviceType);
            isWin5Detected = deviceInfo.DeviceType == DeviceType.GPDWin5;

            Logger.Info($"[GPD] Device detection results:");
            Logger.Info($"[GPD]   DeviceType: {deviceInfo.DeviceType}");
            Logger.Info($"[GPD]   Manufacturer: {deviceInfo.Manufacturer}");
            Logger.Info($"[GPD]   Model: {deviceInfo.Model}");
            Logger.Info($"[GPD]   Version: {deviceInfo.Version}");
            Logger.Info($"[GPD]   IsGPDDevice: {isGPDDetected}");
            Logger.Info($"[GPD]   IsWin5: {isWin5Detected}");

            // Create the detection properties
            GPDDetected = new GPDDetectedProperty(isGPDDetected, this);
            Win5Connected = new GPDWin5ConnectedProperty(false, this);
            DeviceName = new GPDDeviceNameProperty(GetDeviceModelName(), this);
            SupportsFanControlProp = new GPDSupportsFanControlProperty(deviceInfo?.SupportsFanControl ?? false, this);
            RestoreDefaults = new GPDRestoreDefaultsProperty(this);

            Logger.Info($"[GPD] Device name: {GetDeviceModelName()}, SupportsFanControl: {deviceInfo?.SupportsFanControl ?? false}");

            // Create button remapping properties (only used for Win 5)
            ButtonA = new GPDButtonAProperty(this);
            ButtonB = new GPDButtonBProperty(this);
            ButtonX = new GPDButtonXProperty(this);
            ButtonY = new GPDButtonYProperty(this);
            ButtonDPadUp = new GPDButtonDPadUpProperty(this);
            ButtonDPadDown = new GPDButtonDPadDownProperty(this);
            ButtonDPadLeft = new GPDButtonDPadLeftProperty(this);
            ButtonDPadRight = new GPDButtonDPadRightProperty(this);
            ButtonL3 = new GPDButtonL3Property(this);
            ButtonR3 = new GPDButtonR3Property(this);
            ButtonL4 = new GPDButtonL4Property(this);
            ButtonR4 = new GPDButtonR4Property(this);
            ButtonLSUp = new GPDButtonLSUpProperty(this);
            ButtonLSDown = new GPDButtonLSDownProperty(this);
            ButtonLSLeft = new GPDButtonLSLeftProperty(this);
            ButtonLSRight = new GPDButtonLSRightProperty(this);

            // Create fan control properties
            FanSpeed = new GPDFanSpeedProperty(this);
            FanRPM = new GPDFanRPMProperty(this);
            FanMode = new GPDFanModeProperty(this);

            // Create software fan curve properties
            FanCurveEnabled = new GPDFanCurveEnabledProperty(this);
            FanCurveData = new GPDFanCurveDataProperty(this);
            FanCurveVisibleProp = new GPDFanCurveVisibleProperty(this);
            CPUTemp = new GPDCPUTempProperty(this);

            var defaults = GPDWin5Controller.GetDefaultButtonMap();
            for (int i = 0; i < defaults.Length; i++)
            {
                _win5Mappings[i] = defaults[i];
            }

            // Load saved fan curve data from LocalSettings
            LoadFanCurveSettings();

            if (isGPDDetected)
            {
                Logger.Info($"[GPD] GPD device detected: {GetDeviceModelName()}");
                Logger.Info($"[GPD]   Supports Fan Control: {deviceInfo.SupportsFanControl}");
                Logger.Info($"[GPD]   Supports Controller Remap: {deviceInfo.SupportsControllerRemap}");

                if (isWin5Detected)
                {
                    Logger.Info("[GPD] Win 5 detected - initializing HID controller...");
                    InitializeWin5Controller();

                    // Initialize fan controller if device supports it
                    if (deviceInfo.SupportsFanControl)
                    {
                        Logger.Info("[GPD] Win 5 supports fan control - initializing fan controller...");
                        InitializeFanController();
                    }
                }
                else
                {
                    Logger.Info($"[GPD] Non-Win5 GPD device - HID controller features not available");
                }
            }
            else
            {
                Logger.Info("[GPD] No GPD device detected");
            }

            Logger.Info("=== GPDManager Initialization Complete ===");
        }

        /// <summary>
        /// Initializes the Win 5 HID controller.
        /// </summary>
        private void InitializeWin5Controller()
        {
            Logger.Info("[GPDWin5] Initializing Win 5 HID controller...");

            try
            {
                // Check if device is available before attempting connection
                Logger.Debug("[GPDWin5] Checking if Win 5 HID device is available...");
                bool deviceAvailable = GPDWin5Controller.IsDeviceAvailable();
                Logger.Info($"[GPDWin5] Win 5 HID device available: {deviceAvailable}");

                if (!deviceAvailable)
                {
                    Logger.Warn("[GPDWin5] Win 5 HID device not found - may be in different mode or driver issue");
                    Logger.Info($"[GPDWin5] Expected: VID=0x{GPDWin5Controller.VendorId:X4}, PID=0x{GPDWin5Controller.ProductId:X4}");

                    // In debug mode, show Win 5 UI even without actual hardware
                    if (DeviceDetector.IsDebugModeActive)
                    {
                        Logger.Info("[GPDWin5] Debug mode active - enabling Win 5 UI without hardware");
                        Win5Connected.SetConnected(true);
                        return;
                    }

                    // Notify widget this is a Win 5 device (even though HID not available)
                    Win5Connected.SetConnected(false);
                    return;
                }

                _win5Controller = new GPDWin5Controller();
                _win5Controller.ConnectionChanged += OnWin5ConnectionChanged;
                _win5Controller.CommandExecuted += OnWin5CommandExecuted;

                // Attempt initial connection
                Logger.Info("[GPDWin5] Attempting to connect to Win 5 controller...");
                bool connected = _win5Controller.Connect();

                if (connected)
                {
                    Logger.Info("[GPDWin5] Successfully connected to Win 5 controller!");
                    _win5ControllerConnected = true;
                    Win5Connected.SetConnected(true);

                    // Read and log current configuration
                    Logger.Info("[GPDWin5] Reading current button configuration...");
                    ReadAndLogConfiguration();
                }
                else
                {
                    Logger.Warn("[GPDWin5] Failed to connect to Win 5 controller");
                    Logger.Info("[GPDWin5] This may be due to:");
                    Logger.Info("[GPDWin5]   - Device is in a different mode (gamepad vs mouse mode)");
                    Logger.Info("[GPDWin5]   - Another application has exclusive access");
                    Logger.Info("[GPDWin5]   - HID driver issue");
                    // Notify widget this is a Win 5 device (even though HID connection failed)
                    Win5Connected.SetConnected(false);
                }

                // Start connection monitor thread
                StartConnectionMonitor();
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] Error initializing Win 5 controller: {ex.Message}");
                Logger.Error($"[GPDWin5] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Reads and logs the current button configuration.
        /// </summary>
        private void ReadAndLogConfiguration()
        {
            if (_win5Controller == null || !_win5Controller.IsConnected)
            {
                Logger.Warn("[GPDWin5] Cannot read configuration - controller not connected");
                return;
            }

            try
            {
                Logger.Info("[GPDWin5] === Reading Full Configuration ===");

                // Read main button config
                Logger.Info("[GPDWin5] Reading main button configuration...");
                var mainConfig = _win5Controller.ReadConfiguration();
                if (mainConfig != null)
                {
                    Logger.Info($"[GPDWin5] Main config read successfully ({mainConfig.Length} bytes)");
                }
                else
                {
                    Logger.Warn("[GPDWin5] Failed to read main configuration");
                }

                // Read L4 paddle config
                Logger.Info("[GPDWin5] Reading L4 paddle configuration...");
                var l4Config = _win5Controller.ReadL4PaddleConfig();
                if (l4Config != null)
                {
                    Logger.Info($"[GPDWin5] L4 paddle config read successfully ({l4Config.Length} bytes)");
                }
                else
                {
                    Logger.Warn("[GPDWin5] Failed to read L4 paddle configuration");
                }

                // Read R4 paddle config
                Logger.Info("[GPDWin5] Reading R4 paddle configuration...");
                var r4Config = _win5Controller.ReadR4PaddleConfig();
                if (r4Config != null)
                {
                    Logger.Info($"[GPDWin5] R4 paddle config read successfully ({r4Config.Length} bytes)");
                }
                else
                {
                    Logger.Warn("[GPDWin5] Failed to read R4 paddle configuration");
                }

                Logger.Info("[GPDWin5] === Configuration Read Complete ===");
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] Error reading configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts the connection monitor thread that attempts to reconnect if disconnected.
        /// </summary>
        private void StartConnectionMonitor()
        {
            if (_monitoring)
                return;

            Logger.Debug("[GPDWin5] Starting connection monitor thread...");
            _monitoring = true;
            _connectionMonitorThread = new Thread(ConnectionMonitorLoop)
            {
                IsBackground = true,
                Name = "GPDWin5-ConnectionMonitor"
            };
            _connectionMonitorThread.Start();
        }

        /// <summary>
        /// Connection monitor loop that attempts reconnection when disconnected.
        /// </summary>
        private void ConnectionMonitorLoop()
        {
            Logger.Debug("[GPDWin5] Connection monitor thread started");
            int reconnectAttempts = 0;

            while (_monitoring)
            {
                try
                {
                    Thread.Sleep(5000); // Check every 5 seconds

                    if (!_monitoring)
                        break;

                    // Check if connected
                    bool currentlyConnected = _win5Controller?.IsConnected ?? false;

                    if (!currentlyConnected && _win5ControllerConnected)
                    {
                        // Was connected, now disconnected
                        Logger.Warn("[GPDWin5] Connection lost - will attempt to reconnect");
                        _win5ControllerConnected = false;
                        Win5Connected.SetConnected(false);
                    }

                    if (!currentlyConnected && isWin5Detected)
                    {
                        // Attempt to reconnect
                        reconnectAttempts++;
                        if (reconnectAttempts <= 3 || reconnectAttempts % 12 == 0) // Log first 3, then every minute
                        {
                            Logger.Info($"[GPDWin5] Attempting reconnection (attempt #{reconnectAttempts})...");
                        }

                        if (_win5Controller != null && _win5Controller.Connect())
                        {
                            Logger.Info("[GPDWin5] Reconnection successful!");
                            _win5ControllerConnected = true;
                            Win5Connected.SetConnected(true);
                            reconnectAttempts = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"[GPDWin5] Connection monitor error: {ex.Message}");
                }
            }

            Logger.Debug("[GPDWin5] Connection monitor thread stopped");
        }

        /// <summary>
        /// Event handler for Win 5 controller connection changes.
        /// </summary>
        private void OnWin5ConnectionChanged(object sender, bool connected)
        {
            Logger.Info($"[GPDWin5] Connection changed: {(connected ? "Connected" : "Disconnected")}");
            _win5ControllerConnected = connected;
            Win5Connected.SetConnected(connected);
        }

        /// <summary>
        /// Event handler for Win 5 HID command execution (for debugging).
        /// </summary>
        private void OnWin5CommandExecuted(object sender, GPDHidCommandEventArgs e)
        {
            string direction = e.IsSent ? "SENT" : "RECV";
            Logger.Debug($"[GPDWin5] HID {direction}: {e.Hex}");
        }

        /// <summary>
        /// Checks if the device type is a GPD device
        /// </summary>
        private bool IsGPDDevice(DeviceType deviceType)
        {
            return deviceType == DeviceType.GPDWinMini ||
                   deviceType == DeviceType.GPDWin4 ||
                   deviceType == DeviceType.GPDWin5;
        }

        /// <summary>
        /// Gets a friendly model name for the detected device.
        /// </summary>
        private string GetDeviceModelName()
        {
            switch (deviceInfo.DeviceType)
            {
                case DeviceType.GPDWinMini: return "GPD Win Mini";
                case DeviceType.GPDWin4: return "GPD Win 4";
                case DeviceType.GPDWin5: return "GPD Win 5";
                default: return "Unknown GPD Device";
            }
        }

        /// <summary>
        /// Gets whether this device supports fan control
        /// </summary>
        public bool SupportsFanControl => deviceInfo?.SupportsFanControl ?? false;

        /// <summary>
        /// Gets the detected device type
        /// </summary>
        public DeviceType DetectedDeviceType => deviceInfo?.DeviceType ?? DeviceType.Generic;

        /// <summary>
        /// Refreshes the Win 5 button configuration from the device.
        /// </summary>
        public void RefreshConfiguration()
        {
            Logger.Info("[GPD] RefreshConfiguration requested");
            if (isWin5Detected && _win5Controller?.IsConnected == true)
            {
                ReadAndLogConfiguration();
            }
            else
            {
                Logger.Warn("[GPD] Cannot refresh - Win 5 controller not connected");
            }
        }

        #region Software Fan Curve

        /// <summary>
        /// Sets the PerformanceManager reference for CPU temperature access.
        /// Called from Program.cs after both managers are initialized.
        /// </summary>
        public void SetPerformanceManager(PerformanceManager manager)
        {
            performanceManager = manager;
            Logger.Info($"[GPDFan] PerformanceManager reference set, CPUTemperature sensor available: {manager?.CPUTemperature != null}");
        }

        /// <summary>
        /// Enables or disables the software fan curve.
        /// When disabled, restores Auto fan mode.
        /// </summary>
        public void SetFanCurveEnabled(bool enabled)
        {
            Logger.Info($"[GPDFan] SetFanCurveEnabled: {enabled}");
            fanCurveEnabled = enabled;

            if (!enabled)
            {
                // Restore Auto mode when disabling fan curve
                lastAppliedFanSpeed = -1;
                if (_fanController != null && _fanController.IsReady)
                {
                    _fanController.SetFanMode(GPDFanMode.Auto);
                    FanMode?.UpdateMode(GPDFanMode.Auto);
                }
            }

            // Save enabled state
            LocalSettingsHelper.SetValue("GPDFanCurveEnabled", enabled);
        }

        /// <summary>
        /// Updates the fan curve data from the widget.
        /// </summary>
        public void SetFanCurveData(string data)
        {
            Logger.Info($"[GPDFan] SetFanCurveData: {data}");
            try
            {
                var parts = data?.Split(',');
                if (parts != null && parts.Length == 10)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        fanCurveValues[i] = Math.Max(0, Math.Min(100, int.Parse(parts[i].Trim())));
                    }
                    // Reset lastAppliedFanSpeed so new curve takes effect immediately
                    lastAppliedFanSpeed = -1;
                    // Save curve data
                    LocalSettingsHelper.SetValue("GPDFanCurveData", data);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[GPDFan] Error parsing fan curve data: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets whether the fan curve graph is visible in the widget.
        /// When visible, helper pushes CPU temp updates.
        /// </summary>
        public void SetFanCurveVisible(bool visible)
        {
            Logger.Info($"[GPDFan] SetFanCurveVisible: {visible}");
            fanCurveVisible = visible;

            // Push current temp immediately when becoming visible
            if (visible && performanceManager != null)
            {
                CPUTemp?.UpdateTemp((int)performanceManager.CPUTemperature.Value);
            }
        }

        /// <summary>
        /// Interpolates the target fan speed for a given CPU temperature using the fan curve.
        /// </summary>
        /// <param name="tempC">CPU temperature in Celsius</param>
        /// <returns>Target fan speed percentage (0 = off/auto idle, 1-100 = manual speed)</returns>
        private int InterpolateFanSpeed(float tempC)
        {
            // Below minimum temp → first point value
            if (tempC <= FanCurveTemps[0])
                return fanCurveValues[0];

            // Above maximum temp → 100%
            if (tempC >= FanCurveTemps[9])
                return 100;

            // Find the two surrounding points and interpolate
            for (int i = 0; i < 9; i++)
            {
                if (tempC >= FanCurveTemps[i] && tempC <= FanCurveTemps[i + 1])
                {
                    float t = (tempC - FanCurveTemps[i]) / (float)(FanCurveTemps[i + 1] - FanCurveTemps[i]);
                    int speed = (int)Math.Round(fanCurveValues[i] + t * (fanCurveValues[i + 1] - fanCurveValues[i]));
                    speed = Math.Max(0, Math.Min(100, speed));

                    // GPD minimum manual speed is 30%, so clamp 1-29 up to 30
                    // Value 0 means "off" — allow auto idle
                    if (speed > 0 && speed < 30)
                        speed = 30;

                    return speed;
                }
            }

            return 100; // Fallback
        }

        /// <summary>
        /// Loads saved fan curve settings from LocalSettings.
        /// </summary>
        private void LoadFanCurveSettings()
        {
            try
            {
                if (LocalSettingsHelper.TryGetValue("GPDFanCurveData", out string savedData))
                {
                    var parts = savedData?.Split(',');
                    if (parts != null && parts.Length == 10)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            fanCurveValues[i] = Math.Max(0, Math.Min(100, int.Parse(parts[i].Trim())));
                        }
                        Logger.Info($"[GPDFan] Loaded saved fan curve data: {savedData}");
                    }
                }

                if (LocalSettingsHelper.TryGetValue("GPDFanCurveEnabled", out bool savedEnabled))
                {
                    fanCurveEnabled = savedEnabled;
                    Logger.Info($"[GPDFan] Loaded saved fan curve enabled: {savedEnabled}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[GPDFan] Error loading fan curve settings: {ex.Message}");
            }
        }

        #endregion

        #region IManager Implementation

        public override void Update()
        {
            // Periodic fan RPM update
            if (_fanController != null && _fanController.IsReady)
            {
                var now = DateTime.Now;
                if ((now - _lastFanRPMUpdate).TotalMilliseconds >= FAN_RPM_UPDATE_INTERVAL_MS)
                {
                    _lastFanRPMUpdate = now;
                    try
                    {
                        int rpm = _fanController.GetFanRPM();
                        FanRPM?.UpdateRPM(rpm);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"[GPD] Error updating fan RPM: {ex.Message}");
                    }

                    // Software fan curve: read CPU temp and set fan speed
                    if (fanCurveEnabled && performanceManager != null)
                    {
                        try
                        {
                            float cpuTemp = performanceManager.CPUTemperature.Value;
                            int targetSpeed = InterpolateFanSpeed(cpuTemp);

                            if (targetSpeed != lastAppliedFanSpeed)
                            {
                                if (targetSpeed == 0)
                                {
                                    _fanController.SetFanMode(GPDFanMode.Auto);
                                }
                                else
                                {
                                    _fanController.SetFanSpeed(targetSpeed);
                                }
                                lastAppliedFanSpeed = targetSpeed;
                                Logger.Debug($"[GPDFan] Curve: temp={cpuTemp:F1}°C → speed={targetSpeed}%");
                            }

                            // Push CPU temp to widget if graph is visible
                            if (fanCurveVisible)
                            {
                                CPUTemp?.UpdateTemp((int)cpuTemp);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"[GPDFan] Error in fan curve update: {ex.Message}");
                        }
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("[GPD] GPDManager shutting down...");

                // Stop connection monitor
                _monitoring = false;
                if (_connectionMonitorThread != null && _connectionMonitorThread.IsAlive)
                {
                    _connectionMonitorThread.Join(1000);
                    _connectionMonitorThread = null;
                }

                // Dispose Win 5 controller
                if (_win5Controller != null)
                {
                    _win5Controller.ConnectionChanged -= OnWin5ConnectionChanged;
                    _win5Controller.CommandExecuted -= OnWin5CommandExecuted;
                    _win5Controller.Dispose();
                    _win5Controller = null;
                }

                // Restore auto mode if fan curve was active
                if (fanCurveEnabled && _fanController != null && _fanController.IsReady)
                {
                    try
                    {
                        _fanController.SetFanMode(GPDFanMode.Auto);
                        Logger.Info("[GPDFan] Restored Auto fan mode on shutdown");
                    }
                    catch { }
                }

                // Dispose fan controller (will restore auto mode)
                if (_fanController != null)
                {
                    _fanController.Dispose();
                    _fanController = null;
                }

                Logger.Info("[GPD] GPDManager shutdown complete");
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Button Remapping (Win 5)

        /// <summary>
        /// Remaps a single button on the Win 5 controller.
        /// </summary>
        /// <param name="buttonPosition">Button position (use GPDWin5Controller.ButtonPosition constants).</param>
        /// <param name="keycode">New keycode (use GPDWin5Keycodes constants).</param>
        /// <returns>True if successful.</returns>
        public bool RemapButton(int buttonPosition, ushort keycode)
        {
            return ApplyButtonMapping(buttonPosition, keycode);
        }

        /// <summary>
        /// Remaps multiple buttons at once on the Win 5 controller.
        /// </summary>
        /// <param name="mappings">Dictionary of button position to keycode.</param>
        /// <param name="r4Keycode">Optional R4 paddle keycode.</param>
        /// <returns>True if successful.</returns>
        public bool RemapButtons(Dictionary<int, ushort> mappings, ushort r4Keycode = 0x002B)
        {
            if (!isWin5Detected || _win5Controller == null)
            {
                Logger.Warn("[GPD] RemapButtons called but Win 5 not detected or controller not initialized");
                return false;
            }

            if (!_win5Controller.IsConnected)
            {
                Logger.Warn("[GPD] RemapButtons called but Win 5 controller not connected");
                return false;
            }

            if (mappings == null)
            {
                mappings = new Dictionary<int, ushort>();
            }

            Logger.Info($"[GPD] RemapButtons: {mappings.Count} buttons to remap");

            foreach (var mapping in mappings)
            {
                _win5Mappings[mapping.Key] = mapping.Value;
            }
            _win5R4Keycode = r4Keycode;

            return _win5Controller.RemapButtons(new Dictionary<int, ushort>(_win5Mappings), _win5R4Keycode);
        }

        /// <summary>
        /// Restores all button mappings to defaults on the Win 5 controller.
        /// </summary>
        /// <returns>True if successful.</returns>
        public bool RestoreDefaultMappings()
        {
            if (!isWin5Detected || _win5Controller == null)
            {
                Logger.Warn("[GPD] RestoreDefaultMappings called but Win 5 not detected or controller not initialized");
                return false;
            }

            if (!_win5Controller.IsConnected)
            {
                Logger.Warn("[GPD] RestoreDefaultMappings called but Win 5 controller not connected");
                return false;
            }

            Logger.Info("[GPD] RestoreDefaultMappings: restoring Win 5 button defaults");
            bool success = _win5Controller.RestoreDefaults();
            if (success)
            {
                _win5Mappings.Clear();
                var defaults = GPDWin5Controller.GetDefaultButtonMap();
                for (int i = 0; i < defaults.Length; i++)
                {
                    _win5Mappings[i] = defaults[i];
                }
                _win5R4Keycode = 0x002B;
            }
            return success;
        }

        public bool ApplyButtonMapping(int buttonPosition, ushort keycode)
        {
            if (!isWin5Detected || _win5Controller == null)
            {
                Logger.Warn("[GPD] ApplyButtonMapping called but Win 5 not detected or controller not initialized");
                return false;
            }

            if (!_win5Controller.IsConnected)
            {
                Logger.Warn("[GPD] ApplyButtonMapping called but Win 5 controller not connected");
                return false;
            }

            Logger.Info($"[GPD] ApplyButtonMapping: position={buttonPosition}, keycode=0x{keycode:X4}");
            _win5Mappings[buttonPosition] = keycode;
            return _win5Controller.RemapButtons(new Dictionary<int, ushort>(_win5Mappings), _win5R4Keycode);
        }

        public bool ApplyR4Mapping(ushort keycode)
        {
            if (!isWin5Detected || _win5Controller == null)
            {
                Logger.Warn("[GPD] ApplyR4Mapping called but Win 5 not detected or controller not initialized");
                return false;
            }

            if (!_win5Controller.IsConnected)
            {
                Logger.Warn("[GPD] ApplyR4Mapping called but Win 5 controller not connected");
                return false;
            }

            Logger.Info($"[GPD] ApplyR4Mapping: keycode=0x{keycode:X4}");
            _win5R4Keycode = keycode;
            return _win5Controller.RemapButtons(new Dictionary<int, ushort>(_win5Mappings), _win5R4Keycode);
        }

        #endregion

        #region Fan Control

        /// <summary>
        /// Initializes the GPD fan controller.
        /// </summary>
        private void InitializeFanController()
        {
            try
            {
                _fanController = new GPDFanController();

                if (_fanController.IsReady)
                {
                    Logger.Info("[GPD] Fan controller initialized successfully");

                    // Read initial fan RPM and notify widget
                    int rpm = _fanController.GetFanRPM();
                    FanRPM?.UpdateRPM(rpm);
                    Logger.Info($"[GPD] Initial fan RPM: {rpm}");
                }
                else
                {
                    Logger.Warn("[GPD] Fan controller failed to initialize - fan control unavailable");
                    _fanController?.Dispose();
                    _fanController = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPD] Error initializing fan controller: {ex.Message}");
                _fanController = null;
            }
        }

        /// <summary>
        /// Sets the fan speed (0 = auto, 30-100 = manual percentage).
        /// </summary>
        /// <param name="percent">Fan speed percentage. 0 enables auto mode.</param>
        /// <returns>True if successful.</returns>
        public bool SetFanSpeed(int percent)
        {
            if (!isGPDDetected || !deviceInfo.SupportsFanControl)
            {
                Logger.Warn("[GPD] SetFanSpeed called but device doesn't support fan control");
                return false;
            }

            if (_fanController == null || !_fanController.IsReady)
            {
                Logger.Warn("[GPD] SetFanSpeed called but fan controller not available");
                return false;
            }

            bool success = _fanController.SetFanSpeed(percent);

            if (success)
            {
                // Update mode property to reflect the change
                FanMode?.UpdateMode(_fanController.CurrentMode);
            }

            return success;
        }

        /// <summary>
        /// Sets the fan mode (Auto or Manual).
        /// </summary>
        /// <param name="mode">Fan mode.</param>
        /// <returns>True if successful.</returns>
        public bool SetFanMode(GPDFanMode mode)
        {
            if (!isGPDDetected || !deviceInfo.SupportsFanControl)
            {
                Logger.Warn("[GPD] SetFanMode called but device doesn't support fan control");
                return false;
            }

            if (_fanController == null || !_fanController.IsReady)
            {
                Logger.Warn("[GPD] SetFanMode called but fan controller not available");
                return false;
            }

            return _fanController.SetFanMode(mode);
        }

        /// <summary>
        /// Gets the current fan speed in RPM.
        /// </summary>
        /// <returns>Fan speed in RPM, or 0 if unavailable.</returns>
        public int GetFanSpeedRPM()
        {
            if (!isGPDDetected || !deviceInfo.SupportsFanControl)
            {
                return 0;
            }

            if (_fanController == null || !_fanController.IsReady)
            {
                return 0;
            }

            return _fanController.GetFanRPM();
        }

        /// <summary>
        /// Gets whether fan control is available.
        /// </summary>
        public bool IsFanControlAvailable => _fanController?.IsReady ?? false;

        #endregion
    }
}
