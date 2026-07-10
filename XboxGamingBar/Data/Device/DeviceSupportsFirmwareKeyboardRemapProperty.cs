using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only capability pushed by the helper: whether the controller firmware supports the
    /// verified button→keyboard remap (MSI Claw A2VM only). Controls visibility of the firmware
    /// keyboard-remap toggle in the Controller Status card. Mirrors DeviceSupportsGyroProperty.
    /// </summary>
    internal class DeviceSupportsFirmwareKeyboardRemapProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> visibilityCallback;

        public DeviceSupportsFirmwareKeyboardRemapProperty(Page inOwner)
            : base(false, null, Function.DeviceSupportsFirmwareKeyboardRemap)
        {
            owner = inOwner;
        }

        public void SetVisibilityCallback(Action<bool> callback)
        {
            visibilityCallback = callback;
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
