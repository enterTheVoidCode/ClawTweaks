using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetToggleProperty : WidgetProperty<bool, ToggleSwitch>
    {
        public WidgetToggleProperty(bool inValue, Function inFunction, ToggleSwitch inUI, Page inOwner) : base(inValue, inFunction, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.Toggled += ToggleSwitch_ValueChanged;
                UI.IsOn = inValue;
            }
        }

        protected virtual void ToggleSwitch_ValueChanged(object sender, RoutedEventArgs e)
        {
            Value = UI.IsOn;
        }

        public override async Task Sync()
        {

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { UI.IsEnabled = false; });
            }

            await base.Sync();

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { UI.IsEnabled = true; });
            }
        }

        protected override async void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} value {Value}.");
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UI.IsOn = Value;
                });
            }
        }
    }
}
