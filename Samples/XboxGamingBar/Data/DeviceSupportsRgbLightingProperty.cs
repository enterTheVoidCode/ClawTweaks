using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if the device supports HID RGB lighting control.
    /// Controls visibility of the Lighting section in the Legion tab.
    /// </summary>
    internal class DeviceSupportsRgbLightingProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> visibilityCallback;

        public DeviceSupportsRgbLightingProperty(Page inOwner) : base(true, null, Function.DeviceSupportsRgbLighting)
        {
            owner = inOwner;
        }

        public void SetVisibilityCallback(Action<bool> callback)
        {
            visibilityCallback = callback;
            // Invoke immediately with current value
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && visibilityCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    visibilityCallback(Value);
                });
            }
        }
    }
}
