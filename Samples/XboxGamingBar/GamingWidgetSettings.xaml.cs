using Microsoft.Gaming.XboxGameBar;
using System;
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
        private XboxGameBarWidget widget = null;

        private readonly AutoStartRTSSProperty autoStartRTSS;
        private readonly OnScreenDisplayProviderProperty onScreenDisplayProvider;

        private readonly WidgetProperties properties;

        private SolidColorBrush widgetDarkThemeBrush = null;
        private SolidColorBrush widgetLightThemeBrush = null;

        public GamingWidgetSettings()
        {
            this.InitializeComponent();

            autoStartRTSS = new AutoStartRTSSProperty(AutoStartRTSSToggle, this);
            onScreenDisplayProvider = new OnScreenDisplayProviderProperty(OnScreenDisplayProviderRadioButtons, this);
            properties = new WidgetProperties(autoStartRTSS);
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
    }
}
