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
        private void DebugExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isDebugExpanded = !isDebugExpanded;

            if (DebugContent != null)
            {
                DebugContent.Visibility = isDebugExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (DebugExpandIcon != null)
            {
                DebugExpandIcon.Glyph = isDebugExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private bool isThemeInitialized = false;

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isThemeInitialized) return; // Don't save until initial load completes

            if (ThemeComboBox?.SelectedItem is ComboBoxItem item)
            {
                string themeName = item.Content?.ToString() ?? "Default";
                ApplyTheme(themeName);
                SaveThemeSetting(themeName);
            }
        }

        private void ApplyTheme(string themeName)
        {
            if (!WidgetThemes.TryGetValue(themeName, out var theme))
            {
                Logger.Warn($"Theme '{themeName}' not found, using Default");
                theme = WidgetThemes["Default"];
                themeName = "Default";
            }

            currentThemeName = themeName;
            Logger.Info($"Applying theme: {themeName}");

            // "Windows" theme: pull the LIVE Windows system accent colour as the source colour.
            if (theme.UseWindowsAccent)
            {
                try
                {
                    var ui = new Windows.UI.ViewManagement.UISettings();
                    var winAccent = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                    // Clone so we don't mutate the shared dictionary entry.
                    theme = new ThemeColors
                    {
                        Name = theme.Name, MonoFromAccent = true, UseWindowsAccent = true,
                        ShimmerEnabled = theme.ShimmerEnabled,
                        AccentColor = Windows.UI.Color.FromArgb(255, winAccent.R, winAccent.G, winAccent.B)
                    };
                    Logger.Info($"[Theme] Windows accent = #{winAccent.R:X2}{winAccent.G:X2}{winAccent.B:X2}");
                }
                catch (Exception ex) { Logger.Warn($"[Theme] Windows accent read failed: {ex.Message}"); }
            }

            // Mono / Windows theme: derive the entire palette from the single accent colour.
            if (theme.MonoFromAccent)
                theme = ComputeMonoTheme(theme);

            // Drive every accent-coloured control (slider fill, toggle ON, focus, accent text)
            // from THIS theme's accent — never the Windows system accent. Mutates shared brush
            // instances so already-created controls update live.
            ApplyAccentBrushes(theme.EffectiveAccent());

            // Update page background (diagonal gradient when the theme defines a second stop = glass look)
            var pageBrush = ThemeFill(theme.PageBackground, theme.PageBackground2, diagonal: true);
            this.Background = pageBrush;
            widgetDarkThemeBrush = pageBrush;

            // Refresh the tile brushes from the theme. On the initial load the grid is built AFTER
            // this with the brushes already set (see InitializeQuickSettings), so we must NOT rebuild
            // here then — doing so disturbed scroll/focus/metrics. Only repaint the grid for a real
            // runtime theme switch (isThemeInitialized == true), and restore tile states afterwards.
            try
            {
                UpdateTileBrushesFromTheme(theme);
                if (isThemeInitialized && quickSettingsInitialized)
                {
                    RebuildQuickSettingsTiles();
                    UpdateQuickSettingsTileStates();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Theme: tile refresh failed: {ex.Message}");
            }

            // Metrics bar (stats row above the tiles) — repaint when the theme defines it
            // (intended a touch lighter than the active tiles).
            try
            {
                if (QuickMetricsRow != null && theme.MetricsBackground != null)
                {
                    QuickMetricsRow.Background = ThemeFill(theme.MetricsBackground.Value, theme.MetricsBackground2, diagonal: true);
                    QuickMetricsRow.BorderBrush = new SolidColorBrush(theme.MetricsBorder ?? theme.CardBorder);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Theme: metrics bar refresh failed: {ex.Message}");
            }

            // Update resource brushes (for new elements)
            try
            {
                Resources["PageBackgroundBrush"] = ThemeFill(theme.PageBackground, theme.PageBackground2);
                Resources["CardBackgroundBrush"] = new SolidColorBrush(theme.CardBackground);
                Resources["CardBorderBrush"] = new SolidColorBrush(theme.CardBorder);
                Resources["ButtonBackground"] = new SolidColorBrush(theme.ButtonBackground);
                Resources["ButtonBorderBrush"] = new SolidColorBrush(theme.ButtonBorder);
                Resources["TileOffBackground"] = new SolidColorBrush(theme.TileOff);
                Resources["TileOnBackground"] = new SolidColorBrush(theme.TileOn);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating theme resources: {ex.Message}");
            }

            // Manually update existing elements (StaticResource doesn't update at runtime)
            try
            {
                var cardBgBrush = new SolidColorBrush(theme.CardBackground);
                var cardBorderBrush = new SolidColorBrush(theme.CardBorder);
                var accentBrush = new SolidColorBrush(theme.AccentColor);
                var textSecondaryBrush = new SolidColorBrush(theme.TextSecondary);

                // Update all Border elements (cards)
                ApplyThemeToVisualTree(this, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);

                Logger.Info($"Theme '{themeName}' applied to visual tree");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying theme to visual tree: {ex.Message}");
            }
        }

        /// <summary>
        /// Mutates the shared accent brush instances (defined in Page.Resources) to the given accent.
        /// Controls reference these instances via {StaticResource}/{ThemeResource}, so changing the
        /// brush colour updates slider fills, toggle ON state, accent text etc. live — no rebuild.
        /// </summary>
        /// <summary>Effective accent colour of the active in-app theme (replaces the Windows system accent).</summary>
        internal Windows.UI.Color CurrentThemeAccent()
        {
            if (WidgetThemes.TryGetValue(currentThemeName, out var t))
                return t.EffectiveAccent();
            return Windows.UI.Color.FromArgb(255, 0, 200, 255);
        }

        private void ApplyAccentBrushes(Windows.UI.Color accent)
        {
            try
            {
                var accentLight = ThemeColors.Shade(accent, 0.40);  // brighter end of the slider gradient
                var accentHi    = ThemeColors.Shade(accent, 0.20);  // pointer-over
                var accentLo    = ThemeColors.Shade(accent, -0.15); // pressed

                void SetSolid(string key, Windows.UI.Color c)
                {
                    if (Resources.TryGetValue(key, out var o) && o is SolidColorBrush b) b.Color = c;
                }
                void SetGrad(string key, Windows.UI.Color a, Windows.UI.Color b2)
                {
                    if (Resources.TryGetValue(key, out var o) && o is LinearGradientBrush g && g.GradientStops.Count >= 2)
                    {
                        g.GradientStops[0].Color = a;
                        g.GradientStops[1].Color = b2;
                    }
                }

                SetSolid("ThemeAccentBrush", accent);
                SetSolid("SystemAccentBrush", accent);

                SetGrad("SliderTrackValueFill",            accent, accentLight);
                SetGrad("SliderTrackValueFillPointerOver", accentHi, ThemeColors.Shade(accentHi, 0.40));
                SetGrad("SliderTrackValueFillPressed",     accentLo, ThemeColors.Shade(accentLo, 0.40));

                SetSolid("ToggleSwitchFillOn",            accent);
                SetSolid("ToggleSwitchFillOnPointerOver", accentHi);
                SetSolid("ToggleSwitchFillOnPressed",     accentLo);
                SetSolid("ToggleSwitchStrokeOn",            accent);
                SetSolid("ToggleSwitchStrokeOnPointerOver", accentHi);
                SetSolid("ToggleSwitchStrokeOnPressed",     accentLo);
            }
            catch (Exception ex) { Logger.Warn($"ApplyAccentBrushes: {ex.Message}"); }
        }

        /// <summary>
        /// Builds a full ThemeColors palette derived from a single accent colour (the "Mono" look):
        /// dark near-black surfaces shaded down from the accent, highlights shaded up. Changing only
        /// the theme's AccentColor re-skins the entire theme.
        /// </summary>
        private static ThemeColors ComputeMonoTheme(ThemeColors baseTheme)
        {
            var a = baseTheme.AccentColor;
            Windows.UI.Color S(double f) => ThemeColors.Shade(a, f);
            Windows.UI.Color Sa(double f, byte alpha)
            {
                var c = ThemeColors.Shade(a, f);
                return Windows.UI.Color.FromArgb(alpha, c.R, c.G, c.B);
            }
            return new ThemeColors
            {
                Name = baseTheme.Name,
                MonoFromAccent = true,
                ShimmerEnabled = baseTheme.ShimmerEnabled,
                AccentColor      = a,
                PageBackground   = S(-0.88),
                PageBackground2  = S(-0.94),
                CardBackground   = Sa(-0.80, 210),
                CardBorder       = S(-0.45),
                TextPrimary      = S(0.88),
                TextSecondary    = S(0.30),
                ButtonBackground = S(-0.78),
                ButtonBorder     = S(-0.40),
                TileOff          = S(-0.84),
                TileOff2         = S(-0.92),
                TileOn           = S(-0.25),
                TileOn2          = S(-0.50),
                GlowColor        = a,
                TileIcon         = S(0.45),
                MetricsBackground  = S(-0.40),
                MetricsBackground2 = S(-0.62),
                MetricsBorder      = S(-0.05)
            };
        }

        /// <summary>
        /// Returns a vertical (top→bottom) LinearGradientBrush when <paramref name="c2"/> is set
        /// (glass look), otherwise a flat SolidColorBrush of <paramref name="c1"/>.
        /// </summary>
        private static Windows.UI.Xaml.Media.Brush ThemeFill(Windows.UI.Color c1, Windows.UI.Color? c2, bool diagonal = false)
        {
            if (c2 == null) return new SolidColorBrush(c1);
            return new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = diagonal ? new Windows.Foundation.Point(1, 1) : new Windows.Foundation.Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop { Color = c1, Offset = 0 },
                    new GradientStop { Color = c2.Value, Offset = 1 }
                }
            };
        }

        /// <summary>
        /// Rebuilds the Quick Settings tile brushes from a theme (gradient when the theme provides
        /// a second stop). Called from ApplyTheme; the grid is repainted afterwards.
        /// </summary>
        private void UpdateTileBrushesFromTheme(ThemeColors theme)
        {
            // Diagonal (top-left→bottom-right) gradients so tiles match the page sheen.
            tileOffBrush = ThemeFill(theme.TileOff, theme.TileOff2, diagonal: true);
            tileOnBrush  = ThemeFill(theme.TileOn,  theme.TileOn2,  diagonal: true);
            // Active/trigger accents derive from the theme accent so they fit the palette.
            tileActiveBrush = new SolidColorBrush(theme.GlowColor ?? theme.AccentColor);
            // Tile icon tint (light azure for Next Gen Claw, white otherwise).
            tileIconBrush = theme.TileIcon != null
                ? new SolidColorBrush(theme.TileIcon.Value)
                : new SolidColorBrush(Windows.UI.Colors.White);
            // Secondary caption text (tiles + metrics labels) — from the theme's secondary colour.
            tileTextBrush = new SolidColorBrush(theme.TextSecondary);
        }

        /// <summary>
        /// Recolours a nav tab's active "SelectionBackground" pill from the current theme. Called
        /// when a tab is checked, so pills whose templates weren't realised at ApplyTheme time
        /// (collapsed/lazy tabs) still get the themed look instead of the default grey gradient.
        /// </summary>
        private void ApplyNavPillTheme(DependencyObject navItem)
        {
            try
            {
                if (navItem == null) return;
                if (!WidgetThemes.TryGetValue(currentThemeName, out var theme)) return;
                var pill = FindDescendantByName(navItem, "SelectionBackground") as Border;
                if (pill == null) return;
                pill.Background = (theme.TileOn2 != null)
                    ? ThemeFill(theme.TileOn, theme.TileOn2, diagonal: true)
                    : new SolidColorBrush(theme.ButtonBackground);
                pill.BorderBrush = new SolidColorBrush(theme.GlowColor ?? theme.AccentColor);
            }
            catch (Exception ex) { Logger.Debug($"ApplyNavPillTheme: {ex.Message}"); }
        }

        private static FrameworkElement FindDescendantByName(DependencyObject parent, string name)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Name == name) return fe;
                var found = FindDescendantByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void ApplyThemeToVisualTree(DependencyObject parent, ThemeColors theme,
            SolidColorBrush cardBgBrush, SolidColorBrush cardBorderBrush,
            SolidColorBrush accentBrush, SolidColorBrush textSecondaryBrush)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Update Border elements (cards use CardStyle with specific properties)
                if (child is Border border)
                {
                    // Active-tab "register" pill (template part). Recolour it per theme so the
                    // selected tab matches the palette: gradient themes (Next Gen Claw) get the
                    // tile-on gradient, flat themes a solid button surface; border = accent/glow.
                    if (border.Name == "SelectionBackground")
                    {
                        border.Background = (theme.TileOn2 != null)
                            ? ThemeFill(theme.TileOn, theme.TileOn2)
                            : new SolidColorBrush(theme.ButtonBackground);
                        border.BorderBrush = new SolidColorBrush(theme.GlowColor ?? theme.AccentColor);
                    }
                    // Check if this looks like a card (has corner radius and padding typical of CardStyle)
                    // Skip borders with LinearGradientBrush backgrounds (custom gradients for "smart" features like DGP card)
                    else if (border.CornerRadius.TopLeft == 8 && border.Padding.Left == 12 &&
                        !(border.Background is LinearGradientBrush))
                    {
                        border.Background = cardBgBrush;
                        border.BorderBrush = cardBorderBrush;
                    }
                }

                // Update accent-colored TextBlocks (section headers, card values)
                if (child is TextBlock textBlock)
                {
                    if (textBlock.Foreground is SolidColorBrush brush)
                    {
                        // Check for cyan accent color (#00C8FF) - update to new accent
                        if (brush.Color.R == 0 && brush.Color.G == 200 && brush.Color.B == 255)
                        {
                            textBlock.Foreground = accentBrush;
                        }
                        // Check for secondary text color (#A0A0A0)
                        else if (brush.Color.R == 160 && brush.Color.G == 160 && brush.Color.B == 160)
                        {
                            textBlock.Foreground = textSecondaryBrush;
                        }
                    }
                }

                // Recurse into children
                ApplyThemeToVisualTree(child, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            }
        }

        private async Task ApplyThemeOnLoadAsync(string themeName)
        {
            // Wait for UI to fully initialize
            await Task.Delay(100);

            try
            {
                // Set ComboBox selection (isThemeInitialized is still false, so this won't trigger save)
                if (ThemeComboBox != null)
                {
                    for (int i = 0; i < ThemeComboBox.Items.Count; i++)
                    {
                        if (ThemeComboBox.Items[i] is ComboBoxItem item && item.Content?.ToString() == themeName)
                        {
                            ThemeComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                ApplyTheme(themeName);

                // Apply to all tabs to prevent flash when switching
                ApplyThemeToCurrentTab();
            }
            finally
            {
                // Now allow saves on future changes
                isThemeInitialized = true;
            }
        }

        private void SaveThemeSetting(string themeName)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values["WidgetTheme"] = themeName;
                Logger.Info($"Theme setting saved: {themeName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save theme setting: {ex.Message}");
            }
        }

        /// <summary>Maps theme names saved by older builds to their current keys after the rename.</summary>
        private static string MigrateThemeName(string name)
        {
            switch (name)
            {
                case "Next Gen Claw": return "Next Gen Claw (Glas)";
                case "Claw Blue":     return "Claw Blue (Glas)";
                case "Box X":         return "Box X (Glas)";
                case "Nintendon't":   return "Nintendon't (Glas)";
                case "Chrilleteur":   return "Chrilleteur (Glas)";
                case "Mono":          return "Windows";
                default:              return name;
            }
        }

        private void LoadThemeSetting()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("WidgetTheme", out var saved) && saved is string themeName)
                {
                    themeName = MigrateThemeName(themeName);  // map renamed themes (e.g. "Mono"→"Windows", glass "(Glas)")
                    currentThemeName = themeName;
                    Logger.Info($"Theme loaded from settings: {themeName}");

                    // Defer visual updates until UI is fully ready
                    _ = ApplyThemeOnLoadAsync(themeName);
                }
                else
                {
                    // No saved theme → apply the default glass look so fresh installs get it.
                    currentThemeName = "Next Gen Claw (Glas)";
                    _ = ApplyThemeOnLoadAsync("Next Gen Claw (Glas)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load theme setting: {ex.Message}");
                isThemeInitialized = true; // Allow saves even on error
            }
        }

        private bool isAboutExpanded = false;

        private void AboutExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isAboutExpanded = !isAboutExpanded;

            if (AboutContent != null)
            {
                AboutContent.Visibility = isAboutExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (AboutExpandIcon != null)
            {
                AboutExpandIcon.Glyph = isAboutExpanded ? "\uE70E" : "\uE70D";
            }

            // Update version text dynamically
            if (isAboutExpanded && AboutVersionText != null)
            {
                try
                {
                    var version = Windows.ApplicationModel.Package.Current.Id.Version;
                    AboutVersionText.Text = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                }
                catch
                {
                    // Keep default version text
                }
            }
        }

        private async void DonateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Send message to helper to launch URL (Game Bar blocks direct URL launching)
                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("LaunchUrl", "https://paypal.me/corando98");
                    await App.SendMessageAsync(message);
                    Logger.Info("Sent LaunchUrl request to helper");
                }
                else
                {
                    Logger.Warn("Cannot launch donate URL - no connection to helper");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send donate link request: {ex.Message}");
            }
        }

        private async void RestartHelperButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RestartHelperButton.IsEnabled = false;
                RestartHelperButton.Content = "Restarting...";

                // Send exit command to helper via IPC
                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExitHelper", true);

                    Logger.Info("Sending ExitHelper command to helper");
                    var response = await App.SendMessageAsync(message);

                    if (response != null)
                    {
                        Logger.Info("Helper acknowledged exit command");
                    }

                    // Disconnect the pipe so we can detect when helper is truly gone
                    App.PipeClient?.Disconnect();
                }

                // Wait for helper to exit and release mutex
                // Helper waits 3 seconds before force-killing, so we wait 4 seconds to be safe
                Logger.Info("Waiting for helper to exit...");
                await Task.Delay(4000);

                // Verify helper has disconnected
                if (App.IsConnected)
                {
                    Logger.Warn("Helper still connected after exit command - forcing disconnect");
                    App.PipeClient?.Disconnect();
                    await Task.Delay(1000);
                }

                // Launch new helper instance
                Logger.Info("Launching new helper instance");
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();

                // Give the helper a moment to start its pipe server, then try to reconnect
                await Task.Delay(1000);
                Logger.Info("Attempting to reconnect to helper via Named Pipe");
                _ = TryConnectPipeAsync();

                await Task.Delay(1500);
                RestartHelperButton.Content = "Restart Helper";
                RestartHelperButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to restart helper: {ex.Message}");
                RestartHelperButton.Content = "Restart Helper";
                RestartHelperButton.IsEnabled = true;
            }
        }

        private async void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportLogsButton.IsEnabled = false;
                ExportLogsButton.Content = "Exporting...";

                // Send export logs command to helper via IPC
                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExportLogs", true);

                    Logger.Info("Sending ExportLogs command to helper");
                    var response = await App.SendMessageAsync(message);

                    if (response != null)
                    {
                        bool success = false;
                        if (response.TryGetValue("Success", out object successObj) && successObj is bool successVal)
                            success = successVal;

                        if (success)
                        {
                            var path = response.TryGetValue("Path", out object pathObj) ? pathObj as string : "Desktop";
                            Logger.Info($"Logs exported successfully to: {path}");
                            ExportLogsButton.Content = "Exported!";
                        }
                        else
                        {
                            var error = response.TryGetValue("Error", out object errorObj) ? errorObj as string : "Unknown error";
                            Logger.Error($"Export logs failed: {error}");
                            ExportLogsButton.Content = "Export Failed";
                        }
                    }
                    else
                    {
                        Logger.Error("Export logs request failed - no response");
                        ExportLogsButton.Content = "Export Failed";
                    }
                }
                else
                {
                    Logger.Error("Cannot export logs - no connection to helper");
                    ExportLogsButton.Content = "No Helper";
                }

                await Task.Delay(2000);
                ExportLogsButton.Content = "Export Logs";
                ExportLogsButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export logs: {ex.Message}");
                ExportLogsButton.Content = "Export Failed";
                await Task.Delay(2000);
                ExportLogsButton.Content = "Export Logs";
                ExportLogsButton.IsEnabled = true;
            }
        }

        private async void KillGoTweaksButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Kill GoTweaks requested by user");

                // Send exit command to helper using all available methods
                bool exitSent = false;

                // Try via Named Pipe
                if (App.PipeClient?.IsConnected == true)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExitHelper", true);
                    Logger.Info("Sending ExitHelper via Named Pipe");
                    await App.SendMessageAsync(message);
                    exitSent = true;
                }
                // Not connected - try temporary pipe connection
                else
                {
                    Logger.Info("Not connected - attempting temporary pipe connection for ExitHelper");
                    try
                    {
                        using (var tempPipe = new System.IO.Pipes.NamedPipeClientStream(".", "GoTweaksHelper", System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous))
                        {
                            var connectTask = tempPipe.ConnectAsync(2000);
                            if (await Task.WhenAny(connectTask, Task.Delay(2500)) == connectTask)
                            {
                                using (var writer = new System.IO.StreamWriter(tempPipe, System.Text.Encoding.UTF8, 4096, leaveOpen: true))
                                {
                                    writer.AutoFlush = true;
                                    await writer.WriteLineAsync("{\"RequestId\":0,\"ExitHelper\":true}");
                                }
                                Logger.Info("Sent ExitHelper via temporary pipe connection");
                                exitSent = true;
                            }
                        }
                    }
                    catch (Exception pipeEx)
                    {
                        Logger.Warn($"Temporary pipe connection failed: {pipeEx.Message}");
                    }
                }

                if (exitSent)
                {
                    // Give helper time to exit (helper waits 3 seconds before force-killing)
                    Logger.Info("Waiting for helper to exit...");
                    await Task.Delay(4000);
                }
                else
                {
                    Logger.Warn("Could not send ExitHelper - helper may still be running");
                }

                // Exit the widget application
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to kill GoTweaks: {ex.Message}");
                // Still try to exit even if helper communication failed
                Application.Current.Exit();
            }
        }

        /// <summary>
        /// Compares two version strings (e.g., "v0.3.902" vs "v0.3.1001.0").
        /// Returns true if latestVersion is newer than currentVersion.
        /// </summary>
        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            // Strip 'v' prefix if present
            var latest = latestVersion.TrimStart('v', 'V');
            var current = currentVersion.TrimStart('v', 'V');

            // Split into parts
            var latestParts = latest.Split('.');
            var currentParts = current.Split('.');

            // Compare each part numerically
            int maxLength = Math.Max(latestParts.Length, currentParts.Length);
            for (int i = 0; i < maxLength; i++)
            {
                int latestNum = 0;
                int currentNum = 0;

                if (i < latestParts.Length && int.TryParse(latestParts[i], out int lp))
                    latestNum = lp;
                if (i < currentParts.Length && int.TryParse(currentParts[i], out int cp))
                    currentNum = cp;

                if (latestNum > currentNum)
                    return true;
                if (latestNum < currentNum)
                    return false;
            }

            return false; // Versions are equal
        }

        private async void CheckForUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckForUpdateButton.IsEnabled = false;
                CheckForUpdateButton.Content = "Checking...";
                UpdateStatusText.Visibility = Visibility.Visible;
                UpdateStatusText.Text = "Checking for updates...";
                UpdateButton.Visibility = Visibility.Collapsed;
                _pendingUpdateZipUrl = null;
                _pendingUpdateVersion = null;

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "GoTweaks-UpdateChecker");
                    var response = await httpClient.GetStringAsync("https://api.github.com/repos/corando98/GoTweaks/releases/latest");

                    // Parse JSON response using Windows.Data.Json
                    var jsonObject = Windows.Data.Json.JsonObject.Parse(response);
                    var latestVersion = jsonObject.GetNamedString("tag_name", "");

                    // Get current version from package
                    var packageVersion = Package.Current.Id.Version;
                    var currentVersion = $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";

                    Logger.Info($"Update check: current={currentVersion}, latest={latestVersion}");

                    if (!string.IsNullOrEmpty(latestVersion) && IsNewerVersion(latestVersion, currentVersion))
                    {
                        // Find the .zip asset download URL
                        string zipUrl = null;
                        if (jsonObject.ContainsKey("assets"))
                        {
                            var assets = jsonObject.GetNamedArray("assets");
                            foreach (var asset in assets)
                            {
                                var assetObj = asset.GetObject();
                                var name = assetObj.GetNamedString("name", "");
                                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    zipUrl = assetObj.GetNamedString("browser_download_url", "");
                                    break;
                                }
                            }
                        }

                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                        UpdateStatusText.Text = $"New version available: {latestVersion}\nCurrent: {currentVersion}";

                        if (!string.IsNullOrEmpty(zipUrl))
                        {
                            _pendingUpdateZipUrl = zipUrl;
                            _pendingUpdateVersion = latestVersion;
                            UpdateButton.Visibility = Visibility.Visible;
                            Logger.Info($"Update zip URL found: {zipUrl}");
                        }
                        else
                        {
                            UpdateStatusText.Text += "\n(No zip asset found in release)";
                            Logger.Warn("No zip asset found in latest release");
                        }
                    }
                    else
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
                        UpdateStatusText.Text = $"You're up to date! ({currentVersion})";
                    }
                }

                CheckForUpdateButton.Content = "Check for Update";
                CheckForUpdateButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check for update: {ex.Message}");
                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                UpdateStatusText.Text = $"Failed to check for updates: {ex.Message}";
                CheckForUpdateButton.Content = "Check for Update";
                CheckForUpdateButton.IsEnabled = true;
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingUpdateZipUrl))
            {
                Logger.Warn("Update clicked but no pending update URL");
                return;
            }

            try
            {
                UpdateButton.IsEnabled = false;
                UpdateButton.Content = "Downloading...";
                UpdateStatusText.Text = $"Downloading {_pendingUpdateVersion}...";

                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("Command", (int)Shared.Enums.Command.Set);
                    message.Add("Function", (int)Shared.Enums.Function.InstallUpdate);
                    message.Add("Content", _pendingUpdateZipUrl);
                    var result = await App.SendMessageAsync(message);

                    if (result != null)
                    {
                        if (result.TryGetValue("UpdateStatus", out object status))
                        {
                            var statusStr = status?.ToString() ?? "";
                            if (statusStr == "Installing")
                            {
                                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                                UpdateStatusText.Text = "Installing update... Please follow the installer prompts.";
                                UpdateButton.Content = "Installing...";
                            }
                            else if (statusStr.StartsWith("Error"))
                            {
                                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                                UpdateStatusText.Text = statusStr;
                                UpdateButton.Content = "Update";
                                UpdateButton.IsEnabled = true;
                            }
                        }
                    }
                    else
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        UpdateStatusText.Text = "Failed to communicate with helper";
                        UpdateButton.Content = "Update";
                        UpdateButton.IsEnabled = true;
                    }
                }
                else
                {
                    UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                    UpdateStatusText.Text = "Helper not connected";
                    UpdateButton.Content = "Update";
                    UpdateButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start update: {ex.Message}");
                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                UpdateStatusText.Text = $"Update failed: {ex.Message}";
                UpdateButton.Content = "Update";
                UpdateButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Automatically checks for updates on startup if the setting is enabled.
        /// Shows a banner if an update is available.
        /// </summary>
        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                // Check if auto-update check is enabled (default: true)
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                bool autoCheckEnabled = true;
                if (settings.Values.TryGetValue("AutoUpdateCheckEnabled", out object val) && val is bool b)
                {
                    autoCheckEnabled = b;
                }

                // Update the toggle to match saved setting
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (AutoUpdateCheckToggle != null)
                    {
                        AutoUpdateCheckToggle.IsOn = autoCheckEnabled;
                    }
                });

                if (!autoCheckEnabled)
                {
                    Logger.Info("Auto-update check is disabled, skipping startup check");
                    return;
                }

                Logger.Info("Checking for updates on startup...");

                // Small delay to let the UI settle first
                await Task.Delay(2000);

                var packageVersion = Package.Current.Id.Version;
                var currentVersion = $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";

                string remoteVersion = null;
                string remoteZipUrl = null;
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.DefaultRequestHeaders.Add("User-Agent", "GoTweaks-UpdateChecker");
                        var response = await httpClient.GetStringAsync("https://api.github.com/repos/corando98/GoTweaks/releases/latest");

                        var jsonObject = Windows.Data.Json.JsonObject.Parse(response);
                        remoteVersion = jsonObject.GetNamedString("tag_name", "");

                        if (jsonObject.ContainsKey("assets"))
                        {
                            var assets = jsonObject.GetNamedArray("assets");
                            foreach (var asset in assets)
                            {
                                var assetObj = asset.GetObject();
                                var name = assetObj.GetNamedString("name", "");
                                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    remoteZipUrl = assetObj.GetNamedString("browser_download_url", "");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Startup update check: remote check failed: {ex.Message}");
                }

                // Probe the helper's local AppPackages folder for a newer debug build so the
                // developer iteration loop (build → install) surfaces an update banner without
                // having to click "Check for Update (Debug)". Silently skipped if the helper
                // isn't connected yet.
                string localVersionStr = null;
                string localMsixPath = null;
                string localFolderName = null;
                try
                {
                    if (App.IsConnected)
                    {
                        var localMsg = new Windows.Foundation.Collections.ValueSet
                        {
                            { "Command", (int)Shared.Enums.Command.Get },
                            { "Function", (int)Shared.Enums.Function.CheckLocalUpdate },
                        };
                        var localResult = await App.SendMessageAsync(localMsg);
                        if (localResult != null
                            && !localResult.ContainsKey("Error")
                            && localResult.TryGetValue("LatestVersion", out object lvObj)
                            && localResult.TryGetValue("MsixbundlePath", out object lpObj))
                        {
                            localVersionStr = lvObj?.ToString();
                            localMsixPath = lpObj?.ToString();
                            localFolderName = localResult.TryGetValue("FolderName", out object lfObj) ? lfObj?.ToString() : "";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Startup update check: local debug probe failed: {ex.Message}");
                }

                Logger.Info($"Startup update check: current={currentVersion}, remote={remoteVersion ?? "(n/a)"}, local={(localVersionStr != null ? "v" + localVersionStr : "(n/a)")}");

                // Pick whichever source is newer than current and newer than the other source.
                // Local debug wins ties against remote — the developer who has a fresher build
                // in AppPackages almost always wants to test that first.
                bool remoteIsNewer = !string.IsNullOrEmpty(remoteVersion) && IsNewerVersion(remoteVersion, currentVersion);
                bool localIsNewer = !string.IsNullOrEmpty(localVersionStr)
                    && Version.TryParse(localVersionStr, out var localParsed)
                    && localParsed > new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);

                // Tie-breaking on equal versions: prefer local. IsNewerVersion returns false
                // for equal versions, so we use !IsNewerVersion(remote, local) which is
                // true when remote ≤ local (i.e., local wins on ties).
                bool preferLocal = localIsNewer && (!remoteIsNewer
                    || string.IsNullOrEmpty(remoteVersion)
                    || !IsNewerVersion(remoteVersion, "v" + localVersionStr));

                if (preferLocal)
                {
                    var localBannerVersion = $"v{localVersionStr} [Debug]";
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        _pendingUpdateZipUrl = localMsixPath; // local .msixbundle
                        _pendingUpdateVersion = localBannerVersion;
                        ShowUpdateBanner(localBannerVersion);
                    });
                    Logger.Info($"Update available (local debug): {localBannerVersion}, folder={localFolderName}, path={localMsixPath}");
                }
                else if (remoteIsNewer)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        _pendingUpdateZipUrl = remoteZipUrl;
                        _pendingUpdateVersion = remoteVersion;
                        ShowUpdateBanner(remoteVersion);
                    });
                    Logger.Info($"Update available (remote): {remoteVersion}, zip URL: {remoteZipUrl ?? "not found"}");
                }
                else
                {
                    Logger.Info("No update available");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check for updates on startup: {ex.Message}");
                // Silently fail - don't show error to user for automatic check
            }
        }

        /// <summary>
        /// Shows the update available banner with the new version.
        /// </summary>
        private void ShowUpdateBanner(string newVersion)
        {
            if (UpdateAvailableBanner != null && UpdateAvailableText != null)
            {
                UpdateAvailableText.Text = $"Update Available: {newVersion}";
                UpdateAvailableBanner.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Hides the update available banner.
        /// </summary>
        private void HideUpdateBanner()
        {
            if (UpdateAvailableBanner != null)
            {
                UpdateAvailableBanner.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Handles the Update button click on the update banner.
        /// </summary>
        private async void UpdateBannerButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingUpdateZipUrl))
            {
                Logger.Warn("Update banner clicked but no pending update URL");
                return;
            }

            try
            {
                UpdateBannerButton.IsEnabled = false;
                UpdateBannerButton.Content = "Updating...";

                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("Command", (int)Shared.Enums.Command.Set);
                    message.Add("Function", (int)Shared.Enums.Function.InstallUpdate);
                    message.Add("Content", _pendingUpdateZipUrl);
                    var result = await App.SendMessageAsync(message);

                    if (result != null && result.TryGetValue("UpdateStatus", out object status))
                    {
                        var statusStr = status?.ToString() ?? "";
                        if (statusStr == "Installing")
                        {
                            UpdateBannerButton.Content = "Installing...";
                            Logger.Info("Update installation started from banner");
                        }
                        else if (statusStr.StartsWith("Error"))
                        {
                            Logger.Error($"Update failed: {statusStr}");
                            UpdateBannerButton.Content = "Failed";
                            await Task.Delay(2000);
                            UpdateBannerButton.Content = "Update";
                            UpdateBannerButton.IsEnabled = true;
                        }
                    }
                    else
                    {
                        UpdateBannerButton.Content = "Failed";
                        await Task.Delay(2000);
                        UpdateBannerButton.Content = "Update";
                        UpdateBannerButton.IsEnabled = true;
                    }
                }
                else
                {
                    Logger.Warn("Helper not connected for update");
                    UpdateBannerButton.Content = "No Helper";
                    await Task.Delay(2000);
                    UpdateBannerButton.Content = "Update";
                    UpdateBannerButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start update from banner: {ex.Message}");
                UpdateBannerButton.Content = "Update";
                UpdateBannerButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Handles the dismiss button click on the update banner.
        /// </summary>
        private void DismissUpdateBannerButton_Click(object sender, RoutedEventArgs e)
        {
            HideUpdateBanner();
        }

        /// <summary>
        /// Handles the auto-update check toggle change.
        /// </summary>
        private void AutoUpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AutoUpdateCheckToggle == null)
                return;

            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            settings.Values["AutoUpdateCheckEnabled"] = AutoUpdateCheckToggle.IsOn;
            Logger.Info($"Auto-update check setting changed to: {AutoUpdateCheckToggle.IsOn}");
        }


        private async void ExportDGPsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportDGPsButton.IsEnabled = false;
                ExportDGPsButton.Content = "Exporting...";

                if (!App.IsConnected)
                {
                    ExportDGPsButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    ExportDGPsButton.Content = "Export DGPs (Desktop)";
                    ExportDGPsButton.IsEnabled = true;
                    return;
                }

                // Send request to helper to export DGPs
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.Debug_ExportDGPs);
                var result = await App.SendMessageAsync(message);

                if (result != null)
                {
                    if (result.TryGetValue("ExportPath", out object pathObj))
                    {
                        ExportDGPsButton.Content = $"Exported!";
                        Logger.Info($"DGPs exported to: {pathObj}");
                    }
                    else if (result.TryGetValue("Error", out object errorObj))
                    {
                        ExportDGPsButton.Content = $"Error: {errorObj}";
                    }
                }
                else
                {
                    ExportDGPsButton.Content = "Failed";
                }

                await Task.Delay(2000);
                ExportDGPsButton.Content = "Export DGPs (Desktop)";
                ExportDGPsButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export DGPs: {ex.Message}");
                ExportDGPsButton.Content = "Export DGPs (Desktop)";
                ExportDGPsButton.IsEnabled = true;
            }
        }

        private async void ExportAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportAllDataButton.IsEnabled = false;
                ExportAllDataButton.Content = "Exporting...";

                if (!App.IsConnected)
                {
                    ExportAllDataButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    ExportAllDataButton.Content = "Export All Data";
                    ExportAllDataButton.IsEnabled = true;
                    return;
                }

                // Gather widget LocalSettings to include in export
                string widgetSettingsJson = GatherWidgetSettingsForExport();

                // Send request to helper to export all data
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.ExportAllData);
                message.Add("Content", widgetSettingsJson);
                var result = await App.SendMessageAsync(message);

                if (result != null && result.TryGetValue("Content", out object contentObj))
                {
                    string resultText = contentObj?.ToString() ?? "";
                    if (resultText.StartsWith("Error:"))
                    {
                        ExportAllDataButton.Content = "Failed";
                        Logger.Error($"Export failed: {resultText}");
                    }
                    else
                    {
                        ExportAllDataButton.Content = "Exported!";
                        Logger.Info($"All data exported to: {resultText}");
                    }
                }
                else
                {
                    ExportAllDataButton.Content = "Failed";
                }

                await Task.Delay(2000);
                ExportAllDataButton.Content = "Export All Data";
                ExportAllDataButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export all data: {ex.Message}");
                ExportAllDataButton.Content = "Export All Data";
                ExportAllDataButton.IsEnabled = true;
            }
        }

        private async void ImportAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open folder picker to select backup folder
                var folderPicker = new Windows.Storage.Pickers.FolderPicker();
                folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                folderPicker.FileTypeFilter.Add("*");

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder == null)
                    return; // User cancelled

                // Check if this looks like a valid backup folder
                var manifestFile = await folder.TryGetItemAsync("manifest.json");
                if (manifestFile == null)
                {
                    var warningDialog = new Windows.UI.Popups.MessageDialog(
                        "The selected folder doesn't appear to be a valid GoTweaks backup.\n\n" +
                        "Please select a folder created by 'Export All Data' (e.g., GoTweaks_Backup_2024-...).",
                        "Invalid Backup Folder");
                    await warningDialog.ShowAsync();
                    return;
                }

                // Show confirmation dialog
                var dialog = new Windows.UI.Popups.MessageDialog(
                    $"Import data from:\n{folder.Name}\n\n" +
                    "This will:\n" +
                    "• Import all per-game profiles\n" +
                    "• Import global settings\n" +
                    "• Import AutoTDP Q-learning model\n" +
                    "• Import helper settings\n" +
                    "• Apply widget settings\n\n" +
                    "Existing data will be overwritten. Continue?",
                    "Import All Data");

                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Import"));
                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Cancel"));
                dialog.DefaultCommandIndex = 1;
                dialog.CancelCommandIndex = 1;

                var confirmResult = await dialog.ShowAsync();
                if (confirmResult.Label == "Cancel")
                    return;

                ImportAllDataButton.IsEnabled = false;
                ImportAllDataButton.Content = "Importing...";

                if (!App.IsConnected)
                {
                    ImportAllDataButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    ImportAllDataButton.Content = "Import All Data";
                    ImportAllDataButton.IsEnabled = true;
                    return;
                }

                // Send request to helper to import all data
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.ImportAllData);
                message.Add("Content", folder.Path);
                var result = await App.SendMessageAsync(message);

                if (result != null && result.TryGetValue("Content", out object contentObj))
                {
                    string summary = contentObj?.ToString() ?? "Import completed";

                    // Check if widget settings were returned
                    if (result.TryGetValue("WidgetSettings", out object widgetSettingsObj))
                    {
                        string widgetSettingsJson = widgetSettingsObj?.ToString();
                        if (!string.IsNullOrEmpty(widgetSettingsJson))
                        {
                            ApplyImportedWidgetSettings(widgetSettingsJson);
                            summary += "\n\nWidget settings have been applied.";
                        }
                    }

                    // Show result dialog
                    var resultDialog = new Windows.UI.Popups.MessageDialog(summary, "Import Complete");
                    await resultDialog.ShowAsync();

                    ImportAllDataButton.Content = "Imported!";
                }
                else
                {
                    ImportAllDataButton.Content = "Failed";
                }

                await Task.Delay(2000);
                ImportAllDataButton.Content = "Import All Data";
                ImportAllDataButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to import all data: {ex.Message}");
                ImportAllDataButton.Content = "Import All Data";
                ImportAllDataButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Gathers widget LocalSettings as JSON for export.
        /// </summary>
        private string GatherWidgetSettingsForExport()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var jsonObj = new Windows.Data.Json.JsonObject();

                // Export all known settings keys
                var keysToExport = new[]
                {
                    // AutoTDP settings
                    "AutoTDPEnabled", "AutoTDPTargetFPS", "AutoTDPMinTDP", "AutoTDPMaxTDP",
                    "AutoTDPUseMLMode", "AutoTDPPauseWhenUnfocused",
                    // TDP Boost settings
                    "TDPBoostEnabled", "TDPBoostSPPT", "TDPBoostFPPT",
                    // OSD settings
                    "OSDConfig", "OLEDConfig",
                    // Profile settings
                    "ProfileMatchByExe", "ProfileGamesOnly", "ProfileCustomGamePath", "ProfileBlacklistPaths",
                    // Legion settings
                    "LegionL_Action", "LegionL_Shortcut", "LegionL_Command",
                    "LegionR_Action", "LegionR_Shortcut", "LegionR_Command",
                    "LegionTouchpadVibration", "LegionDesktopControls",
                    // Controller hotkey settings
                    "ControllerHotkeyConfig",
                    // Display settings
                    "RefreshRateProfile",
                    // Other settings
                    "TdpMethod", "ForceDefaultGameProfile"
                };

                foreach (var key in keysToExport)
                {
                    if (settings.Values.ContainsKey(key))
                    {
                        var value = settings.Values[key];
                        if (value is bool boolVal)
                            jsonObj[key] = Windows.Data.Json.JsonValue.CreateBooleanValue(boolVal);
                        else if (value is int intVal)
                            jsonObj[key] = Windows.Data.Json.JsonValue.CreateNumberValue(intVal);
                        else if (value is double doubleVal)
                            jsonObj[key] = Windows.Data.Json.JsonValue.CreateNumberValue(doubleVal);
                        else if (value is string strVal)
                            jsonObj[key] = Windows.Data.Json.JsonValue.CreateStringValue(strVal);
                    }
                }

                return jsonObj.Stringify();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to gather widget settings for export: {ex.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// Applies imported widget settings from JSON.
        /// </summary>
        private void ApplyImportedWidgetSettings(string json)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                if (!Windows.Data.Json.JsonObject.TryParse(json, out Windows.Data.Json.JsonObject jsonObj))
                {
                    Logger.Error("Failed to parse imported widget settings JSON");
                    return;
                }

                int importedCount = 0;
                foreach (var key in jsonObj.Keys)
                {
                    try
                    {
                        var jsonValue = jsonObj[key];
                        object value = null;

                        switch (jsonValue.ValueType)
                        {
                            case Windows.Data.Json.JsonValueType.Boolean:
                                value = jsonValue.GetBoolean();
                                break;
                            case Windows.Data.Json.JsonValueType.Number:
                                // Try to preserve int vs double
                                double numVal = jsonValue.GetNumber();
                                if (numVal == Math.Floor(numVal) && numVal >= int.MinValue && numVal <= int.MaxValue)
                                    value = (int)numVal;
                                else
                                    value = numVal;
                                break;
                            case Windows.Data.Json.JsonValueType.String:
                                value = jsonValue.GetString();
                                break;
                        }

                        if (value != null)
                        {
                            settings.Values[key] = value;
                            importedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to import setting '{key}': {ex.Message}");
                    }
                }

                Logger.Info($"Applied {importedCount} widget settings from import");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply imported widget settings: {ex.Message}");
            }
        }

        private async void PrepareForUninstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show confirmation dialog
                var dialog = new Windows.UI.Popups.MessageDialog(
                    "This will:\n\n" +
                    "• Remove the scheduled task\n" +
                    "• Restore original CPU Boost settings\n" +
                    "• Restore original EPP settings\n" +
                    "• Re-enable Legion Space service (if disabled)\n\n" +
                    "After this, you can safely uninstall the app.",
                    "Prepare for Uninstall");

                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Continue"));
                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Cancel"));
                dialog.DefaultCommandIndex = 1;
                dialog.CancelCommandIndex = 1;

                var result = await dialog.ShowAsync();
                if (result.Label == "Cancel")
                    return;

                PrepareForUninstallButton.IsEnabled = false;
                PrepareForUninstallButton.Content = "Restoring...";

                if (!App.IsConnected)
                {
                    PrepareForUninstallButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    PrepareForUninstallButton.Content = "Prepare for Uninstall";
                    PrepareForUninstallButton.IsEnabled = true;
                    return;
                }

                // Send request to helper to prepare for uninstall
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.PrepareForUninstall);
                var response = await App.SendMessageAsync(message);

                if (response != null && response.TryGetValue("Content", out object contentObj))
                {
                    string resultText = contentObj?.ToString() ?? "Completed";
                    Logger.Info($"PrepareForUninstall result:\n{resultText}");

                    // Show result in a dialog
                    var resultDialog = new Windows.UI.Popups.MessageDialog(
                        resultText,
                        "Uninstall Preparation Complete");
                    await resultDialog.ShowAsync();

                    PrepareForUninstallButton.Content = "Done!";
                }
                else
                {
                    PrepareForUninstallButton.Content = "Failed";
                }

                await Task.Delay(2000);
                PrepareForUninstallButton.Content = "Prepare for Uninstall";
                PrepareForUninstallButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to prepare for uninstall: {ex.Message}");
                PrepareForUninstallButton.Content = "Prepare for Uninstall";
                PrepareForUninstallButton.IsEnabled = true;
            }
        }
    }
}
