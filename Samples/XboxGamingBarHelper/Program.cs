using NLog;
using Shared.Constants;
using Shared.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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

namespace XboxGamingBarHelper
{
    internal class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static Mutex singleInstanceMutex;
        private static AppServiceConnection connection = null;

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
        /// Hotkey manager for global keyboard shortcuts (Ctrl+Shift+D for Desktop Controls)
        /// </summary>
        private static HotkeyManager hotkeyManager;

        static async Task Main(string[] args)
        {
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
            finally
            {
                singleInstanceMutex?.ReleaseMutex();
                singleInstanceMutex?.Dispose();
            }
        }

        /// <summary>
        /// Initialize the app service connection
        /// </summary>
        private static void InitializeConnection()
        {
            Logger.Info("Initialize connection...");
            connection = new AppServiceConnection();
            connection.AppServiceName = "XboxGamingBarService";
            connection.PackageFamilyName = Package.Current.Id.FamilyName;
            connection.RequestReceived += Connection_RequestReceived;
            connection.ServiceClosed += Connection_ServiceClosed;
        }

        /// <summary>
        /// Connect to the widget with optional blocking retry
        /// </summary>
        private static async Task ConnectToWidget(bool blocking)
        {
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
            // Initialize app service connection.
            InitializeConnection();

            //while (!System.Diagnostics.Debugger.IsAttached)
            //{
            //    await Task.Delay(500);
            //}

            // ALL MANAGERS RE-ENABLED - LibreHardwareMonitor sensors disabled in PerformanceManager
            Logger.Info("Initialize Performance Manager.");
            performanceManager = new PerformanceManager(connection);
            Logger.Info("Initialize RTSS Manager.");
            rtssManager = new RTSSManager(performanceManager, connection);
            Logger.Info("Initialize Profile Manager.");
            profileManager = new ProfileManager(connection);
            Logger.Info("Initialize System Manager.");
            systemManager = new SystemManager(connection, profileManager.GameProfiles);
            Logger.Info("Initialize Power Manager.");
            powerManager = new PowerManager(connection, performanceManager.RyzenAdjHandle);
            Logger.Info("Initialize AMD Manager.");
            amdManager = new AMDManager(connection);
            Logger.Info("Initialize Lossless Scaling Manager.");
            losslessScalingManager = new LosslessScalingManager(connection);
            settingsManager = SettingsManager.CreateInstance(connection);
            Logger.Info("Initialize Legion Manager.");
            legionManager = new LegionManager(connection);
            Logger.Info("Initialize AutoTDP Manager.");
            autoTDPManager = new AutoTDPManager(connection, performanceManager, systemManager);

            Logger.Info("Initialize Default Game Profile Manager.");
            try
            {
                defaultGameProfileManager = new DefaultGameProfileManager(connection, performanceManager, rtssManager, systemManager, profileManager, legionManager);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize DefaultGameProfileManager: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                // Create a null-safe placeholder that won't crash
                defaultGameProfileManager = null;
            }

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
            if (defaultGameProfileManager != null)
            {
                Managers.Add(defaultGameProfileManager);
            }

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
                performanceManager.WinRing0AvailableProperty,
                performanceManager.PawnIOAvailableProperty,
                performanceManager.PawnIOInstalledProperty,
                performanceManager.InstallPawnIOProperty
            };

            // Add Default Game Profile properties if manager initialized successfully
            if (defaultGameProfileManager != null)
            {
                propertyList.Add(defaultGameProfileManager.ProfileAvailable);
                propertyList.Add(defaultGameProfileManager.ProfileData);
                propertyList.Add(defaultGameProfileManager.ProfileEnabled);
                propertyList.Add(defaultGameProfileManager.ForceProfile);
            }

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
            }

            // Initial blocking connection to widget
            await ConnectToWidget(true);

            Logger.Info($"Widget connection status: {appServiceConnectionStatus}");

            // Start battery monitoring after widget connection is established
            // (Starting before connection can cause issues with the AppService)
            if (legionManager != null)
            {
                legionManager.StartBatteryMonitoringIfConnected();
            }

            // Infinite loop - helper runs forever and auto-reconnects if needed
            while (true)
            {
                if (appServiceConnectionStatus != AppServiceConnectionStatus.Success)
                {
                    Logger.Info("Try to reconnect to the widget.");
                    await ConnectToWidget(false);
                }

                await Task.Delay(1000);

                foreach (var manager in Managers)
                {
                    manager.Update();
                }
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
        }

        private static void ApplyLegionControllerSettingsFromProfile()
        {
            var profile = profileManager.CurrentProfile;
            var profileName = profile.GameId.Name;

            Logger.Info($"Applying Legion controller settings from profile: {profileName}");

            // Button mappings
            if (!string.IsNullOrEmpty(profile.LegionButtonY1))
            {
                Logger.Debug($"Applying LegionButtonY1: {profile.LegionButtonY1}");
                legionManager.LegionButtonY1.SetValue(profile.LegionButtonY1);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonY2))
            {
                Logger.Debug($"Applying LegionButtonY2: {profile.LegionButtonY2}");
                legionManager.LegionButtonY2.SetValue(profile.LegionButtonY2);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonY3))
            {
                Logger.Debug($"Applying LegionButtonY3: {profile.LegionButtonY3}");
                legionManager.LegionButtonY3.SetValue(profile.LegionButtonY3);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonM1))
            {
                Logger.Debug($"Applying LegionButtonM1: {profile.LegionButtonM1}");
                legionManager.LegionButtonM1.SetValue(profile.LegionButtonM1);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonM2))
            {
                Logger.Debug($"Applying LegionButtonM2: {profile.LegionButtonM2}");
                legionManager.LegionButtonM2.SetValue(profile.LegionButtonM2);
            }
            if (!string.IsNullOrEmpty(profile.LegionButtonM3))
            {
                Logger.Debug($"Applying LegionButtonM3: {profile.LegionButtonM3}");
                legionManager.LegionButtonM3.SetValue(profile.LegionButtonM3);
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
                        Process.Start(startInfo);

                        response.Add("UpdateStatus", "Installing");
                        await args.Request.SendResponseAsync(response);
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

                await properties.OnRequestReceived(args.Request);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling request: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
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
    }
}
