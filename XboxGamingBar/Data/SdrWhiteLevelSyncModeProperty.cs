using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class SdrWhiteLevelSyncModeProperty : WidgetControlProperty<int, ComboBox>
    {
        private bool isUpdatingUI;

        public SdrWhiteLevelSyncModeProperty(ComboBox inUI, Page inOwner)
            : base((int)SdrWhiteLevelSyncMode.Off, Function.SdrWhiteLevelSyncMode, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
                if (UI.SelectedIndex != Value) UI.SelectedIndex = Value;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingUI) return;
            int idx = UI.SelectedIndex;
            if (idx < 0 || idx == Value) return;
            Logger.Info($"{Function} ComboBox -> {(SdrWhiteLevelSyncMode)idx}");
            SetValue(idx);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (UI.SelectedIndex == Value) return;
                    isUpdatingUI = true;
                    try
                    {
                        UI.SelectedIndex = Value;
                    }
                    finally
                    {
                        isUpdatingUI = false;
                    }
                });
            }
        }
    }
}
