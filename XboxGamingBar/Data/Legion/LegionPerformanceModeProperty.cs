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

        /// <summary>
        /// Flag indicating the UI is being updated from a pipe/property sync (not user interaction).
        /// When true, downstream handlers (TDPModeComboBox_SelectionChanged) should treat the
        /// change as helper-initiated and skip profile saves.
        /// </summary>
        private bool isUpdatingUI;
        public bool IsUpdatingUI => isUpdatingUI;

        /// <summary>
        /// When true, both UI updates AND internal value changes from helper sync are suppressed.
        /// Used during initial sync to prevent helper's cached mode from overwriting profile mode.
        /// </summary>
        public bool SuppressUpdates { get; set; } = false;

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

        /// <summary>
        /// Override SetValue to skip value changes when SuppressUpdates is true.
        /// This prevents the helper's cached mode from being stored during initial sync.
        /// </summary>
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            if (SuppressUpdates)
            {
                Logger.Info($"{Function} value update suppressed during initial sync (incoming value={newValue})");
                return true; // Return true to indicate "handled" without actually setting
            }
            return base.SetValue(newValue, updatedTime);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Skip UI updates during initial sync - profile mode will be applied afterward
            if (SuppressUpdates)
            {
                Logger.Info($"{Function} UI update suppressed during initial sync (value={Value})");
                return;
            }

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    WidgetSliderProperty.HelperSyncCount++;
                    isUpdatingUI = true;
                    try
                    {
                        UpdateUISelection();
                    }
                    finally
                    {
                        isUpdatingUI = false;
                        WidgetSliderProperty.HelperSyncCount--;
                    }
                });
            }
        }
    }
}
