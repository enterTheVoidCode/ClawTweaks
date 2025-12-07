using Shared.Data;
using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using muxc = Microsoft.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RunningGameProperty : WidgetPropertyWithAdditionalUI<RunningGame, TextBlock, ToggleSwitch>
    {
        private TextBlock detectedGameText;
        private muxc.NavigationViewItem performanceNavItem;
        private ComboBox performanceOverlayComboBox;

        public RunningGameProperty(TextBlock inUI, ToggleSwitch inAdditionalUI, Page inOwner) : base(new RunningGame(), Function.RunningGame, inUI, inAdditionalUI, inOwner)
        {

        }

        public RunningGameProperty(TextBlock inUI, ToggleSwitch inAdditionalUI, TextBlock inDetectedGameText, Page inOwner) : base(new RunningGame(), Function.RunningGame, inUI, inAdditionalUI, inOwner)
        {
            detectedGameText = inDetectedGameText;
        }

        /// <summary>
        /// Set references for XY navigation updates
        /// </summary>
        public void SetNavigationReferences(muxc.NavigationViewItem navItem, ComboBox overlayComboBox)
        {
            performanceNavItem = navItem;
            performanceOverlayComboBox = overlayComboBox;

            // Set initial navigation state (no game detected by default)
            UpdateXYNavigation(false);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update running game value \"{Value.GameId.Name}\".");
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                    UI.Text = Value.IsValid() ? Value.GameId.Name : "No Game Detected";
                    AdditionalUI.IsEnabled = Value.IsValid();
                    if (!Value.IsValid())
                    {
                        AdditionalUI.IsOn = false;
                    }

                    // Update detected game text on Performance tab
                    if (detectedGameText != null)
                    {
                        detectedGameText.Text = Value.IsValid() ? Value.GameId.Name : "No game detected";
                    }

                    // Update XY navigation based on game detection
                    UpdateXYNavigation(Value.IsValid());
                });
            }
        }

        /// <summary>
        /// Update XY focus navigation based on whether a game is detected
        /// </summary>
        private void UpdateXYNavigation(bool gameDetected)
        {
            if (performanceNavItem == null || performanceOverlayComboBox == null) return;

            if (gameDetected)
            {
                // Game detected: Nav -> PerGameProfileToggle -> PerformanceOverlay
                performanceNavItem.XYFocusDown = AdditionalUI;
                AdditionalUI.XYFocusUp = performanceNavItem;
                AdditionalUI.XYFocusDown = performanceOverlayComboBox;
                performanceOverlayComboBox.XYFocusUp = AdditionalUI;
            }
            else
            {
                // No game: Nav -> PerformanceOverlay (skip disabled toggle)
                performanceNavItem.XYFocusDown = performanceOverlayComboBox;
                performanceOverlayComboBox.XYFocusUp = performanceNavItem;
            }
        }
    }
}
