using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property for current fan RPM on GPD Win 5.
    /// Helper sends RPM updates to widget.
    /// </summary>
    internal class GPDFanRPMProperty : WidgetProperty<int>
    {
        private readonly Page owner;
        private Action<int> rpmCallback;

        public GPDFanRPMProperty(Page inOwner) : base(0, null, Function.GPDFanRPM)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Sets a callback to be invoked when RPM updates are received.
        /// </summary>
        public void SetRPMCallback(Action<int> callback)
        {
            rpmCallback = callback;
            // Invoke immediately with current value
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && rpmCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    rpmCallback(Value);
                });
            }
        }
    }
}
