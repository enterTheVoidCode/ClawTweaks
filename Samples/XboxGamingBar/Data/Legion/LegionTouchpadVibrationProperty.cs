using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Touchpad Vibration level (Off=1, Low=2, Medium=3, High=4)
    /// UI index is 0-based, mode value is 1-based
    /// Note: This is a GLOBAL setting, not per-game profile
    /// </summary>
    internal class LegionTouchpadVibrationProperty : WidgetControlProperty<int, ComboBox>
    {
        public LegionTouchpadVibrationProperty(ComboBox inUI, Page inOwner) : base(3, Function.LegionTouchpadVibration, inUI, inOwner)
        {
            // Default to Medium (value=3, index=2)
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
            int newLevel = UI.SelectedIndex + 1;
            if (newLevel >= 1 && newLevel != Value)
            {
                Logger.Info($"{Function} combo box updated to level {newLevel}.");
                SetValue(newLevel);
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
                        Logger.Info($"{Function} combo box selected index {targetIndex} (level {Value}).");
                        UI.SelectedIndex = targetIndex;
                    }
                });
            }
        }
    }
}
