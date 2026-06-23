using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Indicates whether the Steam "steamxbox" upper-filter driver is active on the system.
    /// When true, HidHide cannot reliably hide/unhide the MSI Claw, breaking virtual controller
    /// emulation. Helper pushes the value on connect; widget blocks the emulation toggle.
    /// </summary>
    internal class SteamXboxDriverDetectedProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> detectedCallback;

        public SteamXboxDriverDetectedProperty(Page inOwner) : base(false, null, Function.SteamXboxDriverDetected)
        {
            owner = inOwner;
        }

        public void SetDetectedCallback(Action<bool> callback)
        {
            detectedCallback = callback;
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && detectedCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    detectedCallback(Value);
                });
            }
        }
    }
}
