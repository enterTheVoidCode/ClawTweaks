using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Joystick as Mouse Mode (0=Disabled, 1=Left Stick, 2=Right Stick)
    /// Also controls visibility of the sensitivity grid based on selection.
    /// </summary>
    internal class LegionJoystickAsMouseModeProperty : WidgetControlProperty<int, ComboBox>
    {
        private readonly Grid sensitivityGrid;

        public LegionJoystickAsMouseModeProperty(ComboBox inUI, Grid inSensitivityGrid, Page inOwner) : base(0, Function.LegionJoystickAsMouseMode, inUI, inOwner)
        {
            sensitivityGrid = inSensitivityGrid;

            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;

                // Always ensure initial selection is set - handles both fresh install and
                // cases where helper/widget have same value (no sync message sent)
                SetInitialSelection();
            }
        }

        private void SetInitialSelection()
        {
            // If items are loaded, set selection immediately
            if (UI.Items.Count > 0)
            {
                int index = Math.Min(Value, UI.Items.Count - 1);
                if (index >= 0)
                {
                    UI.SelectedIndex = index;
                    Logger.Info($"{Function} initialized to index {index}");
                }
                UpdateSensitivityGridVisibility();
            }
            else
            {
                // Items not loaded yet, wait for Loaded event
                UI.Loaded += OnComboBoxLoaded;
            }
        }

        private void OnComboBoxLoaded(object sender, RoutedEventArgs e)
        {
            UI.Loaded -= OnComboBoxLoaded; // Unsubscribe to avoid multiple calls
            if (UI.Items.Count > 0)
            {
                int index = Math.Min(Value, UI.Items.Count - 1);
                if (index >= 0 && UI.SelectedIndex != index)
                {
                    UI.SelectedIndex = index;
                    Logger.Info($"{Function} initialized to index {index} (after load)");
                }
                UpdateSensitivityGridVisibility();
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int newIndex = UI.SelectedIndex;
            if (newIndex >= 0 && newIndex != Value)
            {
                Logger.Info($"{Function} combo box updated to index {newIndex}.");
                SetValue(newIndex);
                UpdateSensitivityGridVisibility();
            }
        }

        private void UpdateSensitivityGridVisibility()
        {
            if (sensitivityGrid != null)
            {
                sensitivityGrid.Visibility = Value > 0 ? Visibility.Visible : Visibility.Collapsed;
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
                    UpdateSensitivityGridVisibility();
                });
            }
        }
    }
}
