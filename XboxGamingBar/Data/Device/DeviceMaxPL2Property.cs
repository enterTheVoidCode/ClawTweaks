using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only capability pushed by the helper: PL2 (boost) TDP power-limit ceiling in watts.
    /// Per-model on the MSI Claw (A2VM = 37W, Claw 8 EX = 45W). Drives the TDP Boost slider maximum.
    /// Mirrors DeviceSupportsFanControlProperty.
    /// </summary>
    internal class DeviceMaxPL2Property : WidgetProperty<int>
    {
        private readonly Page owner;
        private Action<int> valueCallback;

        public DeviceMaxPL2Property(Page inOwner)
            : base(37, null, Function.DeviceMaxPL2)
        {
            owner = inOwner;
        }

        public void SetValueCallback(Action<int> callback)
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
