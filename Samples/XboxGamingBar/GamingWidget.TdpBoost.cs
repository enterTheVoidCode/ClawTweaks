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
        private void TDPBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (TDPBoostToggle == null) return;
            if (isApplyingHelperUpdate) return;
            // Skip during mode changes - don't save forced-off state
            if (isUpdatingTDPMode) return;

            Logger.Info($"TDP Boost toggled to: {TDPBoostToggle.IsOn}");

            // Send to helper
            tdpBoostEnabled?.SetValue(TDPBoostToggle.IsOn);

            // Save to local settings for persistence across widget restarts
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostEnabled"] = TDPBoostToggle.IsOn;

            // When enabling boost, also send current SPPT/FPPT values to ensure helper has them
            if (TDPBoostToggle.IsOn)
            {
                int spptBoost = (int)(TDPBoostSPPTSlider?.Value ?? 1);
                int fpptBoost = (int)(TDPBoostFPPTSlider?.Value ?? 3);
                tdpBoostSPPT?.SetValue(spptBoost);
                tdpBoostFPPT?.SetValue(fpptBoost);
                Logger.Info($"TDP Boost enabled - sent SPPT={spptBoost}W, FPPT={fpptBoost}W to helper");
            }

            // Save to profile if not loading
            if (!isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        private void TDPBoostSPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostSPPTSlider == null) return;

            int spptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost SPPT changed to: {spptBoost}W");

            if (TDPBoostSPPTValue != null)
            {
                TDPBoostSPPTValue.Text = $"{spptBoost}W";
            }

            // Send to helper
            tdpBoostSPPT?.SetValue(spptBoost);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostSPPT"] = spptBoost;
        }

        private void TDPBoostFPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostFPPTSlider == null) return;

            int fpptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost FPPT changed to: {fpptBoost}W");

            if (TDPBoostFPPTValue != null)
            {
                TDPBoostFPPTValue.Text = $"{fpptBoost}W";
            }

            // Send to helper
            tdpBoostFPPT?.SetValue(fpptBoost);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostFPPT"] = fpptBoost;
        }

        private void LoadTDPBoostSettings()
        {
            isLoadingTDPBoostSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load TDP Boost enabled state (default OFF)
                if (settings.Values.TryGetValue("TDPBoostEnabled", out object enabledObj) && enabledObj is bool enabled)
                {
                    if (TDPBoostToggle != null)
                    {
                        TDPBoostToggle.IsOn = enabled;
                    }
                    tdpBoostEnabled?.SetValue(enabled);
                    Logger.Info($"TDP Boost enabled state loaded from settings: {enabled}");
                }

                // Load SPPT boost (default 1W)
                int spptBoost = 1; // Default
                if (settings.Values.TryGetValue("TDPBoostSPPT", out object spptObj) && spptObj != null)
                {
                    try
                    {
                        spptBoost = Convert.ToInt32(spptObj);
                    }
                    catch
                    {
                        spptBoost = 1;
                    }
                }
                if (TDPBoostSPPTSlider != null)
                {
                    TDPBoostSPPTSlider.Value = spptBoost;
                }
                if (TDPBoostSPPTValue != null)
                {
                    TDPBoostSPPTValue.Text = $"{spptBoost}W";
                }
                tdpBoostSPPT?.SetValue(spptBoost);
                // Ensure value is saved (in case it was missing or converted)
                settings.Values["TDPBoostSPPT"] = spptBoost;

                // Load FPPT boost (default 3W)
                int fpptBoost = 3; // Default
                if (settings.Values.TryGetValue("TDPBoostFPPT", out object fpptObj) && fpptObj != null)
                {
                    try
                    {
                        fpptBoost = Convert.ToInt32(fpptObj);
                    }
                    catch
                    {
                        fpptBoost = 3;
                    }
                }
                if (TDPBoostFPPTSlider != null)
                {
                    TDPBoostFPPTSlider.Value = fpptBoost;
                }
                if (TDPBoostFPPTValue != null)
                {
                    TDPBoostFPPTValue.Text = $"{fpptBoost}W";
                }
                tdpBoostFPPT?.SetValue(fpptBoost);
                // Ensure value is saved (in case it was missing or converted)
                settings.Values["TDPBoostFPPT"] = fpptBoost;

                Logger.Info($"TDP Boost settings loaded - SPPT: {spptBoost}W, FPPT: {fpptBoost}W");
            }
            finally
            {
                isLoadingTDPBoostSettings = false;
            }
        }

        private void TDPBoostEnabled_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // NOTE: This callback is triggered when helper syncs TDPBoostEnabled.
            // We do NOT update the toggle from this callback because:
            // 1. The widget (LocalSettings) is the source of truth for this setting
            // 2. The helper doesn't persist TDPBoostEnabled, so it always sends False on fresh start
            // 3. Profile loading explicitly sets the toggle in LoadProfileSettings()
            //
            // If boost is enabled, we just need to ensure SPPT/FPPT values are sent to helper.
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (TDPBoostToggle == null || tdpBoostEnabled == null) return;

                // Only send SPPT/FPPT to helper if boost is currently enabled in the UI
                // (regardless of what the helper sent us)
                if (TDPBoostToggle.IsOn)
                {
                    int spptBoost = (int)(TDPBoostSPPTSlider?.Value ?? 1);
                    int fpptBoost = (int)(TDPBoostFPPTSlider?.Value ?? 3);
                    tdpBoostSPPT?.SetValue(spptBoost);
                    tdpBoostFPPT?.SetValue(fpptBoost);
                    Logger.Debug($"TDP Boost PropertyChanged - ensuring SPPT={spptBoost}W, FPPT={fpptBoost}W sent to helper");
                }
            });
        }

    }
}
