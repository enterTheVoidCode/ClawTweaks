using Shared.Enums;
using System;
using System.Collections.Generic;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class ResolutionsProperty : WidgetControlProperty<List<string>, ComboBox>
    {
        // Reference to the ResolutionProperty to restore selection after list updates
        private ResolutionProperty resolutionProperty;

        public ResolutionsProperty(ComboBox inUI, Page inOwner) : base(new List<string>() { "1920x1080" }, Function.Resolutions, inUI, inOwner)
        {
        }

        /// <summary>
        /// Sets the ResolutionProperty reference so we can restore selection after list updates.
        /// </summary>
        public void SetResolutionProperty(ResolutionProperty property)
        {
            resolutionProperty = property;
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} combo box value {Value?.Count ?? 0} items.");
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UI.Items.Clear();
                    if (Value != null)
                    {
                        foreach (var value in Value)
                        {
                            UI.Items.Add(value);
                        }
                    }

                    // After updating items, restore the current resolution selection
                    // This handles the case where the value doesn't change but items do (e.g., dock/undock)
                    if (resolutionProperty != null)
                    {
                        var currentRes = resolutionProperty.Value;
                        for (int i = 0; i < UI.Items.Count; i++)
                        {
                            if (UI.Items[i] is string res && res == currentRes)
                            {
                                Logger.Info($"Restoring {Function} selection to index {i} ({currentRes}) after list update.");
                                UI.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                });
            }
        }

        protected override void SetControlEnabled(bool isEnabled)
        {
            // Resolution combo box should be enabled/disabled by ResolutionProperty, not this.
        }
    }
}
