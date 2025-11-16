using Microsoft.Gaming.XboxGameBar;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using XboxGamingBar.Data;
using XboxGamingBar.Event;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace XboxGamingBar
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GamingWidget : Page, INotifyPropertyChanged
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
        private readonly IsForegroundProperty isForeground;

        // AMD properties
        private readonly AMDRadeonSuperResolutionEnabledProperty amdRadeonSuperResolutionEnabled;
        private readonly AMDRadeonSuperResolutionSupportedProperty amdRadeonSuperResolutionSupported;
        private readonly AMDRadeonSuperResolutionSharpnessProperty amdRadeonSuperResolutionSharpness;
        private readonly AMDFluidMotionFrameEnabledProperty amdFluidMotionFrameEnabled;
        private readonly AMDFluidMotionFrameSupportedProperty amdFluidMotionFrameSupported;
        private readonly AMDRadeonAntiLagEnabledProperty amdRadeonAntiLagEnabled;
        private readonly AMDRadeonAntiLagSupportedProperty amdRadeonAntiLagSupported;
        private readonly AMDRadeonBoostEnabledProperty amdRadeonBoostEnabled;
        private readonly AMDRadeonBoostSupportedProperty amdRadeonBoostSupported;
        private readonly AMDRadeonBoostResolutionProperty amdRadeonBoostResolution;
        private readonly AMDRadeonChillEnabledProperty amdRadeonChillEnabled;
        private readonly AMDRadeonChillSupportedProperty amdRadeonChillSupported;
        private readonly AMDRadeonChillMinFPSProperty amdRadeonChillMinFPSProperty;
        private readonly AMDRadeonChillMaxFPSProperty amdRadeonChillMaxFPSProperty;
        private string RadeonChillOnText => string.Format("Idle FPS: {0} - Max FPS: {1}", amdRadeonChillMinFPSProperty.Value, amdRadeonChillMaxFPSProperty.Value);

        private readonly WidgetProperties properties;

        public event PropertyChangedEventHandler PropertyChanged;

        public GamingWidget()
        {
            Logger.Info("GamingWidget constructor called - creating new instance.");
            InitializeComponent();

            // Register for lifecycle events
            this.Loaded += GamingWidget_Loaded;
            this.Unloaded += GamingWidget_Unloaded;
            Logger.Info("Registered Loaded and Unloaded event handlers.");

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
            isForeground = new IsForegroundProperty();
            amdRadeonSuperResolutionEnabled = new AMDRadeonSuperResolutionEnabledProperty(AMDRadeonSuperResolutionToggle, this);
            amdRadeonSuperResolutionSupported = new AMDRadeonSuperResolutionSupportedProperty(AMDRadeonSuperResolutionToggle, this);
            amdRadeonSuperResolutionSharpness = new AMDRadeonSuperResolutionSharpnessProperty(AMDRadeonSuperResolutionSharpnessSlider, this);
            amdFluidMotionFrameEnabled = new AMDFluidMotionFrameEnabledProperty(AMDFluidMotionFrameToggle, this);
            amdFluidMotionFrameSupported = new AMDFluidMotionFrameSupportedProperty(AMDFluidMotionFrameToggle, this);
            amdRadeonAntiLagEnabled = new AMDRadeonAntiLagEnabledProperty(AMDRadeonAntiLagToggle, this);
            amdRadeonAntiLagSupported = new AMDRadeonAntiLagSupportedProperty(AMDRadeonAntiLagToggle, this);
            amdRadeonBoostEnabled = new AMDRadeonBoostEnabledProperty(AMDRadeonBoostToggle, this);
            amdRadeonBoostSupported = new AMDRadeonBoostSupportedProperty(AMDRadeonBoostToggle, this);
            amdRadeonBoostResolution = new AMDRadeonBoostResolutionProperty(AMDRadeonBoostResolutionSlider, this);
            amdRadeonChillEnabled = new AMDRadeonChillEnabledProperty(AMDRadeonChillToggle, this);
            amdRadeonChillSupported = new AMDRadeonChillSupportedProperty(AMDRadeonChillToggle, this);
            amdRadeonChillMinFPSProperty = new AMDRadeonChillMinFPSProperty(AMDRadeonChillMinFPSSlider, this);
            amdRadeonChillMaxFPSProperty = new AMDRadeonChillMaxFPSProperty(AMDRadeonChillMaxFPSSlider, this);

            amdRadeonChillMinFPSProperty.PropertyChanged += AmdRadeonChillFPSChanged;
            amdRadeonChillMaxFPSProperty.PropertyChanged += AmdRadeonChillFPSChanged;

            properties = new WidgetProperties(
                osd,
                tdp,
                runningGame,
                perGameProfile,
                cpuBoost,
                cpuEPP,
                limitCPUClock,
                cpuClockMax,
                refreshRates,
                refreshRate,
                trackedGame,
                rtssInstalled,
                isForeground,
                amdRadeonSuperResolutionEnabled,
                amdRadeonSuperResolutionSupported,
                amdRadeonSuperResolutionSharpness,
                amdFluidMotionFrameEnabled,
                amdFluidMotionFrameSupported,
                amdRadeonAntiLagEnabled,
                amdRadeonAntiLagSupported,
                amdRadeonBoostEnabled,
                amdRadeonBoostSupported,
                amdRadeonBoostResolution,
                amdRadeonChillEnabled,
                amdRadeonChillSupported,
                amdRadeonChillMinFPSProperty,
                amdRadeonChillMaxFPSProperty
            );
        }

        private void GamingWidget_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Info($"GamingWidget_Loaded called. Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, App.Connection is null: {App.Connection == null}");
        }

        private void GamingWidget_Unloaded(object sender, RoutedEventArgs e)
        {
            Logger.Info($"GamingWidget_Unloaded called. Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, App.Connection is null: {App.Connection == null}");

            // Unregister this instance as the active widget
            Logger.Info("Unregistering this GamingWidget instance as the active widget.");
            App.UnregisterActiveGamingWidget(this);
            Logger.Info("GamingWidget instance unregistered.");

            // Unregister from static events to prevent memory leaks and duplicate handlers
            Logger.Info("Unregistering event handlers...");
            App.AppServiceConnected -= GamingWidget_AppServiceConnected;
            App.AppServiceDisconnected -= GamingWidget_AppServiceDisconnected;
            App.AppServiceRequestReceived -= AppServiceConnection_RequestReceived;
            Logger.Info("Event handlers unregistered.");

            // Clean up properties (stop debounce timers, unregister slider events)
            Logger.Info("Cleaning up properties...");
            properties.Cleanup();
            Logger.Info("Properties cleaned up.");

            // Clean up widget activity
            if (widgetActivity != null)
            {
                Logger.Info("Completing widget activity during page unload.");
                try
                {
                    widgetActivity.Complete();
                    widgetActivity = null;
                    Logger.Info("Widget activity completed and disposed.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error completing widget activity during unload: {ex.Message}");
                    widgetActivity = null;
                }
            }
            else
            {
                Logger.Info("No widget activity to clean up during unload.");
            }

            Logger.Info("GamingWidget_Unloaded completed.");
        }

        public void OnDeactivated()
        {
            Logger.Info("GamingWidget being deactivated - stopping pending updates.");
            try
            {
                properties.StopPendingUpdates();
                Logger.Info("Pending updates stopped.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error stopping pending updates: {ex.Message}");
            }
        }

        private void AmdRadeonChillFPSChanged(object sender, PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadeonChillOnText)));
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            Logger.Info("=== OnNavigatedTo START ===");
            Logger.Info($"Parameter type: {e.Parameter?.GetType().FullName ?? "null"}");
            Logger.Info($"Current state - Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, App.Connection is null: {App.Connection == null}");

            base.OnNavigatedTo(e);

            // Register this instance as the active widget to handle AppService messages
            Logger.Info("Registering this GamingWidget instance as the active widget.");
            App.RegisterActiveGamingWidget(this);
            Logger.Info("GamingWidget instance registered as active.");

            //while (!System.Diagnostics.Debugger.IsAttached)
            //{
            //    await Task.Delay(500);
            //}

            Logger.Info("Creating theme brushes...");
            widgetDarkThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 40, 44));
            widgetLightThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

            widget = e.Parameter as XboxGameBarWidget;
            if (widget != null)
            {
                Logger.Info($"Running as a Xbox Game Bar widget. Widget type: {widget.GetType().FullName}");

                Logger.Info("Calling widget.CenterWindowAsync()...");
                await widget.CenterWindowAsync();
                Logger.Info("widget.CenterWindowAsync() completed.");

                Logger.Info("Registering widget event handlers (RequestedThemeChanged, SettingsClicked)...");
                widget.RequestedThemeChanged += GamingWidget_RequestedThemeChanged;
                widget.SettingsClicked += GamingWidget_SettingsClicked;
                Logger.Info("Widget event handlers registered.");

                // Create widget activity if we have a widget but no activity yet
                if (widgetActivity == null)
                {
                    Logger.Info("Widget is available but activity not created yet. Creating now.");
                    await CreateWidgetActivity();
                }
                else
                {
                    Logger.Info($"WidgetActivity already exists, skipping creation.");
                }

                // Create app target tracker if not already created
                if (appTargetTracker == null)
                {
                    Logger.Info("AppTargetTracker is null, creating now.");
                    await CreateAppTargetTracker();
                }
                else
                {
                    Logger.Info("AppTargetTracker already exists, skipping creation.");
                }
            }
            else
            {
                Logger.Info("XboxGameBarWidget not available, probably running as an app instead of widget.");
            }

            if (App.Connection == null && ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
            {
                Logger.Info("App.Connection is null. Registering event handlers and launching full trust process.");
                // Use -= before += to ensure we don't register duplicate handlers
                App.AppServiceConnected -= GamingWidget_AppServiceConnected;
                App.AppServiceDisconnected -= GamingWidget_AppServiceDisconnected;
                App.AppServiceConnected += GamingWidget_AppServiceConnected;
                App.AppServiceDisconnected += GamingWidget_AppServiceDisconnected;

                Logger.Info("Launching full trust process via FullTrustProcessLauncher.");
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                Logger.Info("FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync completed.");
            }
            else
            {
                Logger.Info($"Not launching full trust process. App.Connection is null: {App.Connection == null}, FullTrustAppContract present: {ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0)}");

                // If connection already exists, register event handlers and sync properties
                if (App.Connection != null)
                {
                    Logger.Info("AppService connection already exists. Ensuring event handlers are registered.");

                    // Use -= before += to ensure we don't register duplicate handlers
                    Logger.Info("Unregistering existing event handlers (if any)...");
                    App.AppServiceConnected -= GamingWidget_AppServiceConnected;
                    App.AppServiceDisconnected -= GamingWidget_AppServiceDisconnected;
                    App.AppServiceRequestReceived -= AppServiceConnection_RequestReceived;

                    Logger.Info("Registering event handlers...");
                    App.AppServiceConnected += GamingWidget_AppServiceConnected;
                    App.AppServiceDisconnected += GamingWidget_AppServiceDisconnected;
                    App.AppServiceRequestReceived += AppServiceConnection_RequestReceived;
                    Logger.Info("Event handlers registered.");

                    // Sync properties since we're already connected
                    Logger.Info("Syncing properties with helper since connection already exists...");
                    await properties.Sync();
                    Logger.Info("Property sync completed.");
                }
            }

            Logger.Info("=== OnNavigatedTo END ===");
        }

        public async Task GamingWidget_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            Logger.Info($"GamingWidget_LeavingBackground called. Widget is null: {widget == null}, App.Connection is null: {App.Connection == null}, WidgetActivity is null: {widgetActivity == null}");

            if (widget != null)
            {
                await widget.CenterWindowAsync();
            }

            if (App.Connection != null)
            {
                Logger.Info("GamingWidget LeavingBackground, syncing UI properties with helper.");
                await properties.Sync();
            }
            else
            {
                Logger.Info("GamingWidget LeavingBackground but not connected to the full trust process.");
            }

            isForeground.SetValue(true);
            Logger.Info("GamingWidget_LeavingBackground completed.");
        }

        public void GamingWidget_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            Logger.Info($"GamingWidget_EnteredBackground called. WidgetActivity is null: {widgetActivity == null}");
            isForeground.SetValue(false);
        }

        private async Task CreateWidgetActivity()
        {
            Logger.Info("=== CreateWidgetActivity START ===");

            if (widget == null)
            {
                Logger.Warn("Cannot create widget activity - widget is null!");
                Logger.Info("=== CreateWidgetActivity END (skipped - no widget) ===");
                return;
            }

            if (widgetActivity != null)
            {
                Logger.Info("Widget activity already exists, skipping creation.");
                Logger.Info("=== CreateWidgetActivity END (skipped - already exists) ===");
                return;
            }

            try
            {
                // Use a unique activity ID to avoid conflicts when the widget is reopened
                string activityId = $"XboxGamingBarActivity_{Guid.NewGuid():N}";
                Logger.Info($"Attempting to create XboxGameBarWidgetActivity with activityId='{activityId}'");
                Logger.Info($"Widget object details - Type: {widget.GetType().FullName}, Widget.ToString(): {widget.ToString()}");

                Logger.Info("Calling XboxGameBarWidgetActivity constructor...");
                widgetActivity = new XboxGameBarWidgetActivity(widget, activityId);
                Logger.Info("XboxGameBarWidgetActivity constructor completed.");

                Logger.Info($"Successfully created widget activity with ID '{activityId}' to keep the widget running in the background.");
            }
            catch (ArgumentException argumentException)
            {
                Logger.Error($"ArgumentException when creating widget activity: {argumentException}");
                Logger.Error($"Exception details - Message: {argumentException.Message}, ParamName: {argumentException.ParamName}, StackTrace: {argumentException.StackTrace}");
                Logger.Warn("Widget activity creation failed, but widget may still function. Continuing...");
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected exception when creating widget activity: {ex}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
                Logger.Warn("Widget activity creation failed, but widget may still function. Continuing...");
            }

            Logger.Info("=== CreateWidgetActivity END ===");
        }

        private async Task CreateAppTargetTracker()
        {
            Logger.Info("=== CreateAppTargetTracker START ===");

            if (widget == null)
            {
                Logger.Warn("Cannot create app target tracker - widget is null!");
                Logger.Info("=== CreateAppTargetTracker END (skipped - no widget) ===");
                return;
            }

            if (appTargetTracker != null)
            {
                Logger.Info("AppTargetTracker already exists, skipping creation.");
                Logger.Info("=== CreateAppTargetTracker END (skipped - already exists) ===");
                return;
            }

            try
            {
                Logger.Info("Creating XboxGameBarAppTargetTracker...");
                appTargetTracker = new XboxGameBarAppTargetTracker(widget);
                appTargetTracker.SettingChanged += AppTargetTracker_TargetChanged;
                Logger.Info("XboxGameBarAppTargetTracker created.");

                if (appTargetTracker.Setting == XboxGameBarAppTargetSetting.Enabled)
                {
                    Logger.Info("App target tracker is enabled. Getting initial target...");
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

                    Logger.Info("Registering TargetChanged event handler...");
                    appTargetTracker.TargetChanged += AppTargetTracker_TargetChanged;
                    Logger.Info("TargetChanged event handler registered.");
                }
                else
                {
                    Logger.Info($"App target tracker created but not enabled. Setting: {appTargetTracker.Setting}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error creating app target tracker: {ex}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
            }

            Logger.Info("=== CreateAppTargetTracker END ===");
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
                Logger.Info("Found no target.");
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
            Logger.Info("=== GamingWidget_AppServiceConnected START ===");
            Logger.Info($"Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}");

            if (widget != null)
            {
                Logger.Info($"Widget state - RequestedTheme: {widget.RequestedTheme}");

                // Create widget activity if needed
                Logger.Info("Checking if widget activity needs to be created...");
                await CreateWidgetActivity();

                // Create app target tracker if needed
                Logger.Info("Checking if app target tracker needs to be created...");
                await CreateAppTargetTracker();
            }
            else
            {
                Logger.Info("Widget is null in AppServiceConnected - likely running as standalone app.");
            }

            // Register for request received events via the App-level relay
            Logger.Info("Registering for AppServiceRequestReceived events...");
            App.AppServiceRequestReceived -= AppServiceConnection_RequestReceived;
            App.AppServiceRequestReceived += AppServiceConnection_RequestReceived;
            Logger.Info("AppServiceRequestReceived handler registered.");

            Logger.Info("Starting property sync with helper...");
            try
            {
                await properties.Sync();
                Logger.Info("Property sync completed successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during property sync: {ex}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
            }

            Logger.Info("=== GamingWidget_AppServiceConnected END ===");
        }

        /// <summary>
        /// When the desktop process is disconnected, reconnect if needed
        /// </summary>
        private async void GamingWidget_AppServiceDisconnected(object sender, EventArgs e)
        {
            var eventArgs = e as BackgroundTaskCancellationEventArgs;
            Logger.Info($"GamingWidget_AppServiceDisconnected called. Reason: {eventArgs?.Reason.ToString() ?? "Unknown"}. WidgetActivity is null: {widgetActivity == null}, Widget is null: {widget == null}");

            // Unregister as active widget
            Logger.Info("Unregistering this widget as active due to disconnect.");
            App.UnregisterActiveGamingWidget(this);

            // Clean up properties
            Logger.Info("Cleaning up properties during disconnect...");
            try
            {
                properties.Cleanup();
                Logger.Info("Properties cleaned up.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error cleaning up properties: {ex.Message}");
            }

            // Clean up widget activity
            if (widgetActivity != null)
            {
                Logger.Info("Completing and disposing widget activity.");
                try
                {
                    widgetActivity.Complete();
                    widgetActivity = null;
                    Logger.Info("Widget activity stopped successfully.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error completing widget activity: {ex.Message}");
                    widgetActivity = null;
                }
            }
            else
            {
                Logger.Info("WidgetActivity was already null during disconnect.");
            }

            // Only relaunch if we're running as a widget (not standalone app)
            // and the disconnect was due to a crash/termination (not normal suspension)
            // AND we don't already have a connection (prevents duplicate launches)
            bool shouldRelaunch = widget != null &&
                                  eventArgs != null &&
                                  (eventArgs.Reason == BackgroundTaskCancellationReason.Abort ||
                                   eventArgs.Reason == BackgroundTaskCancellationReason.Terminating) &&
                                  App.Connection == null;  // Don't relaunch if connection still exists

            if (shouldRelaunch)
            {
                Logger.Info($"Widget disconnected due to {eventArgs.Reason} and no connection exists. Attempting to relaunch full trust process.");
                try
                {
                    await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error relaunching full trust process: {ex.Message}");
                }
            }
            else
            {
                Logger.Info($"Skipping relaunch. Widget is null: {widget == null}, Reason: {eventArgs?.Reason.ToString() ?? "Unknown"}, Connection exists: {App.Connection != null}");
            }
        }

        private async void GamingWidget_RequestedThemeChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SetBackgroundColor();
            });
        }

        private async void GamingWidget_SettingsClicked(XboxGameBarWidget sender, object args)
        {
            await widget.ActivateSettingsAsync();
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
        private async void AppServiceConnection_RequestReceived(object sender, AppServiceRequestReceivedEventArgs args)
        {
            try
            {
                // Only process messages if this is the active widget instance
                // This prevents multiple instances from handling the same message
                var activeWidget = App.GetActiveGamingWidget();
                if (activeWidget != null && activeWidget != this)
                {
                    Logger.Info($"Widget received message {args.Request.Message.ToDebugString()} from helper, but this is NOT the active instance. Ignoring.");
                    return;
                }

                Logger.Info($"Widget received message {args.Request.Message.ToDebugString()} from helper.");
                await properties.OnRequestReceived(args.Request);
                Logger.Info($"Widget finished processing message {args.Request.Message.ToDebugString()}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing message from helper: {ex.Message}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
            }
        }
    }
}
