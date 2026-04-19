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
                // Set selected index to match initial value BEFORE wiring SelectionChanged, so the
                // ComboBox renders the right item from the start. NotifyPropertyChanged only fires
                // when Value actually changes — if the helper syncs the same value as our initial,
                // the UI would otherwise never get updated and the combo appears empty.
                SyncSelectedIndexFromValue();
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void SyncSelectedIndexFromValue()
        {
            if (UI == null) return;
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
                    return;
                }
            }
            // Value doesn't match any item — fall back to first item so the combo isn't empty.
            if (UI.Items.Count > 0 && UI.SelectedIndex < 0)
            {
                UI.SelectedIndex = 0;
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
