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
                if (UI.Items.Count > Value)
                {
                    UI.SelectedIndex = Value;
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
