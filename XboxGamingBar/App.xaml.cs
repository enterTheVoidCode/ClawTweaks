using Microsoft.Gaming.XboxGameBar;
using NLog;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using XboxGamingBar.IPC;

namespace XboxGamingBar
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        // Named Pipe client for communication with the helper
        public static NamedPipeClient PipeClient = null;
        public static event EventHandler PipeConnected;
        public static event EventHandler PipeDisconnected;
        public static event EventHandler<PipeMessageEventArgs> PipeMessageReceived;

        /// <summary>
        /// True after first successful pipe connection. Used to detect reconnection vs cold start.
        /// On reconnection, widget should accept helper's TDP mode instead of applying its own profile.
        /// This is static so it persists across widget instance recreations.
        /// </summary>
        public static bool HasEverConnectedToHelper { get; set; } = false;

        /// <summary>
        /// One-shot in-app-update detection. On the first entry of this process we compare the
        /// running package version against the version recorded on the previous run
        /// (LocalSettings["LastRunVersion"]). If they differ, this is the first start right after
        /// an in-app update — which is exactly when the standalone "app mode" window (OnLaunched)
        /// pops up over Game Bar. These flags let us recognise that case (logging now; an auto-close
        /// of the app-mode window can hang off JustUpdated later).
        /// </summary>
        public static bool JustUpdated { get; private set; } = false;
        // True on the very first launch after a fresh install (no previous version recorded). Like
        // JustUpdated, this is a Windows auto-launch that collides with the Game Bar widget activation,
        // so we show the notice window instead of the full standalone widget.
        public static bool IsFirstRun { get; private set; } = false;
        public static string PreviousRunVersion { get; private set; } = "";
        public static string CurrentRunVersion { get; private set; } = "";
        private static bool _versionTransitionEvaluated = false;

        private static void LogVersionTransition(string entryPoint)
        {
            try
            {
                string current;
                try
                {
                    var v = Windows.ApplicationModel.Package.Current.Id.Version;
                    current = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                }
                catch { current = "unknown"; }

                if (_versionTransitionEvaluated)
                {
                    Logger.Info($"[VersionTransition] entry={entryPoint} (already evaluated: current={CurrentRunVersion}, previous={PreviousRunVersion}, justUpdated={JustUpdated})");
                    return;
                }
                _versionTransitionEvaluated = true;
                CurrentRunVersion = current;

                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                string previous = settings.Values.TryGetValue("LastRunVersion", out var pv) ? (pv as string ?? "") : "";
                PreviousRunVersion = previous;

                if (string.IsNullOrEmpty(previous))
                {
                    IsFirstRun = true;
                    Logger.Info($"[VersionTransition] entry={entryPoint}: first recorded run (current={current}, no previous version stored) — fresh install.");
                }
                else if (previous != current)
                {
                    JustUpdated = true;
                    Logger.Info($"[VersionTransition] entry={entryPoint}: VERSION CHANGED {previous} -> {current} — FIRST START AFTER IN-APP UPDATE.");
                }
                else
                {
                    Logger.Info($"[VersionTransition] entry={entryPoint}: version unchanged ({current}) — normal start.");
                }

                settings.Values["LastRunVersion"] = current;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[VersionTransition] failed: {ex.Message}");
            }
        }

        // Standalone "app mode" window (App.OnLaunched) — the ClawTweaks UI shown as a normal desktop
        // window, an alternative to the Game Bar widget. We track its CoreWindow so we can auto-close it
        // when Game Bar later launches the actual widget (so it doesn't linger and block the widget host).
        // Compact launch size so the narrow widget UI doesn't render stretched across the whole screen.
        private static Windows.UI.Core.CoreWindow appModeCoreWindow = null;
        private static Windows.UI.Core.CoreDispatcher appModeDispatcher = null;
        private static readonly Size AppModeWindowSize = new Size(480, 940);

        // True while the current window is the standalone "app mode" window (not the Game Bar widget).
        // The helper reads this (via AppModeWindowState notifications) to TOGGLE the window: the
        // "Open ClawTweaks Window" action closes it on a second press instead of re-launching.
        public static bool IsStandaloneAppMode { get; private set; }

        /// <summary>
        /// Closes the standalone app-mode window on request (helper sends Function.CloseAppModeWindow
        /// for the toggle). No-op if no app-mode window is open. Notifies the helper it's closed.
        /// </summary>
        public static void CloseStandaloneAppModeWindow()
        {
            var cw = appModeCoreWindow;
            var disp = appModeDispatcher;
            IsStandaloneAppMode = false;
            if (cw == null || disp == null) return;
            appModeCoreWindow = null;
            appModeDispatcher = null;
            Logger.Info("Closing standalone app-mode window on helper toggle request.");
            _ = disp.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try { cw.Close(); } catch (Exception ex) { Logger.Warn($"App-mode toggle close failed: {ex.Message}"); }
            });
            NotifyAppModeWindowState(false);
        }

        /// <summary>Best-effort notify the helper of the standalone app-mode window's open/closed state.</summary>
        internal static void NotifyAppModeWindowState(bool opened)
        {
            try
            {
                if (!IsConnected) return;
                var msg = new Shared.IPC.PipeMessage
                {
                    Command = Shared.Enums.Command.Set,
                    Function = Shared.Enums.Function.AppModeWindowState,
                    Content = opened ? "true" : "false"
                };
                _ = SendMessageAsync(msg.ToValueSet());
                Logger.Info($"Notified helper: app-mode window {(opened ? "opened" : "closed")}");
            }
            catch (Exception ex) { Logger.Warn($"NotifyAppModeWindowState failed: {ex.Message}"); }
        }

        // Track the active GamingWidget instance to prevent multiple instances from handling messages
        private static GamingWidget activeGamingWidget = null;
        private static readonly object activeWidgetLock = new object();

        /// <summary>
        /// Closes the standalone app-mode window if one is open and it isn't the window we're now using
        /// for the Game Bar widget. Called when Game Bar launches the widget, so opening Game Bar makes
        /// the standalone window disappear and the widget take over (per the user's auto-close choice).
        /// </summary>
        private static void CloseAppModeWindowIfOpen(Windows.UI.Core.CoreWindow exclude)
        {
            var cw = appModeCoreWindow;
            var disp = appModeDispatcher;
            if (cw == null || disp == null) return;
            if (ReferenceEquals(cw, exclude)) return; // the standalone window is being reused as the widget host
            appModeCoreWindow = null;
            appModeDispatcher = null;
            IsStandaloneAppMode = false;
            NotifyAppModeWindowState(false);
            Logger.Info("Closing standalone app-mode window — Game Bar is taking over the widget.");
            _ = disp.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try { cw.Close(); } catch (Exception ex) { Logger.Warn($"App-mode window close failed: {ex.Message}"); }
            });
        }

        public static void RegisterActiveGamingWidget(GamingWidget widget)
        {
            lock (activeWidgetLock)
            {
                if (activeGamingWidget != null && activeGamingWidget != widget)
                {
                    Logger.Info($"Replacing active GamingWidget. Old instance being deactivated.");
                    // Notify the old instance that it's no longer active so it can clean up
                    try
                    {
                        activeGamingWidget.OnDeactivated();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error deactivating old widget instance: {ex.Message}");
                    }
                }
                activeGamingWidget = widget;
                Logger.Info($"GamingWidget registered as active instance.");
            }
        }

        public static void UnregisterActiveGamingWidget(GamingWidget widget)
        {
            lock (activeWidgetLock)
            {
                if (activeGamingWidget == widget)
                {
                    Logger.Info($"Active GamingWidget unregistered.");
                    activeGamingWidget = null;
                }
            }
        }

        public static GamingWidget GetActiveGamingWidget()
        {
            lock (activeWidgetLock)
            {
                return activeGamingWidget;
            }
        }

        private XboxGameBarWidget gamingXboxGameBarWidget = null;
        private XboxGameBarWidget gamingSettingsXboxGameBarWidget = null;
        private GamingWidget gamingWidget = null;
        private GamingWidgetSettings gamingWidgetSettings = null;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.EnteredBackground += App_EnteredBackground;
            this.LeavingBackground += App_LeavingBackground;
            // Capture otherwise-silent XAML/async crashes (stowed exceptions) so the cause is
            // logged instead of only appearing as 0xc000027b in the Windows event log.
            this.UnhandledException += (s, e) =>
            {
                try
                {
                    Logger.Error($"!!! UNHANDLED EXCEPTION: {e.Message}\n{e.Exception}");
                    NLog.LogManager.Flush();
                }
                catch { }
            };
            //var installedLocation = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
            //var localFolder = ApplicationData.Current.LocalFolder.Path;
            //var localCache = ApplicationData.Current.LocalCacheFolder.Path;
            //Logger.Info($"App initializing {installedLocation} {localFolder} {localCache}");
        }

        private async void App_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            if (gamingWidget == null) return;

            await gamingWidget.GamingWidget_LeavingBackground(sender, e);
        }

        private void App_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            gamingWidget?.GamingWidget_EnteredBackground(sender, e);
        }

        /// <summary>
        /// Connects to the helper via Named Pipe.
        /// This works even when the helper is running elevated.
        /// </summary>
        public static async Task<bool> ConnectPipeAsync(int timeoutMs = 5000)
        {
            try
            {
                // Dispose existing client if any
                if (PipeClient != null)
                {
                    PipeClient.MessageReceived -= PipeClient_MessageReceived;
                    PipeClient.Connected -= PipeClient_Connected;
                    PipeClient.Disconnected -= PipeClient_Disconnected;
                    PipeClient.Dispose();
                    PipeClient = null;
                }

                PipeClient = new NamedPipeClient();
                PipeClient.MessageReceived += PipeClient_MessageReceived;
                PipeClient.Connected += PipeClient_Connected;
                PipeClient.Disconnected += PipeClient_Disconnected;

                return await PipeClient.ConnectAsync(timeoutMs);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error connecting to pipe: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the Named Pipe
        /// </summary>
        public static void DisconnectPipe()
        {
            if (PipeClient != null)
            {
                PipeClient.MessageReceived -= PipeClient_MessageReceived;
                PipeClient.Connected -= PipeClient_Connected;
                PipeClient.Disconnected -= PipeClient_Disconnected;
                PipeClient.Disconnect();
                PipeClient = null;
            }
        }

        /// <summary>
        /// Whether we have an active communication channel via Named Pipe
        /// </summary>
        public static bool IsConnected => PipeClient?.IsConnected == true;

        /// <summary>
        /// Sends a message and waits for a response via Named Pipe.
        /// </summary>
        public static async Task<ValueSet> SendMessageAsync(ValueSet message)
        {
            if (PipeClient?.IsConnected == true)
            {
                return await PipeClient.SendRequestAsync(message);
            }

            Logger.Warn("Cannot send message - pipe not connected");
            return null;
        }

        private static void PipeClient_MessageReceived(object sender, PipeMessageEventArgs e)
        {
            Logger.Debug($"Pipe message received from helper");
            PipeMessageReceived?.Invoke(sender, e);
        }

        private static void PipeClient_Connected(object sender, EventArgs e)
        {
            Logger.Info("Named pipe connected to helper");
            PipeConnected?.Invoke(sender, e);
        }

        private static void PipeClient_Disconnected(object sender, EventArgs e)
        {
            Logger.Info("Named pipe disconnected from helper");
            PipeDisconnected?.Invoke(sender, e);
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            Logger.Info($"=== App.OnActivated START === Kind={args.Kind}, PreviousExecutionState={args.PreviousExecutionState}");
            LogVersionTransition($"OnActivated:{args.Kind}");
            XboxGameBarWidgetActivatedEventArgs widgetArgs = null;
            if (args.Kind == ActivationKind.Protocol)
            {
                var protocolArgs = args as IProtocolActivatedEventArgs;
                string scheme = protocolArgs.Uri.Scheme;
                Logger.Info($"Protocol activation: scheme={scheme}, Uri={protocolArgs.Uri}");
                if (scheme.Equals("ms-gamebarwidget"))
                {
                    widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
                    Logger.Info($"Game Bar widget activation: AppExtensionId={widgetArgs?.AppExtensionId}, IsLaunchActivation={widgetArgs?.IsLaunchActivation}");
                }
            }
            if (widgetArgs != null)
            {
                //
                // Activation Notes:
                //
                //    If IsLaunchActivation is true, this is Game Bar launching a new instance
                // of our widget. This means we have a NEW CoreWindow with corresponding UI
                // dispatcher, and we MUST create and hold onto a new XboxGameBarWidget.
                //
                // Otherwise this is a subsequent activation coming from Game Bar. We MUST
                // continue to hold the XboxGameBarWidget created during initial activation
                // and ignore this repeat activation, or just observe the URI command here and act 
                // accordingly.  It is ok to perform a navigate on the root frame to switch 
                // views/pages if needed.  Game Bar lets us control the URI for sending widget to
                // widget commands or receiving a command from another non-widget process. 
                //
                // Important Cleanup Notes:
                //    When our widget is closed--by Game Bar or us calling XboxGameBarWidget.Close()-,
                // the CoreWindow will get a closed event.  We can register for Window.Closed
                // event to know when our particular widget has shutdown, and cleanup accordingly.
                //
                // NOTE: If a widget's CoreWindow is the LAST CoreWindow being closed for the process
                // then we won't get the Window.Closed event.  However, we will get the OnSuspending
                // call and can use that for cleanup.
                //
                if (widgetArgs.IsLaunchActivation)
                {
                    Logger.Info($"IsLaunchActivation=true: Creating new widget window. Window.Current={Window.Current?.GetHashCode()}, CoreWindow={Window.Current?.CoreWindow?.GetHashCode()}");
                    var rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    Window.Current.Content = rootFrame;

                    if (widgetArgs.AppExtensionId == "GamingWidget")
                    {
                        Logger.Info("Creating XboxGameBarWidget for GamingWidget...");
                        try
                        {
                            // Create Game Bar widget object which bootstraps the connection with Game Bar
                            gamingXboxGameBarWidget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
                            Logger.Info($"XboxGameBarWidget created: {gamingXboxGameBarWidget?.GetHashCode()}");
                            rootFrame.Navigate(typeof(GamingWidget), gamingXboxGameBarWidget);
                            gamingWidget = rootFrame.Content as GamingWidget;
                            Logger.Info($"GamingWidget navigated: {gamingWidget?.GetHashCode()}");

                            Window.Current.Closed += GamingWidgetWindow_Closed;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to create GamingWidget on launch activation: {ex.Message}");
                            Logger.Error($"Stack trace: {ex.StackTrace}");
                        }
                    }
                    else if (widgetArgs.AppExtensionId == "GamingWidgetSettings")
                    {
                        Logger.Info("Creating XboxGameBarWidget for GamingWidgetSettings...");
                        gamingSettingsXboxGameBarWidget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
                        rootFrame.Navigate(typeof(GamingWidgetSettings), gamingSettingsXboxGameBarWidget);
                        gamingWidgetSettings = rootFrame.Content as GamingWidgetSettings;

                        Window.Current.Closed += GamingSettingsWidgetWindow_Closed;
                    }

                    Logger.Info("Calling Window.Current.Activate()...");
                    Window.Current.Activate();
                    Logger.Info("Window activated successfully");

                    // Auto-close the standalone app-mode window (if any) now that Game Bar hosts the widget
                    // in its own view, so it doesn't linger as an orphan desktop window.
                    CloseAppModeWindowIfOpen(Window.Current?.CoreWindow);
                }
                else
                {
                    // Subsequent activation from Game Bar
                    // Check if we're running in app mode (no Game Bar widget) and should upgrade to widget mode
                    Logger.Info($"IsLaunchActivation=false: Subsequent Game Bar activation. AppExtensionId={widgetArgs.AppExtensionId}");
                    Logger.Info($"Current state: gamingXboxGameBarWidget={gamingXboxGameBarWidget?.GetHashCode() ?? 0}, gamingWidget={gamingWidget?.GetHashCode() ?? 0}, IsConnected={IsConnected}");
                    Logger.Info($"Window.Current={Window.Current?.GetHashCode()}, CoreWindow={Window.Current?.CoreWindow?.GetHashCode()}");

                    if (widgetArgs.AppExtensionId == "GamingWidget" && gamingXboxGameBarWidget == null)
                    {
                        Logger.Info("Running in app mode but received Game Bar activation. Attempting to upgrade to widget mode.");

                        // Get the existing frame or create a new one
                        var rootFrame = Window.Current.Content as Frame;
                        Logger.Info($"Existing rootFrame: {rootFrame?.GetHashCode() ?? 0}, Content type: {rootFrame?.Content?.GetType().Name ?? "null"}");
                        if (rootFrame == null)
                        {
                            Logger.Info("Creating new Frame for upgrade...");
                            rootFrame = new Frame();
                            rootFrame.NavigationFailed += OnNavigationFailed;
                            Window.Current.Content = rootFrame;
                        }

                        try
                        {
                            // Create the Game Bar widget object
                            Logger.Info("Creating XboxGameBarWidget for upgrade...");
                            gamingXboxGameBarWidget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
                            Logger.Info($"XboxGameBarWidget created successfully: {gamingXboxGameBarWidget.GetHashCode()}");

                            // Re-navigate to inject the widget context
                            Logger.Info("Navigating to GamingWidget with new widget context...");
                            rootFrame.Navigate(typeof(GamingWidget), gamingXboxGameBarWidget);
                            gamingWidget = rootFrame.Content as GamingWidget;
                            Logger.Info($"GamingWidget navigated: {gamingWidget?.GetHashCode() ?? 0}");

                            Window.Current.Closed -= GamingWidgetWindow_Closed; // Remove if already registered
                            Window.Current.Closed += GamingWidgetWindow_Closed;

                            Logger.Info("Calling Window.Current.Activate() for upgrade...");
                            Window.Current.Activate();
                            Logger.Info("Successfully upgraded from app mode to Game Bar widget mode.");

                            // This standalone window has BECOME the Game Bar widget host — stop tracking it
                            // as app-mode so the auto-close never targets the live widget window.
                            if (ReferenceEquals(appModeCoreWindow, Window.Current?.CoreWindow))
                            {
                                appModeCoreWindow = null;
                                appModeDispatcher = null;
                                IsStandaloneAppMode = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to upgrade to widget mode: {ex.Message}");
                            Logger.Error($"Stack trace: {ex.StackTrace}");
                        }
                    }
                    else if (widgetArgs.AppExtensionId == "GamingWidgetSettings" && gamingSettingsXboxGameBarWidget == null)
                    {
                        Logger.Info("Running in app mode but received Game Bar settings activation. Attempting to upgrade.");

                        var rootFrame = Window.Current.Content as Frame;
                        if (rootFrame == null)
                        {
                            rootFrame = new Frame();
                            rootFrame.NavigationFailed += OnNavigationFailed;
                            Window.Current.Content = rootFrame;
                        }

                        try
                        {
                            gamingSettingsXboxGameBarWidget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
                            rootFrame.Navigate(typeof(GamingWidgetSettings), gamingSettingsXboxGameBarWidget);
                            gamingWidgetSettings = rootFrame.Content as GamingWidgetSettings;

                            Window.Current.Closed -= GamingSettingsWidgetWindow_Closed;
                            Window.Current.Closed += GamingSettingsWidgetWindow_Closed;

                            Window.Current.Activate();
                            Logger.Info("Successfully upgraded settings to Game Bar widget mode.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to upgrade settings to widget mode: {ex.Message}");
                        }
                    }
                    else
                    {
                        Logger.Info($"Subsequent activation ignored. gamingXboxGameBarWidget is null: {gamingXboxGameBarWidget == null}");
                    }
                }
            }
            Logger.Info("=== App.OnActivated END ===");
        }

        private void GamingWidgetWindow_Closed(object sender, Windows.UI.Core.CoreWindowEventArgs e)
        {
            Logger.Info("App gaming widget closed");
            gamingXboxGameBarWidget = null;
            gamingWidget = null;
            Window.Current.Closed -= GamingWidgetWindow_Closed;
        }

        private void GamingSettingsWidgetWindow_Closed(object sender, Windows.UI.Core.CoreWindowEventArgs e)
        {
            Logger.Info("App gaming widget settings closed");
            gamingSettingsXboxGameBarWidget = null;
            gamingWidgetSettings = null;
            Window.Current.Closed -= GamingSettingsWidgetWindow_Closed;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Logger.Info("App launched");
            LogVersionTransition("OnLaunched");

            // If the Game Bar widget is already active, this OnLaunched was triggered by Windows
            // after an MSIX install (Windows auto-launches installed apps). Opening a standalone
            // window on top of an active Game Bar session obscures the widget and confuses the user.
            // Close immediately so the Game Bar overlay stays visible.
            if (gamingXboxGameBarWidget != null)
            {
                Logger.Info("OnLaunched with active Game Bar widget — suppressing standalone window (post-install auto-launch)");
                try { Window.Current?.Close(); } catch { }
                return;
            }

            // First launch right after an in-app (MSIX) update: Windows auto-launches the updated
            // package, which lands here (fresh process, no Game Bar widget yet) and would pop the big
            // standalone widget window on top of Game Bar. ClawTweaks lives in the Game Bar, so instead
            // of the full app show a small notice telling the user to close it and open Game Bar. (Just
            // closing the window left the OS splash screen hanging.)
            if (JustUpdated || IsFirstRun)
            {
                Logger.Info($"OnLaunched on a Windows auto-launch (justUpdated={JustUpdated}, firstRun={IsFirstRun}, {PreviousRunVersion} -> {CurrentRunVersion}) — showing the notice instead of the standalone widget window (avoids a cross-thread clash with the Game Bar widget activation).");
                try
                {
                    ShowPostUpdateNotice();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Post-update notice failed ({ex.Message}) — closing window instead.");
                    try { Window.Current?.Close(); } catch { }
                }
                return;
            }

            // Standalone "app mode": launch compact (not stretched across the whole screen). Set the
            // preferred size before the view is activated; this only affects the standalone desktop
            // window — the Game Bar host sizes the widget from the manifest, not this.
            try
            {
                ApplicationView.PreferredLaunchViewSize = AppModeWindowSize;
                ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
            }
            catch (Exception ex) { Logger.Warn($"App-mode preferred size failed: {ex.Message}"); }

            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    // When the navigation stack isn't restored navigate to the first page,
                    // configuring the new page by passing required information as a navigation
                    // parameter
                    rootFrame.Navigate(typeof(GamingWidget), e.Arguments);
                }
                // Ensure the current window is active
                Window.Current.Activate();

                // Track this as the standalone app-mode window and force a compact size (covers the case
                // where PreferredLaunchViewSize didn't apply, e.g. an already-running instance). Also
                // resize on close-tracking so the auto-close hook can find and dismiss it later.
                try
                {
                    appModeCoreWindow = Window.Current?.CoreWindow;
                    appModeDispatcher = Window.Current?.Dispatcher;
                    IsStandaloneAppMode = true;   // enables the helper-side open/close toggle
                    ApplicationView.GetForCurrentView()?.TryResizeView(AppModeWindowSize);
                    Window.Current.Closed -= AppModeWindow_Closed;
                    Window.Current.Closed += AppModeWindow_Closed;
                }
                catch (Exception ex) { Logger.Warn($"App-mode window setup failed: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Post-update notice window: shown instead of the standalone widget window on the first launch
        /// right after an in-app update (see OnLaunched / JustUpdated). Replaces the hanging OS splash
        /// with a clear "close this and open Game Bar" message. It tries to auto-close after a short
        /// while, but that doesn't always fire, so the text must not depend on it and instead tells the
        /// user exactly what to do next (open Game Bar, select ClawTweaks, approve the admin prompt).
        /// </summary>
        private void ShowPostUpdateNotice()
        {
            var title = new Windows.UI.Xaml.Controls.TextBlock
            {
                Text = JustUpdated ? "ClawTweaks updated" : "ClawTweaks installed",
                FontSize = 24,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White),
                HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Center,
                TextAlignment = Windows.UI.Xaml.TextAlignment.Center,
                TextWrapping = Windows.UI.Xaml.TextWrapping.Wrap
            };
            var body = new Windows.UI.Xaml.Controls.TextBlock
            {
                Text = "You can close this window. Press Win + G to open the Xbox Game Bar, select ClawTweaks, and wait for the administrator prompt to appear — approve it to finish.",
                FontSize = 15,
                Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 205)),
                HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Center,
                TextAlignment = Windows.UI.Xaml.TextAlignment.Center,
                TextWrapping = Windows.UI.Xaml.TextWrapping.Wrap,
                MaxWidth = 460,
                Margin = new Windows.UI.Xaml.Thickness(0, 14, 0, 0)
            };
            // No button: Window.Current.Close() is a no-op on the primary app view, so the notice can't
            // reliably close itself that way. Instead it auto-closes by exiting the process (the notice
            // IS the whole app at this point — an auto-launch with no widget yet). If a Game Bar widget
            // somehow exists in this process by then, don't kill it — just leave the window (the shell X
            // still works).
            void DismissNotice()
            {
                if (gamingXboxGameBarWidget == null && gamingSettingsXboxGameBarWidget == null)
                {
                    try { Application.Current.Exit(); } catch { }
                }
            }

            var stack = new Windows.UI.Xaml.Controls.StackPanel
            {
                HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment = Windows.UI.Xaml.VerticalAlignment.Center,
                MaxWidth = 480,
                Margin = new Windows.UI.Xaml.Thickness(24)
            };
            stack.Children.Add(title);
            stack.Children.Add(body);

            var root = new Windows.UI.Xaml.Controls.Grid
            {
                Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 22, 26))
            };
            root.Children.Add(stack);

            Window.Current.Content = root;
            try
            {
                ApplicationView.GetForCurrentView()?.TryResizeView(new Size(560, 360));
            }
            catch { }
            Window.Current.Activate();

            // If the user opens Game Bar (widget activation) before the auto-close fires, the guarded
            // Exit() is skipped so the widget survives — but then this notice would linger. Register it
            // so the widget-activation path (OnActivated -> CloseAppModeWindowIfOpen) closes it: once the
            // widget view exists this primary view is no longer the only one, so it can be closed.
            try
            {
                appModeCoreWindow = Window.Current?.CoreWindow;
                appModeDispatcher = Window.Current?.Dispatcher;
            }
            catch { }

            // Auto-close: show the notice long enough to read, then exit the process so the window
            // closes on its own (no button — see DismissNotice for why).
            try
            {
                var timer = new Windows.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                timer.Tick += (s, ev) => { timer.Stop(); DismissNotice(); };
                timer.Start();
            }
            catch { }
        }

        private void AppModeWindow_Closed(object sender, Windows.UI.Core.CoreWindowEventArgs e)
        {
            Logger.Info("Standalone app-mode window closed");
            if (ReferenceEquals(appModeCoreWindow, sender as Windows.UI.Core.CoreWindow) || appModeCoreWindow == Window.Current?.CoreWindow)
            {
                appModeCoreWindow = null;
                appModeDispatcher = null;
            }
            IsStandaloneAppMode = false;
            NotifyAppModeWindowState(false); // keep the helper's toggle state in sync (manual close too)
            try { Window.Current.Closed -= AppModeWindow_Closed; } catch { }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Normally we
        /// wouldn't know if the app was being terminated or just suspended at this
        /// point. However, the app will never be suspended if Game Bar has an
        /// active widget connection to it, so if you see this call it's safe to
        /// cleanup any widget related objects. Keep in mind if all widgets are closed
        /// and you have a foreground window for your app, this could still result in 
        /// suspend or terminate. Regardless, it should always be safe to cleanup
        /// your widget related objects.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            Logger.Info("App suspending");
            var deferral = e.SuspendingOperation.GetDeferral();

            // Don't manually complete widget activity here - let the widget's disconnect handler manage it
            // to avoid race conditions and double-disposal
            gamingXboxGameBarWidget = null;
            gamingWidget = null;
            gamingSettingsXboxGameBarWidget = null;
            gamingWidgetSettings = null;

            deferral.Complete();
        }
    }
}
