using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetToggleProperty : WidgetControlProperty<bool, ToggleSwitch>
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
            SetValue(UI.IsOn, DateTime.Now.Ticks);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
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
