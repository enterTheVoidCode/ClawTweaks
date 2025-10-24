using NLog;
using Shared.Constants;
using Shared.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Systems;

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
        private static List<IManager> Managers;
        private static AppServiceConnectionStatus appServiceConnectionStatus;

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

            // Initialize managers.
            Logger.Info("Initialize Performance Manager.");
            performanceManager = new PerformanceManager(connection);
            Logger.Info("Initialize RTSS Manager.");
            rtssManager = new RTSSManager(performanceManager, connection);
            Logger.Info("Initialize Profile Manager.");
            profileManager = new ProfileManager(connection);
            Logger.Info("Initialize System Manager.");
            systemManager = new SystemManager(connection, profileManager.GameProfiles);
            Logger.Info("Initialize Power Manager.");
            powerManager = new PowerManager(connection);
            Managers = new List<IManager>
            {
                performanceManager,
                rtssManager,
                profileManager,
                systemManager,
                powerManager
            };

            Logger.Info("Initialize properties.");
            // Initialize properties.
            properties = new HelperProperties(
                systemManager.RunningGame,
                rtssManager.OSD,
                performanceManager.TDP,
                profileManager.PerGameProfile,
                powerManager.CPUBoost,
                powerManager.CPUEPP,
                powerManager.LimitCPUClock,
                powerManager.CPUClockMax);

            Logger.Info("Initialize callbacks.");
            systemManager.RunningGame.PropertyChanged += RunningGame_PropertyChanged;
            profileManager.PerGameProfile.PropertyChanged += PerGameProfile_PropertyChanged;
            performanceManager.TDP.PropertyChanged += TDP_PropertyChanged;
            powerManager.CPUBoost.PropertyChanged += CPUBoost_PropertyChanged;
            powerManager.CPUEPP.PropertyChanged += CPUEPP_PropertyChanged;
            powerManager.LimitCPUClock.PropertyChanged += CPUClock_PropertyChanged;
            powerManager.CPUClockMax.PropertyChanged += CPUClock_PropertyChanged;
            profileManager.CurrentProfile.PropertyChanged += CurrentProfile_PropertyChanged;

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

                foreach (var manager in Managers)
                {
                    manager.Update();
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
                performanceManager.TDP.Value = profileManager.CurrentProfile.TDP;
                powerManager.CPUBoost.Value = profileManager.CurrentProfile.CPUBoost;
                powerManager.CPUEPP.Value = profileManager.CurrentProfile.CPUEPP;
                powerManager.LimitCPUClock.Value = profileManager.CurrentProfile.CPUClock > 0;
                powerManager.CPUClockMax.Value = profileManager.CurrentProfile.CPUClock > 0 ? profileManager.CurrentProfile.CPUClock : CPUConstants.DEFAULT_CPU_CLOCK;
                profileManager.PerGameProfile.Value = profileManager.CurrentProfile.Use;
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
            profileManager.CurrentProfile.Value = gameProfile;
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
                        profileManager.CurrentProfile.Value = runningGameProfile;
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
            }
            else
            {
                Logger.Info($"Stopped playing game, use global profile instead.");
                profileManager.CurrentProfile.Value = profileManager.GlobalProfile;
            }
        }

        /// <summary>
        /// Handles the event when the desktop process receives a request from the UWP app
        /// </summary>
        private static async void Connection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            Logger.Info($"Helper received message {args.Request.Message.ToDebugString()} from widget.");
            await properties.OnRequestReceived(args.Request);
        }

        /// <summary>
        /// Handles the event when the app service connection is closed
        /// </summary>
        private static void Connection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
        {
            Logger.Info("Lost connection to the app.");
            appServiceConnectionStatus = AppServiceConnectionStatus.AppServiceUnavailable;
        }
    }
}
