using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Fan mode property for GPD Win 5.
    /// 0 = Auto, 1 = Manual.
    /// Widget can set mode, and receives updates from helper.
    /// </summary>
    internal class GPDFanModeProperty : WidgetProperty<int>
    {
        private readonly Page owner;
        private Action<int> modeCallback;

        public GPDFanModeProperty(Page inOwner) : base(0, null, Function.GPDFanMode)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Sets a callback to be invoked when mode updates are received.
        /// </summary>
        public void SetModeCallback(Action<int> callback)
        {
            modeCallback = callback;
            // Invoke immediately with current value
            callback?.Invoke(Value);
        }

        /// <summary>
        /// Sets the fan mode and sends to helper.
        /// </summary>
        /// <param name="mode">0 = Auto, 1 = Manual</param>
        public void SetMode(int mode)
        {
            SetValue(mode);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && modeCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    modeCallback(Value);
                });
            }
        }
    }
}
