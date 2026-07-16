using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only capability pushed by the helper: PL1 (sustained) TDP power-limit ceiling in watts.
    /// Per-model on the MSI Claw (A2VM = 30W, Claw 8 EX = 35W). Drives the TDP slider maximum.
    /// Mirrors DeviceSupportsFanControlProperty.
    /// </summary>
    internal class DeviceMaxPL1Property : WidgetProperty<int>
    {
        private readonly Page owner;
        private Action<int> valueCallback;

        public DeviceMaxPL1Property(Page inOwner)
            : base(30, null, Function.DeviceMaxPL1)
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
