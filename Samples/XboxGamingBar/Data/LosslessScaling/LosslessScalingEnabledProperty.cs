using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingEnabledProperty : WidgetControlProperty<bool, ToggleButton>
    {
        public LosslessScalingEnabledProperty(ToggleButton inUI, Page inOwner) : base(false, Function.LosslessScalingEnabled, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.Checked += ToggleButton_Toggled;
                UI.Unchecked += ToggleButton_Toggled;
            }
        }

        private void ToggleButton_Toggled(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            bool newValue = UI.IsChecked ?? false;
            if (newValue != Value)
            {
                Logger.Info($"{Function} toggle button changed to {newValue}.");
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
                    if (UI.IsChecked != Value)
                    {
                        Logger.Info($"{Function} toggle button set to {Value}.");
                        UI.IsChecked = Value;
                    }
                });
            }
        }
    }
}
