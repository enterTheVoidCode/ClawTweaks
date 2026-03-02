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

    internal class ControllerEmulationEnabledProperty : WidgetToggleProperty
    {
        public ControllerEmulationEnabledProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationEnabled, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationHideStockControllerProperty : WidgetToggleProperty
    {
        public ControllerEmulationHideStockControllerProperty(ToggleSwitch inUI, Page inOwner)
            : base(true, Function.ControllerEmulationHideStockController, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationImprovedInputProperty : WidgetToggleProperty
    {
        public ControllerEmulationImprovedInputProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationImprovedInput, inUI, inOwner)
        {
        }
    }

    /// <summary>
    /// Property for suppression target selection.
    /// 0 = Auto, 1 = Native handheld, 2 = Xbox 360 bridge, 3 = Native + Xbox 360
    /// </summary>
    internal class ControllerEmulationHideTargetProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationHideTargetProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationHideTarget, inUI, inOwner)
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

    /// <summary>
    /// Property for rumble response profile.
    /// 0 = Balanced, 1 = Sharp, 2 = Soft, 3 = Impact, 4 = Boosted
    /// </summary>
    internal class ControllerEmulationRumbleProfileProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationRumbleProfileProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationRumbleProfile, inUI, inOwner)
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
    /// Property for gyro activation behavior.
    /// 0 = Always On, 1 = Hold, 2 = Toggle
    /// </summary>
    internal class ControllerEmulationGyroActivationModeProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationGyroActivationModeProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationGyroActivationMode, inUI, inOwner)
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
    /// Property for gyro activation button binding.
    /// 0 = None, 1 = Right Trigger, 2 = Left Trigger, ...
    /// </summary>
    internal class ControllerEmulationGyroActivationButtonProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationGyroActivationButtonProperty(ComboBox inUI, Page inOwner)
            : base(1, Function.ControllerEmulationGyroActivationButton, inUI, inOwner)
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
    /// Property for DS4 motion orientation.
    /// 0 = Parallel, 1 = Orthogonal
    /// </summary>
    internal class ControllerEmulationDs4OrientationProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationDs4OrientationProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationDs4Orientation, inUI, inOwner)
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

    internal class ControllerEmulationPs4TouchpadEnabledProperty : WidgetToggleProperty
    {
        public ControllerEmulationPs4TouchpadEnabledProperty(ToggleSwitch inUI, Page inOwner)
            : base(true, Function.ControllerEmulationPs4TouchpadEnabled, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationLedForwardingEnabledProperty : WidgetToggleProperty
    {
        public ControllerEmulationLedForwardingEnabledProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationLedForwardingEnabled, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationMouseSensitivityProperty : WidgetSliderProperty
    {
        public ControllerEmulationMouseSensitivityProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationMouseSensitivity, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationMouseThresholdProperty : WidgetSliderProperty
    {
        public ControllerEmulationMouseThresholdProperty(Slider inUI, Page inOwner)
            : base(2, Function.ControllerEmulationMouseThreshold, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationMouseAxisProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationMouseAxisProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationMouseAxis, inUI, inOwner)
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

    internal class ControllerEmulationMouseInvertXProperty : WidgetToggleProperty
    {
        public ControllerEmulationMouseInvertXProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationMouseInvertX, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationMouseInvertYProperty : WidgetToggleProperty
    {
        public ControllerEmulationMouseInvertYProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationMouseInvertY, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationMouseGainXProperty : WidgetSliderProperty
    {
        public ControllerEmulationMouseGainXProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationMouseGainX, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationMouseGainYProperty : WidgetSliderProperty
    {
        public ControllerEmulationMouseGainYProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationMouseGainY, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickSensitivityProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickSensitivityProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationStickSensitivity, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickThresholdProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickThresholdProperty(Slider inUI, Page inOwner)
            : base(2, Function.ControllerEmulationStickThreshold, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickAxisProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationStickAxisProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationStickAxis, inUI, inOwner)
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

    internal class ControllerEmulationStickInvertXProperty : WidgetToggleProperty
    {
        public ControllerEmulationStickInvertXProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationStickInvertX, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickInvertYProperty : WidgetToggleProperty
    {
        public ControllerEmulationStickInvertYProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationStickInvertY, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickGainXProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickGainXProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationStickGainX, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickGainYProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickGainYProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationStickGainY, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickSelectProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationStickSelectProperty(ComboBox inUI, Page inOwner)
            : base(1, Function.ControllerEmulationStickSelect, inUI, inOwner)
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

    internal class ControllerEmulationStickExcessMoveProperty : WidgetToggleProperty
    {
        public ControllerEmulationStickExcessMoveProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationStickExcessMove, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickRangeProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickRangeProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationStickRange, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationStickOnlyJoystickDataProperty : WidgetToggleProperty
    {
        public ControllerEmulationStickOnlyJoystickDataProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.ControllerEmulationStickOnlyJoystickData, inUI, inOwner)
        {
        }
    }

    internal class ControllerEmulationVirtualABXYLayoutProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationVirtualABXYLayoutProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationVirtualABXYLayout, inUI, inOwner)
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

    // Stick v2 slider properties
    internal class ControllerEmulationStickMinGyroSpeedProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickMinGyroSpeedProperty(Slider inUI, Page inOwner)
            : base(0, Function.ControllerEmulationStickMinGyroSpeed, inUI, inOwner) { }
    }

    internal class ControllerEmulationStickMaxGyroSpeedProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickMaxGyroSpeedProperty(Slider inUI, Page inOwner)
            : base(220, Function.ControllerEmulationStickMaxGyroSpeed, inUI, inOwner) { }
    }

    internal class ControllerEmulationStickMinOutputProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickMinOutputProperty(Slider inUI, Page inOwner)
            : base(0, Function.ControllerEmulationStickMinOutput, inUI, inOwner) { }
    }

    internal class ControllerEmulationStickMaxOutputProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickMaxOutputProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationStickMaxOutput, inUI, inOwner) { }
    }

    internal class ControllerEmulationStickPowerCurveProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickPowerCurveProperty(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationStickPowerCurve, inUI, inOwner) { }
    }

    internal class ControllerEmulationStickSensitivityV2Property : WidgetSliderProperty
    {
        public ControllerEmulationStickSensitivityV2Property(Slider inUI, Page inOwner)
            : base(100, Function.ControllerEmulationStickSensitivityV2, inUI, inOwner) { }
    }

    internal class ControllerEmulationStickDeadzoneProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickDeadzoneProperty(Slider inUI, Page inOwner)
            : base(2, Function.ControllerEmulationStickDeadzone, inUI, inOwner) { }
    }

    internal class ControllerEmulationStickPrecisionSpeedProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickPrecisionSpeedProperty(Slider inUI, Page inOwner)
            : base(0, Function.ControllerEmulationStickPrecisionSpeed, inUI, inOwner) { }
    }

    internal class ControllerEmulationStickOutputMixProperty : WidgetSliderProperty
    {
        public ControllerEmulationStickOutputMixProperty(Slider inUI, Page inOwner)
            : base(0, Function.ControllerEmulationStickOutputMix, inUI, inOwner) { }
    }

    // Stick v2 combo box properties
    internal class ControllerEmulationStickOrientationV2Property : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationStickOrientationV2Property(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationStickOrientationV2, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
                if (UI.Items.Count > Value) UI.SelectedIndex = Value;
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
                        UI.SelectedIndex = Value;
                });
            }
        }
    }

    internal class ControllerEmulationStickConversionProperty : WidgetControlProperty<int, ComboBox>
    {
        public ControllerEmulationStickConversionProperty(ComboBox inUI, Page inOwner)
            : base(0, Function.ControllerEmulationStickConversion, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
                if (UI.Items.Count > Value) UI.SelectedIndex = Value;
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
                        UI.SelectedIndex = Value;
                });
            }
        }
    }
}
