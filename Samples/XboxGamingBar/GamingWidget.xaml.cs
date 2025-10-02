using NLog;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;
using XboxGamingBar.Internal;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace XboxGamingBar
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GamingWidget : Page
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public GamingWidget()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var helperIsRunning = false;
            DiagnosticAccessStatus diagnosticAccessStatus = await AppDiagnosticInfo.RequestAccessAsync();
            if (diagnosticAccessStatus == DiagnosticAccessStatus.Allowed)
            {
                IReadOnlyList<ProcessDiagnosticInfo> processes = ProcessDiagnosticInfo.GetForProcesses();
                helperIsRunning = processes.Where(x => x.ExecutableFileName == "XboxGamingBarHelper.exe").FirstOrDefault() != null;
            }

            if (helperIsRunning && App.FullTrustLaunchState == FullTrustLaunchState.NotLaunched)
            {
                if (App.Connection == null)
                {
                    Logger.Info("No AppServiceConnection yet, trying to connect to an existing full trust process");

                    App.Connection = new AppServiceConnection
                    {
                        AppServiceName = "XboxGamingBarService",
                        PackageFamilyName = Package.Current.Id.FamilyName
                    };
                    App.FullTrustLaunchState = FullTrustLaunchState.Reconnecting;
                    var status = await App.Connection.OpenAsync();
                    if (status != AppServiceConnectionStatus.Success)
                    {
                        Logger.Info($"Can't connect to any existing full trust process ({status}). Try to run it now.");
                        App.Connection = null;
                    }
                    else
                    {
                        App.FullTrustLaunchState = FullTrustLaunchState.Launched;
                        Logger.Info("Connected to an existing full trust process.");
                    }
                }
                else
                {
                    Logger.Info("Already have connection to the full trust process, no need to launch.");
                }
            }

            if (App.Connection == null && ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0) && App.FullTrustLaunchState != FullTrustLaunchState.Launching && App.FullTrustLaunchState != FullTrustLaunchState.Reconnecting)
            {
                Logger.Info("Launching a new full trust process.");
                App.AppServiceConnected += GamingWidget_AppServiceConnected;
                App.AppServiceDisconnected += GamingWidget_AppServiceDisconnected;
                App.FullTrustLaunchState = FullTrustLaunchState.Launching;
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
            }
        }

        /// <summary>
        /// When the desktop process is connected, get ready to send/receive requests
        /// </summary>
        private async void GamingWidget_AppServiceConnected(object sender, AppServiceTriggerDetails e)
        {
            App.FullTrustLaunchState = FullTrustLaunchState.Launched;
            App.Connection.RequestReceived += AppServiceConnection_RequestReceived;

            // Sync OSD.
            ValueSet request;
            AppServiceResponse response;

            request = new ValueSet
            {
                { nameof(Command), (int)Command.Get },
                { nameof(Function), (int)Function.OSD },
            };
            response = await App.Connection.SendMessageAsync(request);
            if (response != null)
            {
                object value;
                if (response.Message.TryGetValue(nameof(Value), out value))
                {
                    var level = (int)value;
                    Logger.Info($"Get OSD level {level} from desktop process");

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        PerformanceOverlaySlider.IsEnabled = true;
                        PerformanceOverlaySlider.Value = level;
                    });
                }
                else
                {
                    Logger.Info("No Value in response from desktop process after connected???");
                }
            }
            else
            {
                Logger.Info("No response from desktop process after connected???");
            }

            // Sync TDP.
            request = new ValueSet
            {
                { nameof(Command), (int)Command.Get },
                { nameof(Function), (int)Function.TDP },
            };
            response = await App.Connection.SendMessageAsync(request);
            if (response != null)
            {
                object value;
                if (response.Message.TryGetValue(nameof(Value), out value))
                {
                    var tdp = (int)value;
                    Logger.Info($"Get TDP limit {tdp}W from desktop process");

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        TDPSlider.IsEnabled = true;
                        TDPSlider.Value = tdp;
                    });
                }
                else
                {
                    Logger.Info("No Value in response from desktop process after connected???");
                }
            }
            else
            {
                Logger.Info("No response from desktop process after connected???");
            }
            //LoadingProgressRing.Visibility = Visibility.Collapsed;

            //await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            //{
            //    Logger.Info("AppService Connected");
            //    // enable UI to access  the connection
            //    // btnRegKey.IsEnabled = true;
            //});
        }

        /// <summary>
        /// When the desktop process is disconnected, reconnect if needed
        /// </summary>
        private async void GamingWidget_AppServiceDisconnected(object sender, EventArgs e)
        {
            App.FullTrustLaunchState = FullTrustLaunchState.NotLaunched;
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

            if (App.Connection != null)
            {
                ValueSet request = new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function), (int)Function.OSD },
                    { nameof(Value), level }
                };
                AppServiceResponse response = await App.Connection.SendMessageAsync(request);
                if (response != null)
                {
                    Logger.Info($"Set OSD level {level} to desktop process");
                }
                else
                {
                    Logger.Info($"No response from desktop process when trying to change OSD level {level}.");
                }
            }
            else
            {
                Logger.Info("No connection!");
            }
        }

        private async void TDPSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            TDPValueText.Text = $"{((int)e.NewValue).ToString()}W";
            
            if (App.Connection != null)
            {
                var tdp = (int)e.NewValue;
                ValueSet request = new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function), (int)Function.TDP },
                    { nameof(Value), tdp }
                };
                AppServiceResponse response = await App.Connection.SendMessageAsync(request);
                if (response != null)
                {
                    Logger.Info($"Set TDP limit {tdp}W to desktop process");
                }
                else
                {
                    Logger.Info($"No response from desktop process when trying to limit TDP {tdp}W");
                }
            }
            else
            {
                Logger.Info("No connection!");
            }
        }
    }
}
