using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property indicating whether ViGEmBus driver is installed.
    /// Read-only from widget perspective - helper pushes updates.
    /// </summary>
    internal class ViGEmBusInstalledProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> installedCallback;

        public ViGEmBusInstalledProperty(Page inOwner) : base(false, null, Function.ViGEmBusInstalled)
        {
            owner = inOwner;
        }

        public void SetInstalledCallback(Action<bool> callback)
        {
            installedCallback = callback;
            // Invoke immediately with current value
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && installedCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    installedCallback(Value);
                });
            }
        }
    }
}
