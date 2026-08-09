using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only capability pushed by the helper: the STARTING value of the gyro's gravity-relative
    /// ("Accelerometer") toggle on a fresh install. This is not a support flag — the toggle is always
    /// offered, and a value the user has already stored always wins over this one. True everywhere
    /// except the Claw 8 EX, whose accelerometer axes are unverified against our A1M-derived remap and
    /// where users report the gyro is only usable with the toggle off. Mirrors
    /// DeviceSupportsCpuAdvancedProperty.
    /// </summary>
    internal class DeviceGyroWorldSpaceDefaultProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> valueCallback;

        public DeviceGyroWorldSpaceDefaultProperty(Page inOwner)
            : base(true, null, Function.DeviceGyroWorldSpaceDefault)
        {
            owner = inOwner;
        }

        public void SetValueCallback(Action<bool> callback)
        {
            valueCallback = callback;
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && valueCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    valueCallback(Value);
                });
            }
        }
    }
}
