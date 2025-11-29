using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class HDREnabledProperty : WidgetControlProperty<bool, ToggleSwitch>
    {
        public HDREnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.HDREnabled, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.Toggled += ToggleSwitch_Toggled;
            }
        }

        private void ToggleSwitch_Toggled(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (UI != null && UI.IsOn != Value)
            {
                Logger.Info($"{Function} toggle updated to {UI.IsOn}.");
                SetValue(UI.IsOn);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} toggle value {Value}.");
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UI.IsOn = Value;
                });
            }
        }
    }
}
