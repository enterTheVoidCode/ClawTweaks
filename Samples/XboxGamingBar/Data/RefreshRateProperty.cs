using System;
using Shared.Constants;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RefreshRateProperty : WidgetControlProperty<int, ComboBox>
    {
        public RefreshRateProperty(ComboBox inUI, Page inOwner) : base(SystemConstants.DEFAULT_REFRESH_RATE, Function.RefreshRate, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is int intValue && intValue != Value)
            {
                Logger.Info($"{Function} combo box updated to {intValue}.");
                SetValue(intValue);
            }
            else
            {
                Logger.Info($"{Function} combo box changed to {(e.AddedItems.Count > 0 ? e.AddedItems[0] : "none")}.");
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} combo box value to {Value}Hz.");
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
                    if (UI.Items[i] is int intValue && intValue == Value)
                    {
                        Logger.Info($"{Function} combo box selected index {i} ({Value}Hz).");
                        UI.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
                if (!found && retryCount < 3)
                {
                    // ComboBox items may not be populated yet (race condition with RefreshRatesProperty)
                    // Retry after a short delay
                    Logger.Info($"{Function} value {Value}Hz not found in ComboBox items (count={UI.Items.Count}), retry {retryCount + 1}/3...");
                    await System.Threading.Tasks.Task.Delay(100);
                    await SelectValueInComboBox(retryCount + 1);
                }
                else if (!found)
                {
                    Logger.Warn($"{Function} value {Value}Hz not found in ComboBox items after retries.");
                }
            });
        }
    }
}
