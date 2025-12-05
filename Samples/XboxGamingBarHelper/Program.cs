using Microsoft.Win32;
using NLog;
using Shared.Constants;
using Shared.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
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

        static async Task Main(string[] args)
        {
            await Initialize();
        }

        /// <summary>
        /// Open connection to UWP app service
        /// </summary>
        private static async Task Initialize()
        {
            // Initialize app service connection.
            connection = new AppServiceConnection();
            connection.AppServiceName = "XboxGamingBarService";
            connection.PackageFamilyName = Package.Current.Id.FamilyName;
            connection.RequestReceived += Connection_RequestReceived;
            connection.ServiceClosed += Connection_ServiceClosed;

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

            // Set LegionManager reference in PerformanceManager for WMI TDP support
            performanceManager.SetLegionManager(legionManager);

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
                powerManager.LimitCPUClock,
                powerManager.CPUClockMax,
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
                autoTDPManager.TDPLimits,
                systemManager.ForceParkMode);

            Logger.Info("Initialize callbacks.");
            systemManager.RunningGame.PropertyChanged += RunningGame_PropertyChanged;
            profileManager.PerGameProfile.PropertyChanged += PerGameProfile_PropertyChanged;
            performanceManager.TDP.PropertyChanged += TDP_PropertyChanged;
            powerManager.CPUBoost.PropertyChanged += CPUBoost_PropertyChanged;
            powerManager.CPUEPP.PropertyChanged += CPUEPP_PropertyChanged;
            powerManager.LimitCPUClock.PropertyChanged += CPUClock_PropertyChanged;
            powerManager.CPUClockMax.PropertyChanged += CPUClock_PropertyChanged;
            // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
            //powerManager.LimitGPUClock.PropertyChanged += GPUClock_PropertyChanged;
            //powerManager.GPUClockMin.PropertyChanged += GPUClock_PropertyChanged;
            //powerManager.GPUClockMax.PropertyChanged += GPUClock_PropertyChanged;
            profileManager.CurrentProfile.PropertyChanged += CurrentProfile_PropertyChanged;

            // Subscribe to system power events for sleep/wake detection
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

            Logger.Info("Start connecting to the widget.");
            appServiceConnectionStatus = await connection.OpenAsync();
            if (appServiceConnectionStatus != AppServiceConnectionStatus.Success)
            {
                Logger.Info("Can't conncect to the widget.");
                return;
            }

            Logger.Info("Can't conncect to the widget.");
            while (appServiceConnectionStatus == AppServiceConnectionStatus.Success)
            {
                await Task.Delay(500);

                // Create a copy of the list to avoid "collection was modified" exception
                // if DisposeManagers() is called while we're iterating
                var managersCopy = Managers?.ToList();
                if (managersCopy != null)
                {
                    foreach (var manager in managersCopy)
                    {
                        manager?.Update();
                    }
                }
            }
            Logger.Info("Helper close...");
        }

        private static void CPUClock_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var cpuClock = powerManager.LimitCPUClock ? powerManager.CPUClockMax : 0;
            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU Clock from {profileManager.CurrentProfile.CPUClock} to {cpuClock}.");
            profileManager.CurrentProfile.CPUClock = cpuClock;
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
            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU Boost from {profileManager.CurrentProfile.CPUBoost} to {powerManager.CPUBoost}.");
            profileManager.CurrentProfile.CPUBoost = powerManager.CPUBoost;
        }

        private static void CPUEPP_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU EPP from {profileManager.CurrentProfile.CPUEPP} to {powerManager.CPUEPP}.");
            profileManager.CurrentProfile.CPUEPP = powerManager.CPUEPP;
        }

        private static void CurrentProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (profileManager.CurrentProfile.Use || profileManager.CurrentProfile.IsGlobalProfile)
            {
                Logger.Info($"Profile changed to {profileManager.CurrentProfile.GameId.Name}, apply it.");
                performanceManager.TDP.SetValue(profileManager.CurrentProfile.TDP);
                powerManager.CPUBoost.SetValue(profileManager.CurrentProfile.CPUBoost);
                powerManager.CPUEPP.SetValue(profileManager.CurrentProfile.CPUEPP);
                powerManager.LimitCPUClock.SetValue(profileManager.CurrentProfile.CPUClock > 0);
                powerManager.CPUClockMax.SetValue(profileManager.CurrentProfile.CPUClock > 0 ? profileManager.CurrentProfile.CPUClock : CPUConstants.DEFAULT_CPU_CLOCK);
                profileManager.PerGameProfile.SetValue(profileManager.CurrentProfile.Use);
            }
            else
            {
                Logger.Info($"Profile changed to {profileManager.CurrentProfile.GameId.Name} is not used.");
            }
        }

        private static void PerGameProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
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

        private static void TDP_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s TDP from {profileManager.CurrentProfile.TDP} to {performanceManager.TDP}.");
            profileManager.CurrentProfile.TDP = performanceManager.TDP;
        }

        private static void RunningGame_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
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
            }
            else
            {
                Logger.Info($"Stopped playing game, use global profile instead.");
                profileManager.CurrentProfile.SetValue(profileManager.GlobalProfile);
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
            Logger.Info("Lost connection to the app.");
            DisposeManagers();
            appServiceConnectionStatus = AppServiceConnectionStatus.AppServiceUnavailable;
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

        /// <summary>
        /// Handles system power mode changes (sleep/wake)
        /// </summary>
        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            Logger.Info($"Power mode changed: {e.Mode}");
            if (e.Mode == PowerModes.Resume)
            {
                Logger.Info("System resumed from sleep/hibernate, reinitializing RyzenAdj...");
                // Request RyzenAdj reinit on next TDP update by triggering a read
                if (performanceManager != null)
                {
                    // The existing retry/reinit logic in UpdateCurrentTDP will handle this
                    Logger.Info("RyzenAdj will reinitialize on next TDP read if needed.");
                }
            }
        }
    }
}
