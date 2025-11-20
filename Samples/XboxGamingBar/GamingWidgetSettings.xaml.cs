using Microsoft.Gaming.XboxGameBar;
using NLog;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Shared.Data;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using XboxGamingBar.Data;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace XboxGamingBar
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GamingWidgetSettings : Page
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private XboxGameBarWidget widget = null;

        private readonly OnScreenDisplayProviderProperty onScreenDisplayProvider;

        private readonly WidgetProperties properties;

        private SolidColorBrush widgetDarkThemeBrush = null;
        private SolidColorBrush widgetLightThemeBrush = null;

        public GamingWidgetSettings()
        {
            this.InitializeComponent();

            onScreenDisplayProvider = new OnScreenDisplayProviderProperty(OnScreenDisplayProviderRadioButtons, this);
            properties = new WidgetProperties(onScreenDisplayProvider);
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            widgetDarkThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 40, 44));
            widgetLightThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

            widget = e.Parameter as XboxGameBarWidget;
            widget.RequestedThemeChanged += GamingWidgetSettings_RequestedThemeChanged;

            await properties.Sync();
        }

        private async void GamingWidgetSettings_RequestedThemeChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
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
        public async Task RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            Logger.Info($"GamingWidget received message {args.Request.Message.ToDebugString()} from helper.");
            await properties.OnRequestReceived(args.Request);
        }
    }
}
