using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property for GPD device display name (e.g., "GPD Win 5").
    /// Based on SMBIOS detection, independent of HID controller connection.
    /// </summary>
    internal class GPDDeviceNameProperty : WidgetProperty<string>
    {
        private readonly Page owner;
        private Action<string> nameCallback;

        public GPDDeviceNameProperty(Page inOwner) : base("GPD Device", null, Function.GPDDeviceName)
        {
            owner = inOwner;
        }

        public void SetNameCallback(Action<string> callback)
        {
            nameCallback = callback;
            // Invoke immediately with current value if not default
            if (!string.IsNullOrEmpty(Value) && Value != "GPD Device")
            {
                callback?.Invoke(Value);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && nameCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    nameCallback(Value);
                });
            }
        }
    }
}
