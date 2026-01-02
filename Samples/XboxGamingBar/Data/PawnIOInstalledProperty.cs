using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if PawnIO driver is installed.
    /// Controls the Install/Installed button state in TDP Method card.
    /// </summary>
    internal class PawnIOInstalledProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> installedCallback;

        public PawnIOInstalledProperty(Page inOwner) : base(false, null, Function.TdpMethod_PawnIOInstalled)
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
