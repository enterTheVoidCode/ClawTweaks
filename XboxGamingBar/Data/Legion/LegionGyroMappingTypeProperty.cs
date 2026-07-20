using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Claw gyro engine / Legion mapping type
    /// (0=Adaptive, 1=Direct, 2=Normalized Circular).
    /// </summary>
    internal class LegionGyroMappingTypeProperty : WidgetControlProperty<int, ComboBox>
    {
        public LegionGyroMappingTypeProperty(ComboBox inUI, Page inOwner) : base(0, Function.LegionGyroMappingType, inUI, inOwner)
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
            // Skip while the UI is being updated programmatically (helper sync or profile load) —
            // otherwise loading a profile pushes this value to the helper and fights its value.
            if (WidgetSliderProperty.HelperSyncCount > 0) return;
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
