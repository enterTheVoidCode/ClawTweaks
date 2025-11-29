using Shared.Enums;
using System;
using System.Collections.Generic;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class ResolutionsProperty : WidgetControlProperty<List<string>, ComboBox>
    {
        public ResolutionsProperty(ComboBox inUI, Page inOwner) : base(new List<string>() { "1920x1080" }, Function.Resolutions, inUI, inOwner)
        {
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
                });
            }
        }

        protected override void SetControlEnabled(bool isEnabled)
        {
            // Resolution combo box should be enabled/disabled by ResolutionProperty, not this.
        }
    }
}
