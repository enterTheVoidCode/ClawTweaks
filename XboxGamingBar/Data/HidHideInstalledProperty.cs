using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property indicating whether HidHide is installed.
    /// Read-only from widget perspective - helper pushes updates.
    /// </summary>
    internal class HidHideInstalledProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> installedCallback;

        public HidHideInstalledProperty(Page inOwner) : base(false, null, Function.HidHideInstalled)
        {
            owner = inOwner;
        }

        public void SetInstalledCallback(Action<bool> callback)
        {
            installedCallback = callback;
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
