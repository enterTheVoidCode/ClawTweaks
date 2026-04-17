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

        public async Task GamingWidget_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            Logger.Info($"GamingWidget_LeavingBackground called. Widget is null: {widget == null}, Pipe connected: {App.IsConnected}, WidgetActivity is null: {widgetActivity == null}");

            if (widget != null)
            {
                await widget.CenterWindowAsync();
            }

            if (App.IsConnected)
            {
                Logger.Info("GamingWidget LeavingBackground, syncing UI properties with helper.");

                // Show syncing banner while attempting sync (handles stale connections after sleep)
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    ShowConnectionBanner(BannerState.Syncing);
                });

                // Set flag to prevent auto-save during sync (same pattern as OnNavigatedTo)
                bool syncSucceeded = false;
                isApplyingHelperUpdate = true;
                try
                {
                    // Use timeout to detect stale connections after sleep/hibernate
                    var syncTask = properties.Sync();
                    if (await Task.WhenAny(syncTask, Task.Delay(3000)) == syncTask)
                    {
                        await syncTask; // Ensure completion and propagate any exceptions
                        syncSucceeded = true;
                        Logger.Info("Property sync completed successfully.");
                    }
                    else
                    {
                        Logger.Warn("Property sync timed out - connection may be stale after sleep/hibernate.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Property sync failed - connection may be stale: {ex.Message}");
                }
                finally
                {
                    isApplyingHelperUpdate = false;
                }

                // Handle sync failure - trigger reconnection
                if (!syncSucceeded)
                {
                    Logger.Info("Sync failed, triggering helper reconnection...");
                    // Force relaunch helper - ignore heartbeat since we know connection is broken
                    // Helper has mutex protection so it will restart cleanly
                    await LaunchHelperWithGuardsAsync("LeavingBackground - sync failed", forceLaunch: true);
                    return; // Exit early, let AppServiceConnected handle the rest
                }

                // Sync succeeded - hide banner and continue
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    HideConnectionBanner();
                });

                // Update FPS Limit controls based on RTSS installed status
                UpdateFPSLimitControls();

                // Register Chill FPS handlers after sync to prevent crash
                RegisterChillFPSHandlers();

                // Re-evaluate which profile should be active and reload its settings
                // This is needed because the game may have closed while widget was in background
                // and the UI may still show stale game profile values
                // Must run on UI thread since GetTargetProfileName and LoadProfileSettings access UI controls
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        // Skip reloading profile settings if Default Game Profile is active
                        // The Default Game Profile settings are already applied and should not be overwritten
                        if (defaultGameProfileEnabled?.Value == true)
                        {
                            Logger.Info("Skipping profile reload - Default Game Profile is active");
                            return;
                        }

                        string expectedProfile = GetTargetProfileName();
                        if (expectedProfile != currentProfileName)
                        {
                            // Profile changed (game started/closed) - explicit switch, apply HDR/Resolution
                            Logger.Info($"Profile changed while in background: '{currentProfileName}' -> '{expectedProfile}'");
                            currentProfileName = expectedProfile;
                            LoadProfileSettings(currentProfileName, isExplicitSwitch: true);
                        }
                        else
                        {
                            // Same profile, just reloading UI - don't override game's resolution
                            // (e.g., TDP slider may show game value instead of global profile value)
                            Logger.Info($"Reloading profile settings after returning from background: {currentProfileName}");
                            LoadProfileSettings(currentProfileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error reloading profile after returning from background: {ex.Message}");
                    }
                });
            }
            else
            {
                Logger.Info("GamingWidget LeavingBackground but not connected to the full trust process.");
            }

            appIsInBackground = false;
            UpdateGameBarForegroundSignal("LeavingBackground");
            Logger.Info("GamingWidget_LeavingBackground completed.");
        }

        public void GamingWidget_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            Logger.Info($"GamingWidget_EnteredBackground called. WidgetActivity is null: {widgetActivity == null}");
            appIsInBackground = true;
            UpdateGameBarForegroundSignal("EnteredBackground");
        }

    }
}
