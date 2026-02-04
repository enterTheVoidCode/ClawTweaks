using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property for the detected device display name (e.g., "Legion Go", "Legion Go S").
    /// Used to update the device title in the Legion tab.
    /// </summary>
    internal class DeviceDisplayNameProperty : WidgetProperty<string>
    {
        private readonly Page owner;
        private Action<string> displayNameCallback;

        public DeviceDisplayNameProperty(Page inOwner) : base("Legion Go Controller", null, Function.DeviceDisplayName)
        {
            owner = inOwner;
        }

        public void SetDisplayNameCallback(Action<string> callback)
        {
            displayNameCallback = callback;
            // Invoke immediately with current value
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && displayNameCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    displayNameCallback(Value);
                });
            }
        }
    }
}
