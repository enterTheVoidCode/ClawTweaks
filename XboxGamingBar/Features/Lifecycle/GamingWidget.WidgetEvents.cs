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

        private async void GamingWidget_RequestedThemeChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SetBackgroundColor();
            });
        }

        private async void GamingWidget_SettingsClicked(XboxGameBarWidget sender, object args)
        {
            await widget.ActivateSettingsAsync();
        }

        private bool hasAppliedInitialSize = false;

        // Rising-edge tracker for the default-tab-on-open trigger (see UpdateGameBarForegroundSignal).
        // Mirrors the helper's own _lastGameBarForeground edge detection for the RB auto-hop.
        private bool _lastGameBarForegroundForTab = false;

        private async void GamingWidget_VisibleChanged(XboxGameBarWidget sender, object args)
        {
            try
            {
                bool isVisible = sender?.Visible ?? false;
                Logger.Info($"GamingWidget_VisibleChanged: Visible={isVisible}, DisplayMode={sender?.GameBarDisplayMode.ToString() ?? "Unknown"}");
                UpdateGameBarForegroundSignal("VisibleChanged");

                // Apply the user's tab order/visibility (idempotent) and, if enabled, jump to their
                // chosen default tab. Default behaviour (option off) keeps the last position.
                if (isVisible)
                {
                    ApplyTabPrefs();
                    ApplyDefaultTabOnOpen("VisibleChanged");

                    // Re-read current brightness/volume so the media sliders reflect changes made
                    // outside the app (hardware keys, Windows quick settings) while the Game Bar was
                    // closed. Retrying variant: VisibleChanged can fire before the pipe reconnects.
                    _ = RefreshMediaSliderLevelsSoonAsync();
                }

                // Resize to full height on first activation.
                // Delay to let Game Bar finish restoring its cached layout first.
                if (isVisible && !hasAppliedInitialSize && sender != null)
                {
                    hasAppliedInitialSize = true;
                    await Task.Delay(500);
                    var success = await sender.TryResizeWindowAsync(new Windows.Foundation.Size(464, 1080));
                    Logger.Info($"Initial TryResizeWindowAsync(464x1080): success={success}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GamingWidget_VisibleChanged failed: {ex.Message}");
            }
        }

        private void GamingWidget_GameBarDisplayModeChanged(XboxGameBarWidget sender, object args)
        {
            try
            {
                Logger.Info($"GamingWidget_GameBarDisplayModeChanged: DisplayMode={sender?.GameBarDisplayMode.ToString() ?? "Unknown"}, Visible={sender?.Visible ?? false}");
                UpdateGameBarForegroundSignal("GameBarDisplayModeChanged");
            }
            catch (Exception ex)
            {
                Logger.Error($"GamingWidget_GameBarDisplayModeChanged failed: {ex.Message}");
            }
        }

        private void UpdateGameBarForegroundSignal(string source)
        {
            try
            {
                bool widgetVisible = widget?.Visible ?? false;
                XboxGameBarDisplayMode displayMode = widget?.GameBarDisplayMode ?? XboxGameBarDisplayMode.Foreground;

                // Full Game Bar visibility signal:
                // - true when the overlay is foreground (even if this specific widget tab is not selected)
                // - false when app is backgrounded or Game Bar is not in foreground mode
                bool gameBarForeground = !appIsInBackground && displayMode == XboxGameBarDisplayMode.Foreground;
                isForeground.ForceSetValue(gameBarForeground);

                Logger.Info($"GameBar foreground signal update ({source}): value={gameBarForeground}, displayMode={displayMode}, widgetVisible={widgetVisible}, appBackground={appIsInBackground}");

                // Default-tab-on-open: fire on the foreground RISING edge — the SAME false→true edge the
                // helper's RB auto-hop rides. This edge lands ~2 s BEFORE our own VisibleChanged=True (the
                // RB-hop has to bring us into view first), so switching the tab here means the correct tab
                // is already shown by the time ClawTweaks scrolls into view — instead of only reacting once
                // Visible flips true. NavRadioButton_Checked only toggles ScrollViewer visibility, so it is
                // safe to run while this widget is still off-screen. Idempotent (VisibleChanged still calls
                // it too); the earliest successful call wins and the rest are cheap no-ops.
                if (gameBarForeground && !_lastGameBarForegroundForTab)
                    ApplyDefaultTabOnOpen("ForegroundEdge:" + source);
                _lastGameBarForegroundForTab = gameBarForeground;
            }
            catch (Exception ex)
            {
                Logger.Warn($"UpdateGameBarForegroundSignal failed ({source}): {ex.Message}");
            }
        }

    }
}
