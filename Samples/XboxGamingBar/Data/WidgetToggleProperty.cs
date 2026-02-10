using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetToggleProperty : WidgetControlProperty<bool, ToggleSwitch>
    {
        /// <summary>
        /// Flag to indicate when the UI is being updated programmatically (from helper sync).
        /// When true, Toggled events should not send values back to the helper.
        /// </summary>
        private bool isUpdatingUI;

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
            // Skip if UI is being updated programmatically (from helper sync)
            // This prevents echoing values back to the helper and potential profile corruption
            if (isUpdatingUI)
            {
                return;
            }

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
                    // Set flag to prevent Toggled handler from echoing value back
                    isUpdatingUI = true;
                    try
                    {
                        UI.IsOn = Value;
                    }
                    finally
                    {
                        isUpdatingUI = false;
                    }
                });
            }
        }
    }
}
