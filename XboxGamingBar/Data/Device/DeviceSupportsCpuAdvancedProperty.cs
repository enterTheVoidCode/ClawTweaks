using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only capability pushed by the helper: whether the advanced CPU controls (processor
    /// scheduling policy, P/E core max frequency) may be used on this device. Default true so existing
    /// devices are unaffected; off on the Claw 8 EX (Panther Lake), where those settings are not
    /// reliably persistent — the card then shows the Boost toggle only and the expander stays
    /// disabled. Mirrors DeviceSupportsDriverManagementProperty.
    /// </summary>
    internal class DeviceSupportsCpuAdvancedProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> visibilityCallback;

        public DeviceSupportsCpuAdvancedProperty(Page inOwner)
            : base(true, null, Function.DeviceSupportsCpuAdvanced)
        {
            owner = inOwner;
        }

        public void SetVisibilityCallback(Action<bool> callback)
        {
            visibilityCallback = callback;
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && visibilityCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    visibilityCallback(Value);
                });
            }
        }
    }
}
