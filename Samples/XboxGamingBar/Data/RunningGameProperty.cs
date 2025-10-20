using Shared.Data;
using Shared.Enums;
using System;
using System.Runtime.CompilerServices;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RunningGameProperty : WidgetPropertyWithAdditionalUI<RunningGame, TextBlock, ToggleSwitch>
    {
        public RunningGameProperty(TextBlock inUI, ToggleSwitch inAdditionalUI, Page inOwner) : base(new RunningGame(), Function.CurrentGame, inUI, inAdditionalUI, inOwner)
        {

        }

        protected override async void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
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
                });
            }
        }
    }
}
