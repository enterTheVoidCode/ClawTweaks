using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if handheld-agnostic controller emulation is supported.
    /// </summary>
    internal class ControllerEmulationAvailableProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> availabilityCallback;

        public ControllerEmulationAvailableProperty(Page inOwner) : base(false, null, Function.ControllerEmulationAvailable)
        {
            owner = inOwner;
        }

        public void SetAvailabilityCallback(Action<bool> callback)
        {
            availabilityCallback = callback;
            callback?.Invoke(Value);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && availabilityCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    availabilityCallback(Value);
                });
            }
        }
    }

    /// <summary>
    /// Property for controller emulation gyro source selection.
    /// 0 = Internal Handheld, 1 = Controller Internal
    /// </summary>
    internal class ControllerEmulationGyroSourceProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationGyroSourceProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationGyroSource, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
                if (UI.Items.Count > Value)
                {
                    UI.SelectedIndex = Value;
                }
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int newIndex = UI.SelectedIndex;
            if (newIndex >= 0 && newIndex != Value)
            {
                Logger.Info($"{Function} combo box updated to index {newIndex}.");
                SetValue(newIndex);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (UI.Items.Count > Value && UI.SelectedIndex != Value)
                    {
                        Logger.Info($"{Function} combo box selected index {Value}.");
                        UI.SelectedIndex = Value;
                    }
                });
            }
        }
    }

    /// <summary>
    /// Property for controller emulation mode.
    /// 0 = Mouse, 1 = Xbox (Stick), 2 = PS4 (Motion), 3 = PS4 (Stick)
    /// </summary>
    internal class ControllerEmulationModeProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationModeProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationMode, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
                if (UI.Items.Count > Value)
                {
                    UI.SelectedIndex = Value;
                }
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int newIndex = UI.SelectedIndex;
            if (newIndex >= 0 && newIndex != Value)
            {
                Logger.Info($"{Function} combo box updated to index {newIndex}.");
                SetValue(newIndex);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (UI.Items.Count > Value && UI.SelectedIndex != Value)
                    {
                        Logger.Info($"{Function} combo box selected index {Value}.");
                        UI.SelectedIndex = Value;
                    }
                });
            }
        }
    }
}
