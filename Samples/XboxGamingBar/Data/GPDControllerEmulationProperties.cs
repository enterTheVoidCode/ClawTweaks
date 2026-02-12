using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for GPD gyro source selection.
    /// 0 = Internal Handheld, 1 = Controller Internal
    /// </summary>
    internal class GPDGyroSourceProperty : WidgetControlProperty<int, ComboBox>
    {
        public GPDGyroSourceProperty(ComboBox inUI, Page inOwner) : base(0, Function.GPDGyroSource, inUI, inOwner)
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
    /// Property for GPD gyro simulation mode.
    /// 0 = Mouse, 1 = Xbox (Stick), 2 = PS4 (Motion), 3 = PS4 (Stick)
    /// </summary>
    internal class GPDGyroSimulateModeProperty : WidgetControlProperty<int, ComboBox>
    {
        public GPDGyroSimulateModeProperty(ComboBox inUI, Page inOwner) : base(0, Function.GPDGyroSimulateMode, inUI, inOwner)
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
