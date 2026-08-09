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
    ///
    /// Starts at 0 = "the helper has not reported yet", NOT at a guessed ceiling. This used to
    /// default to 30 (the A2VM's value) and that number silently truncated the helper's own TDP
    /// push on a Claw 8 EX: the helper sends TDP on pipe connect, the capabilities only arrive
    /// with the BatchGet answer ~200ms later, so a legitimate 35W landed on a slider still capped
    /// at 30 and was coerced down — after which UpdateTDPSliderEnabledState copied the coerced UI
    /// value back into the TDP property and the post-sync "correction" made it stick. Measured in
    /// a user's bundle on 2026-08-08: six sessions, every one of them 35W -> 30W, two of them with
    /// no resume involved at all. A ceiling we do not know yet must clamp nothing.
    /// </summary>
    internal class DeviceMaxPL1Property : WidgetProperty<int>
    {
        private readonly Page owner;
        private Action<int> valueCallback;

        public DeviceMaxPL1Property(Page inOwner)
            : base(0, null, Function.DeviceMaxPL1)
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
