using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class ResolutionProperty : WidgetControlProperty<string, ComboBox>
    {
        public ResolutionProperty(ComboBox inUI, Page inOwner) : base("1920x1080", Function.Resolution, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string strValue && strValue != Value)
            {
                Logger.Info($"{Function} combo box updated to {strValue}.");
                SetValue(strValue);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} combo box value to {Value}.");
                await SelectValueInComboBox();
            }
        }

        private async System.Threading.Tasks.Task SelectValueInComboBox(int retryCount = 0)
        {
            await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                bool found = false;
                for (var i = 0; i < UI.Items.Count; i++)
                {
                    if (UI.Items[i] is string strValue && strValue == Value)
                    {
                        Logger.Info($"{Function} combo box selected index {i} ({Value}).");
                        UI.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
                if (!found && retryCount < 3)
                {
                    // ComboBox items may not be populated yet (race condition with ResolutionsProperty)
                    // Retry after a short delay
                    Logger.Info($"{Function} value {Value} not found in ComboBox items (count={UI.Items.Count}), retry {retryCount + 1}/3...");
                    await System.Threading.Tasks.Task.Delay(100);
                    await SelectValueInComboBox(retryCount + 1);
                }
                else if (!found)
                {
                    Logger.Warn($"{Function} value {Value} not found in ComboBox items after retries.");
                }
            });
        }
    }
}
