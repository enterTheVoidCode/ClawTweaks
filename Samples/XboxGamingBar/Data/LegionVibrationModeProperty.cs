using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion vibration mode preset (FPS=1, Racing=2, AVG=3, SPG=4, RPG=5)
    /// UI index is 0-based, mode value is 1-based
    /// </summary>
    internal class LegionVibrationModeProperty : WidgetControlProperty<int, ComboBox>
    {
        public LegionVibrationModeProperty(ComboBox inUI, Page inOwner) : base(1, Function.LegionVibrationMode, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
                // Value is 1-based, UI index is 0-based
                if (UI.Items.Count > 0 && Value >= 1)
                {
                    UI.SelectedIndex = Value - 1;
                }
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Convert 0-based index to 1-based mode value
            int newMode = UI.SelectedIndex + 1;
            if (newMode >= 1 && newMode != Value)
            {
                Logger.Info($"{Function} combo box updated to mode {newMode}.");
                SetValue(newMode);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Value is 1-based, UI index is 0-based
                    int targetIndex = Value - 1;
                    if (targetIndex >= 0 && UI.Items.Count > targetIndex && UI.SelectedIndex != targetIndex)
                    {
                        Logger.Info($"{Function} combo box selected index {targetIndex} (mode {Value}).");
                        UI.SelectedIndex = targetIndex;
                    }
                });
            }
        }
    }
}
