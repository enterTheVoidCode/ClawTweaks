using NLog;
using Shared.Constants;
using Shared.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
        private static List<IManager> Managers;
        private static AppServiceConnectionStatus appServiceConnectionStatus;

        public static OnScreenDisplayProperty onScreenDisplay;
        public static List<OnScreenDisplayManager> onScreenDisplayProviders;

        // Properties
        private static HelperProperties properties;

        /// <summary>
        /// Guard flag to prevent reentrant profile change handling.
        /// Prevents race conditions during rapid game switches.
        /// </summary>
        private static bool isApplyingProfile = false;

        /// <summary>
        /// Lock object to ensure atomic profile application.
        /// Prevents race conditions when rapid game switches cause interleaved settings.
        /// </summary>
        private static readonly object profileApplicationLock = new object();

        /// <summary>
        /// Input injector for sending keyboard shortcuts (works in widget context unlike SendInput)
        /// </summary>
        private static InputInjector inputInjector;

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

            // Initialize input injector for keyboard shortcuts (works in widget context unlike SendInput)
            inputInjector = InputInjector.TryCreate();
            if (inputInjector == null)
            {
                Logger.Warn("Failed to create InputInjector - keyboard shortcuts may not work in widget");
            }

            // Set LegionManager reference in PerformanceManager for WMI TDP support
            performanceManager.SetLegionManager(legionManager);

            // PawnIO/RyzenSMU initialization disabled - waiting for CPU support
            // See: https://github.com/namazso/PawnIO.Modules/issues/XX (Ryzen Z2 Extreme model 0x24 not recognized)
            // Legion WMI is used for TDP control on Legion Go/Go S devices
            // performanceManager.InitializePawnIO();

            // Set LegionManager reference in RTSSManager for fan speed OSD support
            rtssManager.SetLegionManager(legionManager);

            // Set AutoTDPManager reference in RTSSManager for AutoTDP OSD support
            rtssManager.SetAutoTDPManager(autoTDPManager);

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

            Logger.Info("Initialize properties.");
            onScreenDisplay = new OnScreenDisplayProperty(0, null, rtssManager);
            onScreenDisplayProviders = new List<OnScreenDisplayManager>() { rtssManager, amdManager };
            //onScreenDisplay = new OnScreenDisplayProperty(0, null, amdManager);

            // Initialize properties.
            properties = new HelperProperties(
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
                legionManager.LegionGyroEnabled,
                legionManager.LegionVibration,
                legionManager.LegionPowerLight,
                legionManager.LegionChargeLimit,
                autoTDPManager.Enabled,
                autoTDPManager.TargetFPS,
                autoTDPManager.CurrentFPS,
                autoTDPManager.MinTDP,
                autoTDPManager.MaxTDP,
                autoTDPManager.TDPLimits,
                systemManager.ForceParkMode,
                performanceManager.TDPBoostEnabled,
                performanceManager.TDPBoostSPPT,
                performanceManager.TDPBoostFPPT);

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

            // Initial blocking connection to widget
            await ConnectToWidget(true);

            Logger.Info($"Widget connection status: {appServiceConnectionStatus}");

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
            Logger.Info("System resumed from sleep, re-apply current profile settings.");
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
    }
}
