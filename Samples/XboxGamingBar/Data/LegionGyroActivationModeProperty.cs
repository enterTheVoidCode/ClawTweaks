using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gyro Activation Mode (0=Hold, 1=Toggle)
    /// </summary>
    internal class LegionGyroActivationModeProperty : WidgetControlProperty<int, ComboBox>
    {
        public LegionGyroActivationModeProperty(ComboBox inUI, Page inOwner) : base(0, Function.LegionGyroActivationMode, inUI, inOwner)
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
