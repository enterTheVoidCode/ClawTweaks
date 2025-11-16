using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class CurrentTDPProperty : WidgetUIProperty<string, TextBlock>
    {
        public CurrentTDPProperty(TextBlock inUI, Page inOwner) : base("-- W", Function.CurrentTDP, inUI, inOwner)
        {
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Display the formatted string from helper (e.g., "S:21W F:21W L:21W")
                    UI.Text = !string.IsNullOrEmpty(Value) ? Value : "-- W";
                });
            }
        }
    }
}
