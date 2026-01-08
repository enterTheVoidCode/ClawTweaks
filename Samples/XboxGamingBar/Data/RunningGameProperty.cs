using Shared.Data;
using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RunningGameProperty : WidgetPropertyWithAdditionalUI<RunningGame, TextBlock, ToggleSwitch>
    {
        private TextBlock detectedGameText;
        private Action onGameDetectionChanged;

        public RunningGameProperty(TextBlock inUI, ToggleSwitch inAdditionalUI, Page inOwner) : base(new RunningGame(), Function.RunningGame, inUI, inAdditionalUI, inOwner)
        {

        }

        public RunningGameProperty(TextBlock inUI, ToggleSwitch inAdditionalUI, TextBlock inDetectedGameText, Page inOwner) : base(new RunningGame(), Function.RunningGame, inUI, inAdditionalUI, inOwner)
        {
            detectedGameText = inDetectedGameText;
        }

        /// <summary>
        /// Set callback for when game detection changes (for XY navigation updates)
        /// </summary>
        public void SetGameDetectionCallback(Action callback)
        {
            onGameDetectionChanged = callback;
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

                    // Notify callback for XY navigation update
                    onGameDetectionChanged?.Invoke();
                });
            }
        }
    }
}
