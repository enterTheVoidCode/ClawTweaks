using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only capability pushed by the helper: whether this device exposes the Drivers tab (GPU
    /// driver updates etc.). Default true so non-MSI devices keep the tab; per-model on the MSI Claw
    /// (e.g. off on the Claw 8 EX / AMD A8 for now). Mirrors DeviceSupportsFanControlProperty.
    /// </summary>
    internal class DeviceSupportsDriverManagementProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> visibilityCallback;

        public DeviceSupportsDriverManagementProperty(Page inOwner)
            : base(true, null, Function.DeviceSupportsDriverManagement)
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
