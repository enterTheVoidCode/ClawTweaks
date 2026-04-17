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

            if (perGameContext)
            {
                SavePerGamePowerSourceProfileSetting(currentGameName, enabled);
                Logger.Info($"PowerSourceProfileToggle toggled for game '{currentGameName}' to: {enabled}");
                LoadOrCreateGameProfiles();
            }
            else
            {
                Logger.Info($"PowerSourceProfileToggle toggled globally to: {enabled}");
                SavePowerSourceProfileSetting(enabled);
            }

            UpdateGlobalProfileDisplayMode();
            UpdateGameProfileCardVisibility();
            UpdateActiveProfileIndicator();
            UpdateProfileDisplay();
        }

        private void LoadPowerSourceProfileSetting()
        {
            try
            {
                if (PowerSourceProfileToggle == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                bool enabled = false;
                if (settings.Values.TryGetValue(GlobalPowerSourceProfileSettingKey, out object val) && val is bool saved)
                {
                    enabled = saved;
                }

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
