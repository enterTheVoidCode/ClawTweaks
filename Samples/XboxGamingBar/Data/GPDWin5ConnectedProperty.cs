using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if the GPD Win 5 HID controller is connected.
    /// Controls visibility of Win 5 specific UI elements.
    /// </summary>
    internal class GPDWin5ConnectedProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> connectionCallback;

        public GPDWin5ConnectedProperty(Page inOwner) : base(false, null, Function.GPDWin5Connected)
        {
            owner = inOwner;
        }

        public void SetConnectionCallback(Action<bool> callback)
        {
            connectionCallback = callback;
            // Invoke immediately with current value
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && connectionCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    connectionCallback(Value);
                });
            }
        }
    }

    /// <summary>
    /// Widget-facing Win 5 HID debug toggle.
    /// </summary>
    internal class GPDWin5HidDebugProperty : WidgetProperty<bool>
    {
        public GPDWin5HidDebugProperty() : base(false, null, Function.GPDWin5HidDebug)
        {
        }

        public void SetEnabled(bool enabled)
        {
            SetValue(enabled);
        }
    }

    /// <summary>
    /// Read-only JSON payload of Win 5 HID candidate interfaces from helper.
    /// </summary>
    internal class GPDWin5HidDevicesProperty : WidgetProperty<string>
    {
        private readonly Page owner;
        private Action<string> devicesCallback;

        public GPDWin5HidDevicesProperty(Page inOwner) : base("[]", null, Function.GPDWin5HidDevices)
        {
            owner = inOwner;
        }

        public void SetDevicesCallback(Action<string> callback)
        {
            devicesCallback = callback;
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && devicesCallback != null)
            {
                var payload = Value ?? "[]";
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    devicesCallback(payload);
                });
            }
        }
    }
}
