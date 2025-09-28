using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.ServiceModel.Channels;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace XboxGamingBarCS
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GamingWidget : Page
    {
        public GamingWidget()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0) && App.Connection == null)
            {
                App.AppServiceConnected += GamingWidget_AppServiceConnected;
                App.AppServiceDisconnected += GamingWidget_AppServiceDisconnected;
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
            }
        }

        /// <summary>
        /// When the desktop process is connected, get ready to send/receive requests
        /// </summary>
        private async void GamingWidget_AppServiceConnected(object sender, AppServiceTriggerDetails e)
        {
            App.Connection.RequestReceived += AppServiceConnection_RequestReceived;
            ValueSet request = new ValueSet
            {
                { nameof(Command), (int)Command.Get },
                { nameof(Function), (int)Function.OSD },
            };
            AppServiceResponse response = await App.Connection.SendMessageAsync(request);
            if (response != null)
            {
                object value;
                if (response.Message.TryGetValue(nameof(Value), out value))
                {
                    var level = (int)value;
                    Debug.WriteLine($"Get OSD level {level} from desktop process");

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PerformanceOverlaySlider.Value = level;
                    });
                }
                else
                {
                    Debug.WriteLine("No Value in response from desktop process after connected???");
                }
            }
            else
            {
                Debug.WriteLine("No response from desktop process after connected???");
            }
                
            //await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            //{
            //    Debug.WriteLine("AppService Connected");
            //    // enable UI to access  the connection
            //    // btnRegKey.IsEnabled = true;
            //});
        }

        /// <summary>
        /// When the desktop process is disconnected, reconnect if needed
        /// </summary>
        private async void GamingWidget_AppServiceDisconnected(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Debug.WriteLine("AppService Disconnected");
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
            double d1 = (double)args.Request.Message["D1"];
            double d2 = (double)args.Request.Message["D2"];
            double result = d1 + d2;

            ValueSet response = new ValueSet();
            response.Add("RESULT", result);
            await args.Request.SendResponseAsync(response);

            // log the request in the UI for demo purposes
            await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                //tbRequests.Text += string.Format("Request: {0} + {1} --> Response = {2}\r\n", d1, d2, result);
            });
        }

        private async void PerformanceOverlaySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var level = (int)e.NewValue;
            ValueSet request = new ValueSet
            {
                { nameof(Command), (int)Command.Set },
                { nameof(Function), (int)Function.OSD },
                { nameof(Value), level }
            };
            AppServiceResponse response = await App.Connection.SendMessageAsync(request);
            if (response != null)
            {
                Debug.WriteLine($"Set OSD level {level} to desktop process");
            }
            else
            {
                Debug.WriteLine("No response from desktop process");
            }
        }
    }
}
