using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        private void PowerSourceProfileToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (PowerSourceProfileToggle == null)
            {
                return;
            }

            if (isUpdatingPowerSourceProfileToggle)
            {
                UpdateGlobalProfileDisplayMode();
                UpdatePowerSourceProfileScopeText();
                return;
            }

            bool enabled = PowerSourceProfileToggle.IsOn;
            bool perGameContext = PerGameProfileToggle?.IsOn == true && HasValidGame(currentGameName);

            // One send, no scope parameter: the helper writes it to CurrentProfile, which IS the
            // per-game profile while a game runs and the global profile otherwise — the same scope
            // this toggle shows. The widget no longer stores the flag itself; it lives in the profile
            // (GameProfile.PowerSourceSplit) and comes back through the snapshot.
            SendPowerSourceSplitToHelper(enabled);
            Logger.Info($"PowerSourceProfileToggle toggled to {enabled} "
                + $"(scope: {(perGameContext ? $"game '{currentGameName}'" : "global")})");

            if (perGameContext)
            {
                LoadOrCreateGameProfiles();
            }

            UpdateGlobalProfileDisplayMode();
            UpdateGameProfileCardVisibility();
            UpdateActiveProfileIndicator();
            UpdateProfileDisplay();
        }

        /// <summary>
        /// Sends the split flag for the ACTIVE profile to the helper, which owns and persists it.
        /// </summary>
        private void SendPowerSourceSplitToHelper(bool enabled)
        {
            try
            {
                if (!App.IsConnected) return;
                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.PowerSourceSplit },
                    { "Content", enabled ? "true" : "false" },
                };
                App.PipeClient?.SendValueSet(request);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending PowerSourceSplit: {ex.Message}");
            }
        }

        private void LoadPowerSourceProfileSetting()
        {
            try
            {
                if (PowerSourceProfileToggle == null) return;

                // From the helper's store via the snapshot, resolved for whichever scope the card is
                // showing. The widget's own LocalSettings key is gone with the flag moving into the
                // profile — reading it here would be the second answer this rebuild removes.
                bool enabled = (PerGameProfileToggle?.IsOn == true && HasValidGame(currentGameName))
                    ? GetPerGamePowerSourceProfileEnabled(currentGameName)
                    : GetGlobalPowerSourceProfileEnabled();

                isUpdatingPowerSourceProfileToggle = true;
                try
                {
                    PowerSourceProfileToggle.IsOn = enabled;
                }
                finally
                {
                    isUpdatingPowerSourceProfileToggle = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading PowerSourceProfile setting: {ex.Message}");
            }
        }

        private void SavePowerSourceProfileSetting(bool enabled)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[GlobalPowerSourceProfileSettingKey] = enabled;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving PowerSourceProfile setting: {ex.Message}");
            }
        }

    }
}
