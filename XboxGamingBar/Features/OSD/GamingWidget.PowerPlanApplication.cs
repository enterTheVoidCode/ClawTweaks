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

        private void ApplyPowerPlan(Guid planGuid)
        {
            if (planGuid == Guid.Empty) return;

            // Send message to helper to apply the power plan
            // Format: "PowerPlan:GUID"
            try
            {
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("PowerPlan", planGuid.ToString());
                _ = SendHelperMessageAsync(message);
                Logger.Info($"Sent power plan change request: {planGuid}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying power plan: {ex.Message}");
            }
        }

        private async Task SendHelperMessageAsync(Windows.Foundation.Collections.ValueSet message)
        {
            if (App.IsConnected)
            {
                try
                {
                    await App.SendMessageAsync(message);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error sending message to helper: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Send a keyboard shortcut via the helper process.
        /// This is required because UWP apps cannot use SendInput directly due to sandboxing.
        /// </summary>
        private async Task SendKeyboardShortcutViaHelper(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                Logger.Warn("Empty shortcut string provided to SendKeyboardShortcutViaHelper");
                return;
            }

            try
            {
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("SendKeyboardShortcut", shortcut);
                await SendHelperMessageAsync(message);
                Logger.Info($"Sent keyboard shortcut request to helper: {shortcut}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending keyboard shortcut via helper: {ex.Message}");
            }
        }

        /// <summary>
        /// Request the helper to refresh display settings (resolution, refresh rate, HDR).
        /// Called when a game closes to ensure the resolution tile shows the correct value.
        /// </summary>
        private async Task RequestDisplaySettingsRefreshAsync()
        {
            try
            {
                Logger.Info("Requesting display settings refresh from helper");
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("RefreshDisplaySettings", true);
                await SendHelperMessageAsync(message);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error requesting display settings refresh: {ex.Message}");
            }
        }

        /// <summary>
        /// Send a custom shortcut by first closing Game Bar (if in widget mode), then sending the shortcut.
        /// Sequence: Win+G (close Game Bar) → Custom shortcut
        /// </summary>
        private async Task SendCustomShortcutAsync(string shortcut, string tileName)
        {
            try
            {
                Logger.Info($"Custom shortcut tile clicked: {tileName} -> {shortcut}");

                // Only close Game Bar if we're running as a widget
                if (widget != null)
                {
                    // First close Game Bar with Win+G
                    await SendKeyboardShortcutViaHelper("Win+G");
                    Logger.Debug("Win+G sent to close Game Bar");

                    // Wait for Game Bar to close
                    await Task.Delay(150);
                }

                // Now send the actual shortcut
                await SendKeyboardShortcutViaHelper(shortcut);
                Logger.Info($"Custom shortcut sent: {shortcut}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending custom shortcut '{shortcut}': {ex.Message}");
            }
        }

        private void SavePowerPlanSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["PowerPlan_AC"] = acPowerPlanGuid.ToString();
                settings.Values["PowerPlan_DC"] = dcPowerPlanGuid.ToString();
                settings.Values["PowerPlan_AutoSwitch"] = powerPlanAutoSwitch;
                Logger.Info("Power plan settings saved");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving power plan settings: {ex.Message}");
            }
        }

        private void LoadPowerPlanSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("PowerPlan_AC", out object acVal) && acVal is string acStr)
                {
                    if (Guid.TryParse(acStr, out Guid acGuid))
                    {
                        acPowerPlanGuid = acGuid;
                    }
                }

                if (settings.Values.TryGetValue("PowerPlan_DC", out object dcVal) && dcVal is string dcStr)
                {
                    if (Guid.TryParse(dcStr, out Guid dcGuid))
                    {
                        dcPowerPlanGuid = dcGuid;
                    }
                }

                if (settings.Values.TryGetValue("PowerPlan_AutoSwitch", out object autoVal))
                {
                    // Handle different possible types stored in settings
                    if (autoVal is bool autoSwitch)
                    {
                        powerPlanAutoSwitch = autoSwitch;
                    }
                    else if (autoVal is string autoStr)
                    {
                        powerPlanAutoSwitch = autoStr.Equals("True", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        Logger.Warn($"PowerPlan_AutoSwitch has unexpected type: {autoVal?.GetType().Name ?? "null"}");
                    }
                }
                else
                {
                    Logger.Info("PowerPlan_AutoSwitch not found in settings, using default (OFF)");
                }

                // Note: If GUIDs are empty, LoadPowerPlans() will use the current active plan as default

                Logger.Info($"Power plan settings loaded: AC={acPowerPlanGuid}, DC={dcPowerPlanGuid}, AutoSwitch={powerPlanAutoSwitch}");

                // Immediately sync the toggle UI to the loaded value
                // Use isLoadingPowerPlans flag to prevent Toggled event from triggering a save
                isLoadingPowerPlans = true;
                try
                {
                    if (PowerPlanAutoSwitchToggle != null)
                    {
                        PowerPlanAutoSwitchToggle.IsOn = powerPlanAutoSwitch;
                        Logger.Info($"PowerPlanAutoSwitchToggle UI synced to {powerPlanAutoSwitch}");
                    }
                }
                finally
                {
                    isLoadingPowerPlans = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading power plan settings: {ex.Message}");
            }
        }

    }
}
