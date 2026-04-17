using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Shared ComboBox-backed property for VIIPER string selectors (device type,
    /// input source, gyro source, Steam sub-device). The ComboBoxItem's Tag string
    /// is the canonical value sent to the helper.
    /// </summary>
    internal class ViiperStringComboProperty : WidgetControlProperty<string, ComboBox>
    {
        public ViiperStringComboProperty(string initialValue, Function inFunction, ComboBox inUI, Page inOwner)
            : base(initialValue, inFunction, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = UI.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                var tagString = selectedItem.Tag as string;
                if (tagString != null && tagString != Value)
                {
                    Logger.Info($"{Function} combo updated to {tagString}.");
                    SetValue(tagString);
                }
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Find the item with matching tag.
                    string targetTag = Value ?? string.Empty;
                    for (int i = 0; i < UI.Items.Count; i++)
                    {
                        var item = UI.Items[i] as ComboBoxItem;
                        if (item != null && (item.Tag as string) == targetTag)
                        {
                            if (UI.SelectedIndex != i)
                            {
                                UI.SelectedIndex = i;
                            }
                            break;
                        }
                    }
                });
            }
        }
    }
}
