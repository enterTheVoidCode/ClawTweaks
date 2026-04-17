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

        private void StickyTDPToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Skip during initialization - don't capture TDP or start timer until profile loads
            if (isLoadingStickyTDPSettings) return;
            // Skip during mode changes - don't save forced-off state
            if (isUpdatingTDPMode) return;

            Logger.Info($"StickyTDPToggle toggled to: {StickyTDPToggle.IsOn}");

            // Save setting to LocalSettings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["StickyTDPEnabled"] = StickyTDPToggle.IsOn;

            if (StickyTDPToggle.IsOn)
            {
                // Store current TDP limit as target
                targetTDPLimit = TDPSlider.Value;
                Logger.Info($"Sticky TDP enabled - monitoring TDP limit: {targetTDPLimit}W");

                // Start the monitoring timer
                StartStickyTDPTimer();
            }
            else
            {
                // Stop the monitoring timer
                StopStickyTDPTimer();
                Logger.Info("Sticky TDP disabled");
            }

            // Trigger profile save if SaveStickyTDP is enabled
            SettingChanged(sender, e);
        }

        private void StickyTDPIntervalSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickyTDPIntervalSlider == null) return;
            if (isLoadingStickyTDPSettings) return;

            stickyTDPCheckIntervalSeconds = (int)Math.Round(e.NewValue);
            Logger.Info($"Sticky TDP check interval changed to: {stickyTDPCheckIntervalSeconds}s");

            // Save setting to LocalSettings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["StickyTDPInterval"] = stickyTDPCheckIntervalSeconds;

            // Update the value display
            if (StickyTDPIntervalValue != null)
            {
                StickyTDPIntervalValue.Text = $"{stickyTDPCheckIntervalSeconds}s";
            }

            // Restart timer with new interval if it's running
            if (StickyTDPToggle?.IsOn == true)
            {
                StopStickyTDPTimer();
                StartStickyTDPTimer();
            }

            // Trigger profile save if SaveStickyTDP is enabled
            SettingChanged(sender, e);
        }

        private void StartStickyTDPTimer()
        {
            if (stickyTDPTimer == null)
            {
                stickyTDPTimer = new DispatcherTimer();
                stickyTDPTimer.Tick += StickyTDPTimer_Tick;
            }

            stickyTDPTimer.Interval = TimeSpan.FromSeconds(stickyTDPCheckIntervalSeconds);
            stickyTDPTimer.Start();
            Logger.Info($"Sticky TDP timer started with {stickyTDPCheckIntervalSeconds}s interval");
        }

        private void StopStickyTDPTimer()
        {
            if (stickyTDPTimer != null)
            {
                stickyTDPTimer.Stop();
                Logger.Info("Sticky TDP timer stopped");
            }
        }

        private async void StickyTDPTimer_Tick(object sender, object e)
        {
            try
            {
                // Skip Sticky TDP in non-Custom modes - preset modes manage TDP automatically
                if (legionGoDetected?.Value == true && legionPerformanceMode?.Value != 255)
                {
                    Logger.Debug($"Sticky TDP: Skipping - using {GetLegionModeShortName(legionPerformanceMode?.Value ?? 0)} preset mode");
                    return;
                }

                // Smart check: Only reapply if current hardware TDP differs from target
                // Parse STAPM limit from currentTdp (format: "STAPM:21W FAST:21W SLOW:21W")
                int currentStapmLimit = -1;
                if (currentTdp != null && !string.IsNullOrEmpty(currentTdp.Value))
                {
                    var parts = currentTdp.Value.Split(' ');
                    foreach (var part in parts)
                    {
                        if (part.StartsWith("STAPM:"))
                        {
                            var valueStr = part.Substring(6).Replace("W", "");
                            if (int.TryParse(valueStr, out currentStapmLimit))
                            {
                                break;
                            }
                        }
                    }
                }

                // Check if hardware TDP matches our target
                if (currentStapmLimit == (int)targetTDPLimit)
                {
                    Logger.Info($"Sticky TDP: Hardware STAPM limit ({currentStapmLimit}W) matches target ({targetTDPLimit}W), no action needed.");
                    return;
                }

                // Hardware TDP differs from target - need to reapply
                Logger.Info($"Sticky TDP: Hardware STAPM limit ({currentStapmLimit}W) differs from target ({targetTDPLimit}W), reapplying...");

                // Set flag to prevent slider UI flicker during reapply
                isStickyTDPReapplying = true;

                // To force the helper to actually apply the TDP (even if its internal value matches),
                // we need to change the value first, then set it to the target.
                // This triggers NotifyPropertyChanged -> Manager.SetTDP() in the helper.
                if (App.IsConnected)
                {
                    // Calculate a different value to force a change
                    int tempValue = (int)targetTDPLimit == 15 ? 16 : (int)targetTDPLimit - 1;

                    // First, set to temp value to force a change
                    var tempRequest = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.TDP },
                        { "Content", tempValue },
                        { "UpdatedTime", DateTimeOffset.Now.Ticks }
                    };
                    await App.SendMessageAsync(tempRequest);

                    // Small delay to ensure the temp value is processed
                    await Task.Delay(50);

                    // Then set to actual target value
                    var targetRequest = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.TDP },
                        { "Content", (int)targetTDPLimit },
                        { "UpdatedTime", DateTimeOffset.Now.Ticks }
                    };

                    var response = await App.SendMessageAsync(targetRequest);
                    if (response != null)
                    {
                        Logger.Info($"Sticky TDP: Successfully reapplied TDP {targetTDPLimit}W to hardware.");
                    }
                    else
                    {
                        Logger.Warn($"Sticky TDP: Got no response from helper when setting TDP.");
                    }

                    // Small delay to ensure helper messages are processed before clearing flag
                    await Task.Delay(100);
                }
                else
                {
                    Logger.Warn("Sticky TDP: No connection to helper app.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in Sticky TDP timer: {ex.Message}");
            }
            finally
            {
                // Clear flag to allow normal slider updates
                isStickyTDPReapplying = false;
            }
        }

    }
}
