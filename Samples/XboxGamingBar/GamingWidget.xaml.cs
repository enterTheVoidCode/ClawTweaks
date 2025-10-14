using Microsoft.Gaming.XboxGameBar;
using NLog;
using Shared.Data;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
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

        // Xbox Game Bar logic
        private XboxGameBarWidget widget = null;
        private XboxGameBarWidgetActivity widgetActivity = null;

        // Properties
        private readonly OSDProperty osd;
        private readonly TDPProperty tdp;
        private readonly RunningGameProperty runningGame;
        private readonly PerGameProfileProperty perGameProfile;
        private readonly WidgetProperties properties;

        public GamingWidget()
        {
            InitializeComponent();
            tdp = new TDPProperty(4, TDPSlider, this);
            osd = new OSDProperty(0, PerformanceOverlaySlider, this);
            runningGame = new RunningGameProperty(CurrentGameText, PerGameProfileToggle, this);
            perGameProfile = new PerGameProfileProperty(PerGameProfileToggle, this);
            properties = new WidgetProperties(osd, tdp, runningGame, perGameProfile);
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            //while (!System.Diagnostics.Debugger.IsAttached)
            //{
            //    await Task.Delay(500);
            //}

            widget = e.Parameter as XboxGameBarWidget;

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

        /// <summary>
        /// When the desktop process is connected, get ready to send/receive requests
        /// </summary>
        private async void GamingWidget_AppServiceConnected(object sender, AppServiceTriggerDetails e)
        {
            if (widget != null && widgetActivity == null)
            {
                widgetActivity = new XboxGameBarWidgetActivity(widget, "XboxGamingBarActivity");
                Logger.Info("Create new activity to keep the widget runs in the background.");
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
