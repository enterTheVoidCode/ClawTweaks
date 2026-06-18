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

        /// <summary>
        /// Optional confirmation gate (oldValue, newValue) → proceed? Set by the owner for selectors
        /// that need a user prompt before committing (e.g. the VIIPER device-type warning for non-Xbox
        /// types). When it returns false the combo is reverted to the current Value WITHOUT pushing a
        /// change — so a cancelled choice never triggers a real device hot-swap. Only fires for genuine
        /// user selections (helper syncs update Value first, so tagString == Value and we early-out).
        /// </summary>
        public Func<string, string, System.Threading.Tasks.Task<bool>> ConfirmChangeAsync;

        private bool _reverting;

        private async void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_reverting) return; // re-entry from our own revert below
            var selectedItem = UI.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;
            var tagString = selectedItem.Tag as string;
            if (tagString == null || tagString == Value) return;

            if (ConfirmChangeAsync != null)
            {
                bool proceed;
                try { proceed = await ConfirmChangeAsync(Value, tagString); }
                catch (Exception ex) { Logger.Warn($"{Function} confirm gate threw: {ex.Message}"); proceed = true; }
                if (!proceed)
                {
                    // User cancelled — snap the combo back to the current Value without pushing.
                    _reverting = true;
                    try { SyncSelectedIndexFromValue(); }
                    finally { _reverting = false; }
                    return;
                }
            }

            Logger.Info($"{Function} combo updated to {tagString}.");
            SetValue(tagString);
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
