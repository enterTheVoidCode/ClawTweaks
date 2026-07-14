using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only capability pushed by the helper: whether this device supports the custom fan curve.
    /// Per-model on the MSI Claw (e.g. off on the Claw 8 EX for now). Controls visibility of the MSI
    /// fan card + Fan nav item. Mirrors DeviceSupportsFirmwareKeyboardRemapProperty.
    /// </summary>
    internal class DeviceSupportsFanControlProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> visibilityCallback;

        public DeviceSupportsFanControlProperty(Page inOwner)
            : base(false, null, Function.DeviceSupportsFanControl)
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
