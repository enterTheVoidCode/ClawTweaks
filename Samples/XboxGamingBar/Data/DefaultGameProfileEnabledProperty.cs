using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property indicating whether the default profile is currently enabled for this game.
    /// Binds to the toggle switch in the Default Profile card.
    /// </summary>
    internal class DefaultGameProfileEnabledProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private ToggleSwitch toggle;
        private Action<bool> enabledCallback;

        public DefaultGameProfileEnabledProperty(Page inOwner) : base(false, null, Function.DefaultGameProfileEnabled)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Sets a callback that's invoked when the enabled state changes.
        /// </summary>
        public void SetEnabledCallback(Action<bool> callback)
        {
            enabledCallback = callback;
            // Invoke immediately with current value
            callback?.Invoke(Value);
        }

        /// <summary>
        /// Binds this property to a toggle switch.
        /// </summary>
        public void BindToggle(ToggleSwitch toggleSwitch)
        {
            // Unsubscribe from previous toggle if any
            if (toggle != null)
            {
                toggle.Toggled -= Toggle_Toggled;
            }

            toggle = toggleSwitch;

            if (toggle != null)
            {
                toggle.IsOn = Value;
                toggle.Toggled += Toggle_Toggled;
            }
        }

        private void Toggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (toggle != null)
            {
                SetValue(toggle.IsOn, DateTime.Now.Ticks);
                // Invoke callback immediately for responsive UI
                enabledCallback?.Invoke(toggle.IsOn);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (toggle != null)
                    {
                        // Temporarily unsubscribe to avoid feedback loop
                        toggle.Toggled -= Toggle_Toggled;
                        toggle.IsOn = Value;
                        toggle.Toggled += Toggle_Toggled;
                    }
                    // Invoke callback when value changes from helper
                    enabledCallback?.Invoke(Value);
                });
            }
        }
    }
}
