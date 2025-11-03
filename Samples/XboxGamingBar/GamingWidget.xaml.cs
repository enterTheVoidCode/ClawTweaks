using Microsoft.Gaming.XboxGameBar;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using XboxGamingBar.Data;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace XboxGamingBar
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GamingWidget : Page
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly List<string> BlackListAppTrackerNames = new List<string>()
        {
            "App Installer", //Somehow App Installer shows up as a game sometimes
        };

        // Xbox Game Bar logic
        private XboxGameBarWidget widget = null;
        private XboxGameBarWidgetActivity widgetActivity = null;
        public XboxGameBarWidgetActivity WidgetActivity { get { return widgetActivity; } }
        private XboxGameBarAppTargetTracker appTargetTracker = null;

        private SolidColorBrush widgetDarkThemeBrush = null;
        private SolidColorBrush widgetLightThemeBrush = null;

        // Properties
        private readonly OSDProperty osd;
        private readonly TDPProperty tdp;
        private readonly RunningGameProperty runningGame;
        private readonly PerGameProfileProperty perGameProfile;
        private readonly CPUBoostProperty cpuBoost;
        private readonly CPUEPPProperty cpuEPP;
        private readonly LimitCPUClockProperty limitCPUClock;
        private readonly CPUClockMaxProperty cpuClockMax;
        private readonly RefreshRatesProperty refreshRates;
        private readonly RefreshRateProperty refreshRate;
        private readonly TrackedGameProperty trackedGame;
        private readonly RTSSInstalledProperty rtssInstalled;
        private readonly WidgetProperties properties;

        public GamingWidget()
        {
            InitializeComponent();
            tdp = new TDPProperty(4, TDPSlider, this);
            osd = new OSDProperty(0, PerformanceOverlaySlider, this);
            runningGame = new RunningGameProperty(RunningGameText, PerGameProfileToggle, this);
            perGameProfile = new PerGameProfileProperty(PerGameProfileToggle, this);
            cpuBoost = new CPUBoostProperty(CPUBoostToggle, this);
            cpuEPP = new CPUEPPProperty(80, CPUEPPSlider, this);
            limitCPUClock = new LimitCPUClockProperty(LimitCPUClockToggle, this);
            cpuClockMax = new CPUClockMaxProperty(CPUClockMaxSlider, this);
            refreshRates = new RefreshRatesProperty(RefreshRatesComboBox, this);
            refreshRate = new RefreshRateProperty(RefreshRatesComboBox, this);
            trackedGame = new TrackedGameProperty(new TrackedGame());
            rtssInstalled = new RTSSInstalledProperty(PerformanceOverlaySlider, this);
            properties = new WidgetProperties(osd, tdp, runningGame, perGameProfile, cpuBoost, cpuEPP, limitCPUClock, cpuClockMax, refreshRates, refreshRate, trackedGame, rtssInstalled);
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            //while (!System.Diagnostics.Debugger.IsAttached)
            //{
            //    await Task.Delay(500);
            //}

            widgetDarkThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 40, 44));
            widgetLightThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

            widget = e.Parameter as XboxGameBarWidget;
            if (widget != null)
            {
                Logger.Info("Running as a Xbox Game Bar widget.");
                await widget.CenterWindowAsync();
                widget.RequestedThemeChanged += GamingWidget_RequestedThemeChanged;
            }
            else
            {
                Logger.Info("XboxGameBarWidget not available, probably running as an app instead of widget.");
            }

            if (App.Connection == null && ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
            {
                Logger.Info("Launching a new full trust process.");
                App.AppServiceConnected += GamingWidget_AppServiceConnected;
                App.AppServiceDisconnected += GamingWidget_AppServiceDisconnected;
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
            }
        }

        public async Task GamingWidget_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            if (widget != null)
            {
                await widget.CenterWindowAsync();
            }

            if (App.Connection != null)
            {
                Logger.Info("GamingWidget LeavingBackground, sync UI now.");
                await properties.Sync();
            }
            else
            {
                Logger.Info("GamingWidget LeavingBackground but not connected to the full trust process.");
            }
        }

        public void GamingWidget_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            Logger.Info("GamingWidget EnterBackground.");
        }

        private void AppTargetTracker_TargetChanged(XboxGameBarAppTargetTracker sender, object args)
        {
            var settingEnabled = appTargetTracker.Setting == XboxGameBarAppTargetSetting.Enabled;

            XboxGameBarAppTarget target = null;
            if (settingEnabled)
            {
                target = appTargetTracker.GetTarget();
            }

            if (target == null)
            {
                //Logger.Info("Found no target.");
                trackedGame.SetValue(new TrackedGame());
            }
            else
            {
                if (target.IsGame && !BlackListAppTrackerNames.Contains(target.DisplayName))
                {
                    Logger.Info($"Tracked game DisplayName={target.DisplayName} AumId={target.AumId} TitleId={target.TitleId} IsFullscreen={target.IsFullscreen}");
                    trackedGame.SetValue(new TrackedGame(target.AumId, target.DisplayName, StringHelper.CleanStringForSerialization(target.TitleId), target.IsFullscreen));
                }
                else
                {
                    Logger.Info($"Tracked non-game DisplayName={target.DisplayName} AumId={target.AumId} TitleId={target.TitleId} IsFullscreen={target.IsFullscreen}");
                    trackedGame.SetValue(new TrackedGame());
                }
            }
        }

        /// <summary>
        /// When the desktop process is connected, get ready to send/receive requests
        /// </summary>
        private async void GamingWidget_AppServiceConnected(object sender, AppServiceTriggerDetails e)
        {
            if (widget != null)
            {
                if (widgetActivity == null)
                {
                    try
                    {
                        widgetActivity = new XboxGameBarWidgetActivity(widget, "XboxGamingBarActivity");
                        Logger.Info("Create new activity to keep the widget runs in the background.");
                    }
                    catch (ArgumentException argumentException)
                    {
                        Logger.Warn($"Can't create widget acitvity: {argumentException}.");
                    }
                }

                if (appTargetTracker == null)
                {
                    appTargetTracker = new XboxGameBarAppTargetTracker(widget);
                    appTargetTracker.SettingChanged += AppTargetTracker_TargetChanged;

                    if (appTargetTracker.Setting == XboxGameBarAppTargetSetting.Enabled)
                    {
                        Logger.Info("Created new app target tracker to track current game.");
                        var initialTarget = appTargetTracker.GetTarget();
                        if (initialTarget.IsGame)
                        {
                            Logger.Info($"Initial tracked game DisplayName={initialTarget.DisplayName} AumId={initialTarget.AumId} TitleId={initialTarget.TitleId} IsFullscreen={initialTarget.IsFullscreen}");
                            trackedGame.SetValue(new TrackedGame(initialTarget.AumId, initialTarget.DisplayName, StringHelper.CleanStringForSerialization(initialTarget.TitleId), initialTarget.IsFullscreen));
                        }
                        else
                        {
                            trackedGame.SetValue(new TrackedGame());
                            Logger.Info("No initial game target found.");
                        }
                        appTargetTracker.TargetChanged += AppTargetTracker_TargetChanged;
                    }
                    else
                    {
                        Logger.Info("Created new app target tracker but not enabled.");
                    }
                }
            }

            App.Connection.RequestReceived += AppServiceConnection_RequestReceived;
            await properties.Sync();
        }

        /// <summary>
        /// When the desktop process is disconnected, reconnect if needed
        /// </summary>
        private async void GamingWidget_AppServiceDisconnected(object sender, EventArgs e)
        {
            if (widgetActivity != null)
            {
                widgetActivity.Complete();
                widgetActivity = null;
                Logger.Info("Stopped widget activity.");
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Logger.Info("AppService Disconnected");
                // disable UI to access the connection
                // btnRegKey.IsEnabled = false;

                // ask user if they want to reconnect
                // Reconnect();
            });
        }

        private async void GamingWidget_RequestedThemeChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                SetBackgroundColor();
            });
        }

        private void SetBackgroundColor()
        {
            this.RequestedTheme = widget.RequestedTheme;
            RootGrid.Background = (widget.RequestedTheme == ElementTheme.Dark) ? widgetDarkThemeBrush : widgetLightThemeBrush;
        }

        /// <summary>
        /// Handle calculation request from desktop process
        /// (dummy scenario to show that connection is bi-directional)
        /// </summary>
        private async void AppServiceConnection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            Logger.Info($"Widget received message {args.Request.Message.ToDebugString()} from helper.");
            await properties.OnRequestReceived(args.Request);
        }
    }
}
