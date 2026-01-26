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
}
