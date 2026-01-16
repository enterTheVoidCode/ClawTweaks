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

        /// <summary>
        /// Override SetValue to reject empty/invalid RunningGame updates during BatchGet sync only.
        /// When BatchGet returns empty "{}", we preserve the current valid game.
        /// But when the helper sends a game-close notification (SuppressRemoteSync=false), we must accept it.
        /// </summary>
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            // Only reject invalid updates during BatchGet sync (SuppressRemoteSync=true)
            // When game actually closes, the helper sends an update with SuppressRemoteSync=false
            // and we must accept it to properly clear the game state
            if (SuppressRemoteSync && Value.IsValid())
            {
                // Check if the incoming RunningGame is valid before accepting it during batch sync
                if (newValue is RunningGame runningGame)
                {
                    if (!runningGame.IsValid())
                    {
                        Logger.Info($"Rejecting empty RunningGame update during batch sync - preserving current: {Value.GameId.Name}");
                        return false;
                    }
                }
                // Also check string values (from BatchGet) - "{}" or empty XML means no valid game
                else if (newValue is string xmlString)
                {
                    // Reject empty JSON objects or empty/minimal XML that won't deserialize to a valid game
                    if (string.IsNullOrWhiteSpace(xmlString) ||
                        xmlString == "{}" ||
                        xmlString == "null" ||
                        !xmlString.Contains("<Name>") ||
                        !xmlString.Contains("<ProcessId>"))
                    {
                        Logger.Info($"Rejecting invalid RunningGame XML during batch sync - preserving current: {Value.GameId.Name}");
                        return false;
                    }
                }
            }

            return base.SetValue(newValue, updatedTime);
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
