using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingScalingTypeProperty : WidgetControlProperty<string, ComboBox>
    {
        public LosslessScalingScalingTypeProperty(ComboBox inUI, Page inOwner) : base("Off", Function.LosslessScalingScalingType, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string stringValue && stringValue != Value)
            {
                Logger.Info($"{Function} combo box updated to {stringValue}.");
                SetValue(stringValue);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    for (var i = 0; i < UI.Items.Count; i++)
                    {
                        if (UI.Items[i] is string stringValue && stringValue == Value)
                        {
                            Logger.Info($"{Function} combo box selected index {i}.");
                            UI.SelectedIndex = i;
                            break;
                        }
                    }
                });
            }
        }
    }
}
