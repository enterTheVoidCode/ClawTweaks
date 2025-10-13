using Shared.Enums;
using System;
using System.Runtime.CompilerServices;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class PerGameProfileProperty : WidgetProperty<bool, ToggleSwitch>
    {
        public PerGameProfileProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.PerGameProfile, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.Toggled += ToggleSwitch_ValueChanged;
            }
        }

        private void ToggleSwitch_ValueChanged(object sender, RoutedEventArgs e)
        {
            Value = UI.IsOn;
        }

        protected override async void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update Per-Game Profile value {Value}.");
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => 
                {
                    UI.IsOn = Value;
                });
            }
        }
    }
}
