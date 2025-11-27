using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion performance mode (Quiet=1, Balanced=2, Performance=3, Custom=255)
    /// Uses ComboBox for selection
    /// </summary>
    internal class LegionPerformanceModeProperty : WidgetControlProperty<int, ComboBox>
    {
        // Mode values: Quiet=1, Balanced=2, Performance=3, Custom=255
        private static readonly int[] PERFORMANCE_MODE_VALUES = { 1, 2, 3, 255 };
        private Action<bool> _customTDPVisibilityCallback;

        public LegionPerformanceModeProperty(ComboBox inUI, Page inOwner) : base(2, Function.LegionPerformanceMode, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
                // Initialize selection
                UpdateUISelection();
            }
        }

        /// <summary>
        /// Sets a callback to show/hide custom TDP controls
        /// </summary>
        public void SetCustomTDPVisibilityCallback(Action<bool> callback)
        {
            _customTDPVisibilityCallback = callback;
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int selectedIndex = UI.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < PERFORMANCE_MODE_VALUES.Length)
            {
                int newMode = PERFORMANCE_MODE_VALUES[selectedIndex];
                if (newMode != Value)
                {
                    Logger.Info($"{Function} ComboBox updated to mode {newMode} (index {selectedIndex}).");
                    SetValue(newMode);
                }
                // Show/hide custom TDP controls
                _customTDPVisibilityCallback?.Invoke(newMode == 255);
            }
        }

        private void UpdateUISelection()
        {
            int index = Array.IndexOf(PERFORMANCE_MODE_VALUES, Value);
            if (index >= 0 && UI.SelectedIndex != index)
            {
                UI.SelectedIndex = index;
            }
            // Update custom TDP visibility
            _customTDPVisibilityCallback?.Invoke(Value == 255);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UpdateUISelection();
                });
            }
        }
    }
}
