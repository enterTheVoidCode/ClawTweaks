using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // Uses a ToggleSwitch: ON = PERFORMANCE, OFF = BALANCED
    internal class LosslessScalingSizeProperty : WidgetControlProperty<string, ToggleSwitch>
    {
        public LosslessScalingSizeProperty(ToggleSwitch inUI, Page inOwner)
            : base("BALANCED", Function.LosslessScalingSize, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.Toggled += Toggle_Toggled;
            }
        }

        private void Toggle_Toggled(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            string newValue = UI.IsOn ? "PERFORMANCE" : "BALANCED";
            if (newValue != Value)
            {
                Logger.Info($"{Function} toggle updated to {newValue}.");
                SetValue(newValue);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    bool shouldBeOn = Value == "PERFORMANCE";
                    if (UI.IsOn != shouldBeOn)
                    {
                        Logger.Info($"{Function} toggle set to {shouldBeOn} (value: {Value}).");
                        UI.IsOn = shouldBeOn;
                    }
                });
            }
        }
    }
}
