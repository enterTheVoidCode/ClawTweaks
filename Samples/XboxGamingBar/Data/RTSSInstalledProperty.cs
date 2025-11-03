using System;
using Shared.Enums;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RTSSInstalledProperty : WidgetControlProperty<bool, Slider>
    {
        public RTSSInstalledProperty(Slider inUI, Page inOwner) : base(false, Function.RTSSInstalled, inUI, inOwner)
        {
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Logger.Debug($"{(Value ? "Enabled" : "Disabled")} OSD slider property changed.");
                    SetControlEnabled(Value);
                });
            }
        }

        public override async Task Sync()
        {
            await base.Sync();

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Logger.Debug($"{(Value ? "Enabled" : "Disabled")} OSD slider at sync.");
                    SetControlEnabled(Value);
                });
            }
        }
    }
}
