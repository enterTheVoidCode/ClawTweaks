using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if WinRing0 files are available in C:\GoTweaks.
    /// Controls visibility of the WinRing0 option in TDP Method dropdown.
    /// </summary>
    internal class WinRing0AvailableProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> availabilityCallback;

        public WinRing0AvailableProperty(Page inOwner) : base(false, null, Function.TdpMethod_WinRing0Available)
        {
            owner = inOwner;
        }

        public void SetAvailabilityCallback(Action<bool> callback)
        {
            availabilityCallback = callback;
            // Invoke immediately with current value
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && availabilityCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    availabilityCallback(Value);
                });
            }
        }
    }
}
