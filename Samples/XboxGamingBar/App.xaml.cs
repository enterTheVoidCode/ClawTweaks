using Microsoft.Gaming.XboxGameBar;
using NLog;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using XboxGamingBar.Event;

namespace XboxGamingBar
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        public static BackgroundTaskDeferral AppServiceDeferral = null;
        public static AppServiceConnection Connection = null;
        public static event EventHandler AppServiceDisconnected;
        public static event EventHandler<AppServiceTriggerDetails> AppServiceConnected;

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
        /// Handles connection requests to the app service
        /// </summary>
        protected override void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            Logger.Info("App background activated");
            base.OnBackgroundActivated(args);

            if (args.TaskInstance.TriggerDetails is AppServiceTriggerDetails details)
            {
                // only accept connections from callers in the same package
                if (details.CallerPackageFamilyName == Package.Current.Id.FamilyName)
                {
                    // connection established from the fulltrust process
                    AppServiceDeferral = args.TaskInstance.GetDeferral();
                    args.TaskInstance.Canceled += OnTaskCanceled;

                    Connection = details.AppServiceConnection;
                    AppServiceConnected?.Invoke(this, args.TaskInstance.TriggerDetails as AppServiceTriggerDetails);
                }
            }
        }

        /// <summary>
        /// Task canceled here means the app service client is gone
        /// </summary>
        private void OnTaskCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            Logger.Info($"App task canceled because {reason}");
            AppServiceDeferral?.Complete();
            AppServiceDeferral = null;
            Connection = null;
            AppServiceDisconnected?.Invoke(this, new BackgroundTaskCancellationEventArgs(reason));
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            Logger.Info("App activated");
            XboxGameBarWidgetActivatedEventArgs widgetArgs = null;
            if (args.Kind == ActivationKind.Protocol)
            {
                var protocolArgs = args as IProtocolActivatedEventArgs;
                string scheme = protocolArgs.Uri.Scheme;
                if (scheme.Equals("ms-gamebarwidget"))
                {
                    widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
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
                    var rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    Window.Current.Content = rootFrame;

                    if (widgetArgs.AppExtensionId == "GamingWidget")
                    {
                        // Create Game Bar widget object which bootstraps the connection with Game Bar
                        gamingXboxGameBarWidget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
                        rootFrame.Navigate(typeof(GamingWidget), gamingXboxGameBarWidget);
                        gamingWidget = rootFrame.Content as GamingWidget;

                        Window.Current.Closed += GamingWidgetWindow_Closed;
                    }
                    else if (widgetArgs.AppExtensionId == "GamingWidgetSettings")
                    {
                        gamingSettingsXboxGameBarWidget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
                        rootFrame.Navigate(typeof(GamingWidgetSettings), gamingSettingsXboxGameBarWidget);
                        gamingWidgetSettings = rootFrame.Content as GamingWidgetSettings;

                        Window.Current.Closed += GamingSettingsWidgetWindow_Closed;
                    }

                    Window.Current.Activate();
                }
                else
                {
                    // You can perform whatever behavior you need based on the URI payload.
                }
            }
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
            gamingWidget = null;
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
            }
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

            gamingXboxGameBarWidget = null;
            if (gamingWidget != null && gamingWidget.WidgetActivity != null)
            {
                gamingWidget.WidgetActivity.Complete();
            }
            gamingWidget = null;

            deferral.Complete();
        }
    }
}
