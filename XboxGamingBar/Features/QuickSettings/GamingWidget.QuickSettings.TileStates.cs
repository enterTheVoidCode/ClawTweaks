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

        /// <summary>
        /// Rebuild tile grid with only visible tiles, in 3-column layout
        /// </summary>
        private void RebuildQuickSettingsTiles()
        {
            if (QuickSettingsTilesContainer == null) return;

            QuickSettingsTilesContainer.Children.Clear();

            // Get tiles to display - in edit mode show all (including hidden), otherwise only visible
            var tilesToShow = qsTileDefinitions
                .Where(t => !ShouldSkipTile(t) && (qsEditMode || t.IsVisible))
                .OrderBy(t => t.Order)
                .ToList();

            // Build rows of tiles (3 or 4 columns based on setting)
            Grid currentRow = null;
            int colIndex = 0;
            // Track the element occupying each logical column in the previous / current row, so a wide
            // tile's inner sliders can point XYFocusUp at the tile directly above (otherwise up-nav from
            // the top slider escapes all the way to the tab bar).
            var aboveByCol = new FrameworkElement[qsColumnCount];
            var thisRowByCol = new FrameworkElement[qsColumnCount];

            for (int i = 0; i < tilesToShow.Count; i++)
            {
                var tile = tilesToShow[i];
                // A wide tile spans 2 logical columns; keep them uniform so it never overflows.
                int span = (tile.IsWide && qsColumnCount >= 2) ? 2 : 1;

                // Start a new row when at the start, or when a wide tile wouldn't fit the remaining columns.
                if (colIndex == 0 || colIndex + span > qsColumnCount)
                {
                    colIndex = 0;
                    aboveByCol = thisRowByCol;
                    thisRowByCol = new FrameworkElement[qsColumnCount];
                    currentRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    // Add column definitions dynamically based on qsColumnCount
                    for (int c = 0; c < qsColumnCount; c++)
                    {
                        if (c > 0) currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });  // Spacer
                        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }
                    QuickSettingsTilesContainer.Children.Add(currentRow);
                }

                var tileButton = CreateTileButton(tile);
                Grid.SetColumn(tileButton, colIndex * 2);
                // 2 logical columns = tile col + spacer + tile col = 3 physical grid columns.
                if (span > 1) Grid.SetColumnSpan(tileButton, span * 2 - 1);
                currentRow.Children.Add(tileButton);
                for (int c = colIndex; c < colIndex + span && c < qsColumnCount; c++)
                    thisRowByCol[c] = tileButton;

                // Media tile: point the top (brightness) slider's up-focus at the tile directly above.
                if (tile.ActionType == TileActionType.MediaSliders && _mediaBrightnessSlider != null
                    && colIndex < aboveByCol.Length && aboveByCol[colIndex] != null)
                {
                    _mediaBrightnessSlider.XYFocusUp = aboveByCol[colIndex];
                }

                colIndex += span;
                if (colIndex >= qsColumnCount)
                {
                    colIndex = 0;
                }
            }

            // Apply the global glass-effect state to the freshly built tiles. The per-button
            // Loaded handler is unreliable for runtime rebuilds (when the Quick tab is collapsed
            // the template parts aren't realised yet, so Loaded no-ops and the tiles keep the
            // template default = glass visible). Re-applying after layout fixes both directions.
            ApplyGlassEffectToTilesDeferred();
        }

        /// <summary>
        /// Sets the Shimmer + GlassSheen visibility on every built tile to match the global glass
        /// toggle. Walks the realised control template parts, so it must run after layout.
        /// </summary>
        internal void ApplyGlassEffectToTiles()
        {
            try
            {
                var vis = IsGlassEffectEnabled() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var t in qsTileDefinitions)
                {
                    var btn = t?.TileButton;
                    if (btn == null) continue;
                    if (FindDescendantByName(btn, "Shimmer")   is FrameworkElement shim)  shim.Visibility  = vis;
                    if (FindDescendantByName(btn, "GlassSheen") is FrameworkElement sheen) sheen.Visibility = vis;
                }
            }
            catch (Exception ex) { Logger.Warn($"ApplyGlassEffectToTiles: {ex.Message}"); }
        }

        /// <summary>Re-applies glass visibility after the current layout pass (template parts realised).</summary>
        internal void ApplyGlassEffectToTilesDeferred()
        {
            try { _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, ApplyGlassEffectToTiles); }
            catch { }
        }

        /// <summary>
        /// Create a tile button for the given definition
        /// </summary>
        // Media-slider tile (2 cells wide): brightness on top, volume on bottom, 5-step.
        private Slider _mediaBrightnessSlider;
        private Slider _mediaVolumeSlider;
        private TextBlock _mediaBrightnessValue;
        private TextBlock _mediaVolumeValue;
        // True while we're pushing helper-reported levels into the sliders, so their
        // ValueChanged handlers don't echo the value straight back to the helper.
        private bool _mediaSlidersUpdating;

        private Brush MediaAccentBrush()
        {
            if (this.Resources.TryGetValue("ThemeAccentBrush", out var ab) && ab is Brush b1) return b1;
            if (Application.Current.Resources.TryGetValue("ThemeAccentBrush", out var ab2) && ab2 is Brush b2) return b2;
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 138, 180, 248));
        }

        private FrameworkElement CreateMediaSliderTile(TileDefinition tile)
        {
            var accent = MediaAccentBrush();

            Slider MakeSlider()
            {
                return new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    StepFrequency = 5,
                    SmallChange = 5,
                    LargeChange = 10,
                    TickFrequency = 5,
                    Value = 50,
                    Style = Resources["ModernSliderStyle"] as Style,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var brightnessSlider = MakeSlider();
            var volumeSlider = MakeSlider();
            _mediaBrightnessSlider = brightnessSlider;
            _mediaVolumeSlider = volumeSlider;

            var brightnessValue = new TextBlock { Text = "50%" };
            var volumeValue = new TextBlock { Text = "50%" };
            _mediaBrightnessValue = brightnessValue;
            _mediaVolumeValue = volumeValue;

            // Keep vertical focus INSIDE the tile: down = brightness->volume, up = volume->brightness.
            // Without the explicit up link, up-nav from the volume slider escaped to the tabs.
            brightnessSlider.XYFocusDown = volumeSlider;
            volumeSlider.XYFocusUp = brightnessSlider;

            brightnessSlider.ValueChanged += (s, e) =>
            {
                int val = (int)Math.Round(e.NewValue);
                brightnessValue.Text = val + "%";
                if (_mediaSlidersUpdating) return;
                _ = SendMediaLevelAsync("SetBrightnessLevel", val);
            };
            volumeSlider.ValueChanged += (s, e) =>
            {
                int val = (int)Math.Round(e.NewValue);
                volumeValue.Text = val + "%";
                if (_mediaSlidersUpdating) return;
                _ = SendMediaLevelAsync("SetVolumeLevel", val);
            };

            StackPanel Row(string glyph, Slider slider, TextBlock valueLabel)
            {
                var header = new Grid { Margin = new Thickness(2, 0, 2, 0) };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var icon = new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 16,
                    Foreground = accent,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Grid.SetColumn(icon, 0);

                valueLabel.Foreground = accent;
                valueLabel.FontSize = 13;
                valueLabel.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
                valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
                valueLabel.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(valueLabel, 1);

                header.Children.Add(icon);
                header.Children.Add(valueLabel);

                var row = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                row.Children.Add(header);
                row.Children.Add(slider);
                return row;
            }

            var content = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
            content.Children.Add(Row("", brightnessSlider, brightnessValue)); // brightness (top)
            content.Children.Add(Row("", volumeSlider, volumeValue));         // volume (bottom)

            var bg = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
            bg.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 42, 48, 74), Offset = 0 });
            bg.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 30, 34, 54), Offset = 1 });

            var border = new Border
            {
                Tag = tile.Id,
                Background = bg,
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = content
            };

            // Pull the current brightness/volume once the tile exists.
            _ = RefreshMediaSliderLevelsAsync();
            return border;
        }
        // Like RefreshMediaSliderLevelsAsync, but tolerates being called before the pipe has (re)connected
        // — e.g. from VisibleChanged on Game Bar open, which can run before the helper pipe is back up.
        // Retries briefly until connected so external brightness/volume changes are still picked up.
        internal async System.Threading.Tasks.Task RefreshMediaSliderLevelsSoonAsync()
        {
            if (_mediaBrightnessSlider == null && _mediaVolumeSlider == null) return;
            for (int i = 0; i < 8 && !App.IsConnected; i++)
                await System.Threading.Tasks.Task.Delay(250);
            await RefreshMediaSliderLevelsAsync();
        }

        // Fetches current brightness + volume from the helper and pushes them into the sliders
        // (guarded so it doesn't echo back). Safe to call whenever the Quick tab is shown.
        internal async System.Threading.Tasks.Task RefreshMediaSliderLevelsAsync()
        {
            if (_mediaBrightnessSlider == null && _mediaVolumeSlider == null) return;
            if (!App.IsConnected) return;
            try
            {
                var request = new Windows.Foundation.Collections.ValueSet { { "GetMediaLevels", "1" } };
                var response = await App.SendMessageAsync(request);
                if (response == null || !response.TryGetValue("MediaLevels", out var payloadObj)
                    || !(payloadObj is string payload)
                    || !Windows.Data.Json.JsonObject.TryParse(payload, out var root))
                    return;

                double bright = root.TryGetValue("brightness", out var b) && b.ValueType == Windows.Data.Json.JsonValueType.Number ? b.GetNumber() : 50;
                double vol = root.TryGetValue("volume", out var v) && v.ValueType == Windows.Data.Json.JsonValueType.Number ? v.GetNumber() : 50;

                // Marshal the slider/label writes to the UI thread. The main trigger for this refresh is
                // the Game-Bar-open foreground edge (UpdateGameBarForegroundSignal), which the Game Bar
                // raises on a BACKGROUND thread — writing UI there throws RPC_E_WRONG_THREAD and the
                // catch below swallowed it, so the sliders silently never updated. Same background-thread
                // trap ApplyDefaultTabOnOpen documents and works around.
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    _mediaSlidersUpdating = true;
                    try
                    {
                        if (_mediaBrightnessSlider != null) _mediaBrightnessSlider.Value = bright;
                        if (_mediaVolumeSlider != null) _mediaVolumeSlider.Value = vol;
                        if (_mediaBrightnessValue != null) _mediaBrightnessValue.Text = (int)Math.Round(bright) + "%";
                        if (_mediaVolumeValue != null) _mediaVolumeValue.Text = (int)Math.Round(vol) + "%";
                    }
                    finally { _mediaSlidersUpdating = false; }
                });
            }
            catch (Exception ex) { Logger.Warn($"RefreshMediaSliderLevels failed: {ex.Message}"); }
        }

        private async System.Threading.Tasks.Task SendMediaLevelAsync(string key, int level)
        {
            if (!App.IsConnected) return;
            try
            {
                var request = new Windows.Foundation.Collections.ValueSet { { key, level } };
                await App.SendMessageAsync(request);
            }
            catch (Exception ex) { Logger.Warn($"SendMediaLevel {key} failed: {ex.Message}"); }
        }

        private FrameworkElement CreateTileButton(TileDefinition tile)
        {
            // Wide media tile renders sliders instead of a button.
            if (tile.ActionType == TileActionType.MediaSliders)
                return CreateMediaSliderTile(tile);

            // Action tiles get a distinct background color
            var bgBrush = tile.IsAction
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 32, 48))  // Dark purple for action tiles
                : tileOffBrush;

            var button = new Button
            {
                Tag = tile.Id,
                Style = Resources["QuickSettingsTileStyle"] as Style,
                Background = bgBrush,
                // Buttons pin FontFamily to the theme resource (Segoe UI) via their default style,
                // so tile text would NOT inherit the app-wide page font. Set it explicitly to the
                // chosen app font (carried on the page root); the icon glyph keeps its own font.
                FontFamily = this.FontFamily,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                // Override QuickSettingsTileStyle's Center default so the ContentPresenter
                // actually hands the inner StackPanel the full tile width (minus the style's
                // 12,16 padding). Without this, Center alignment sizes the ContentPresenter to
                // the StackPanel's DesiredSize — which is the widest child (the Center-aligned
                // label), leaving the state-text Canvas squeezed and the marquee scrolling in
                // a narrow strip while the tile itself looks mostly empty.
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // Gate the tile shimmer by the active theme (ShimmerEnabled). The "Shimmer" rectangle
            // is a template part, only realised once the button is in the tree — set its visibility
            // on Loaded. Tiles are rebuilt on theme change, so this re-evaluates per theme.
            button.Loaded += (s, e) =>
            {
                try
                {
                    // Glass look is a global, theme-independent, user toggle (default on).
                    var vis = IsGlassEffectEnabled() ? Visibility.Visible : Visibility.Collapsed;
                    if (FindDescendantByName(button, "Shimmer")   is FrameworkElement shim)  shim.Visibility  = vis;
                    if (FindDescendantByName(button, "GlassSheen") is FrameworkElement sheen) sheen.Visibility = vis;
                }
                catch { }
            };

            // Stretch so the state-text Canvas below gets the full tile width to scroll in
            // (otherwise the StackPanel sizes to its widest child — usually the centered label —
            // and marquees scroll in a narrow strip). Icon and label stay Center-aligned per-child
            // so the tile still looks centered.
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

            content.Children.Add(BuildTileIconElement(tile.ActionType, tile.Id, tile.Glyph, 28, null));

            content.Children.Add(new TextBlock
            {
                Text = tile.Name,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });

            // Action tiles show "Action" instead of state
            var stateText = new TextBlock
            {
                Text = tile.IsAction ? "Action" : "Off",
                FontSize = 13,
                Foreground = tile.IsAction
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 150, 200))  // Light purple for action
                    : tileTextBrush,  // theme secondary (near-white) instead of dim grey
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                TextWrapping = TextWrapping.NoWrap
            };

            // Wrap every tile's state text in a Canvas that clips to the tile's
            // available width and supports marquee scrolling when the text overflows.
            // Width binds to the parent Grid column via HorizontalAlignment=Stretch +
            // SizeChanged so the clip geometry tracks column resizes (3/4/5 cols).
            var transform = new TranslateTransform { X = 0 };
            stateText.RenderTransform = transform;

            var canvas = new Canvas
            {
                Height = 18,
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, 18) };
            canvas.Children.Add(stateText);

            // Keep the clip rect in sync with the actual laid-out width so scroll
            // calculations reflect the real tile width at 3/4/5 column settings.
            canvas.SizeChanged += (s, e) =>
            {
                if (e.NewSize.Width > 0)
                {
                    canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, 18) };
                    // Re-evaluate scroll state now that the available width changed
                    UpdateTileScrollAnimation(tile);
                }
            };

            content.Children.Add(canvas);

            tile.StateTextCanvas = canvas;
            tile.StateTextTransform = transform;

            // Controller-hotkey indicator: a small teal dot in the top-right corner,
            // shown when this tile has a controller combo assigned via Customize Tiles.
            // The teal matches the bound-combo color used in the Customize Tiles panel.
            // A tooltip reveals the concrete combo after the focus dwell delay.
            bool hasControllerHotkey = !string.IsNullOrEmpty(tile.ControllerHotkey);
            if (hasControllerHotkey)
            {
                var overlay = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                overlay.Children.Add(content);

                var dot = new Windows.UI.Xaml.Shapes.Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 220, 200)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -4, -2, 0)
                };
                overlay.Children.Add(dot);
                button.Content = overlay;

                // Tooltip shows the concrete combo when focus/hover dwells on the tile.
                string comboText = XInputMaskToDisplayString(ParseHotkeyMaskUInt(tile.ControllerHotkey));
                ToolTipService.SetToolTip(button, new ToolTip { Content = $"Hotkey: {comboText}" });
            }
            else
            {
                // All tiles: main content goes directly into the button (no nested split)
                button.Content = content;
            }
            button.Click += QuickSettingsTile_Click;
            tile.TileButton = button;
            tile.StateText = stateText;

            if (tile.HasDropdown)
            {
                // Split tile: main tile button (Row 0) + standalone chevron button (Row 1) as siblings
                // in a wrapper Grid. Previously the chevron was nested inside the outer Button content,
                // which UWP's XY gamepad focus treats as a single unit — the inner button was unreachable
                // with D-pad. As separate siblings both buttons are independently focusable.
                var wrapper = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                wrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                wrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

                Grid.SetRow(button, 0);
                wrapper.Children.Add(button);

                // Chevron button — standalone sibling button (independently focusable via D-pad Down)
                var dropBtn = new Button
                {
                    Tag = tile.Id + "_dropdown",
                    Content = new FontIcon { Glyph = "", FontSize = 10 }, // Chevron down
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 2, 0, 0),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 80, 85, 92)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    UseSystemFocusVisuals = true
                };
                Grid.SetRow(dropBtn, 1);
                wrapper.Children.Add(dropBtn);
                dropBtn.Click += TileDropdown_Click;
                tile.DropdownButton = dropBtn;

                // FPSCombined: bottom button is a mode-switch, not a value dropdown — relabel it
                if (tile.Id == "FPSCombined")
                    dropBtn.Content = new TextBlock
                    {
                        Text = "Mode",
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                return wrapper;
            }

            if (tile.HasDualButtons)
            {
                // Triple split tile: main button (Row 0) + two side-by-side sub-buttons (Row 1).
                // Left button = mode switch (RTSS ↔ Intel), Right button = value cycle.
                // Both sub-buttons are independently focusable via D-pad (UWP XY navigation).
                var wrapper = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                wrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                wrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

                Grid.SetRow(button, 0);
                wrapper.Children.Add(button);

                // Sub-button row: left | 4px spacer | right
                var subRow = new Grid
                {
                    Margin = new Thickness(0, 2, 0, 0),
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                subRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                subRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                subRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var subBtnStyle = new Style { TargetType = typeof(Button) };
                var subBtnBg = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255));
                var subBtnBorder = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 80, 85, 92));

                // Left button: "Mode" label — switches RTSS ↔ Intel
                var leftBtnContent = new TextBlock
                {
                    Text = "Mode",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                var leftBtn = new Button
                {
                    Tag = tile.Id + "_left",
                    Content = leftBtnContent,
                    Padding = new Thickness(0),
                    Background = subBtnBg,
                    BorderBrush = subBtnBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    UseSystemFocusVisuals = true
                };

                var rightBtn = new Button
                {
                    Tag = tile.Id + "_right",
                    Content = new FontIcon { Glyph = "", FontSize = 11 },  // ChevronDown ↓
                    Padding = new Thickness(0),
                    Background = subBtnBg,
                    BorderBrush = subBtnBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    UseSystemFocusVisuals = true
                };

                Grid.SetColumn(leftBtn, 0);
                Grid.SetColumn(rightBtn, 2);
                subRow.Children.Add(leftBtn);
                subRow.Children.Add(rightBtn);

                leftBtn.Click += TileLeftButton_Click;
                rightBtn.Click += TileRightButton_Click;
                tile.LeftButton = leftBtn;
                tile.RightButton = rightBtn;

                Grid.SetRow(subRow, 1);
                wrapper.Children.Add(subRow);

                return wrapper;
            }


            return button;
        }

        /// <summary>
        /// Updates the scroll animation for a tile's state text.
        /// If the rendered text is wider than the tile's column, marquees it
        /// left-right-left on a loop so the full value is readable. Otherwise
        /// centers it. Safe to call repeatedly — it stops any existing storyboard
        /// before starting a new one. Replaces the old Profile-only variant now
        /// that every tile gets the same scrolling treatment.
        /// </summary>
        private void UpdateTileScrollAnimation(TileDefinition tile)
        {
            if (tile?.StateText == null || tile.StateTextCanvas == null || tile.StateTextTransform == null)
                return;

            // Stop any existing animation
            if (tile.ScrollStoryboard != null)
            {
                tile.ScrollStoryboard.Stop();
                tile.ScrollStoryboard = null;
            }

            // Reset transform
            tile.StateTextTransform.X = 0;

            // Measure text width at its natural size
            tile.StateText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = tile.StateText.DesiredSize.Width;

            // Prefer the actual laid-out width; fall back to the declared Width
            // when the canvas hasn't had its first layout pass yet. The declared
            // Width defaults to NaN in XAML, and `NaN <= 0` is false, so guard
            // against NaN/Infinity explicitly — otherwise it propagates into
            // TimeSpan.FromSeconds below and throws.
            double canvasWidth = tile.StateTextCanvas.ActualWidth;
            if (!(canvasWidth > 0)) canvasWidth = tile.StateTextCanvas.Width;
            if (!(canvasWidth > 0)) return; // Not yet laid out — SizeChanged will retry
            if (!(textWidth >= 0) || double.IsInfinity(textWidth)) return;

            // If text fits, no animation needed — just center it
            if (textWidth <= canvasWidth)
            {
                Canvas.SetLeft(tile.StateText, (canvasWidth - textWidth) / 2);
                return;
            }

            // Text is too wide - set up scrolling animation
            Canvas.SetLeft(tile.StateText, 0);

            // Calculate scroll distance and duration
            double scrollDistance = textWidth - canvasWidth + 10; // Extra padding
            double scrollSpeed = 30; // pixels per second
            double scrollDuration = scrollDistance / scrollSpeed;

            var storyboard = new Storyboard();
            var animation = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Pause at start
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 0
            });
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5)),
                Value = 0
            });

            // Scroll left
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration)),
                Value = -scrollDistance
            });

            // Pause at end
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + scrollDuration)),
                Value = -scrollDistance
            });

            // Scroll back right
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + scrollDuration * 2)),
                Value = 0
            });

            // Pause before repeat
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5 + scrollDuration * 2)),
                Value = 0
            });

            Storyboard.SetTarget(animation, tile.StateTextTransform);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);

            tile.ScrollStoryboard = storyboard;
            storyboard.Begin();
        }

        /// <summary>
        /// Update all Quick Settings tile states based on current property values
        /// </summary>
        private void UpdateQuickSettingsTileStates()
        {
            if (!quickSettingsInitialized)
            {
                InitializeQuickSettings();
            }

            try
            {
                var accentForeground = new SolidColorBrush(ThemeColors.Shade(CurrentThemeAccent(), 0.35));
                var offForeground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));

                // TDP Mode tile - color-coded backgrounds based on preset or mode
                if (qsTileMap.TryGetValue("TDPMode", out var tdpTile) && tdpTile.TileButton != null)
                {
                    bool isLegion = legionGoDetected?.Value == true;
                    int selectedIndex = TDPModeComboBox?.SelectedIndex ?? 0;
                    string modeText;
                    SolidColorBrush tdpModeBrush;

                    // Use custom presets if enabled
                    if (useCustomTDPPresets && tdpPresets != null && tdpPresets.Count > 0)
                    {
                        if (selectedIndex < tdpPresets.Count)
                        {
                            var preset = tdpPresets[selectedIndex];
                            modeText = $"{preset.Name} ({preset.TdpWatts}W)";

                            // Color based on LegionModeValue or default to purple for custom
                            switch (preset.LegionModeValue)
                            {
                                case 1: // Quiet - Desaturated Blue
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 45, 60));
                                    break;
                                case 2: // Balanced - Grey
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 55));
                                    break;
                                case 3: // Performance - Desaturated Red
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 40, 40));
                                    break;
                                default: // Custom preset (no LegionModeValue) - Purple
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 42, 58));
                                    break;
                            }
                        }
                        else
                        {
                            // Slider mode (last item, manual TDP slider control)
                            int currentTdp = (int)(TDPSlider?.Value ?? 15);
                            modeText = $"Slider ({currentTdp}W)";
                            tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 42, 58));
                        }
                    }
                    else
                    {
                        // Default hardcoded mode display
                        if (isLegion && legionPerformanceMode != null)
                        {
                            // Legion Go: use hardware mode value for display
                            int legionMode = legionPerformanceMode.Value;
                            switch (legionMode)
                            {
                                case 1:
                                    modeText = "Quiet";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 45, 60));
                                    break;
                                case 2:
                                    modeText = "Balanced";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 55));
                                    break;
                                case 3:
                                    modeText = "Performance";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 40, 40));
                                    break;
                                case 255:
                                    int legionCustomTdp = (int)(TDPSlider?.Value ?? 25);
                                    modeText = $"Slider ({legionCustomTdp}W)";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 42, 58));
                                    break;
                                default:
                                    modeText = "Balanced";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 55));
                                    break;
                            }
                        }
                        else
                        {
                            // MSI Claw / non-Legion: index-based display with watts always shown
                            // Index: 0=Max(30W), 1=Standard(25W), 2=Balanced(17W), 3=Battery(12W), 4=Super Battery(8W), 5+=Slider
                            switch (selectedIndex)
                            {
                                case 0: // Max - Deep red (max performance)
                                    modeText = "Max (30W)";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 65, 25, 25));
                                    break;
                                case 1: // Standard - Green-tinted (high performance)
                                    modeText = "Standard (25W)";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 55, 40));
                                    break;
                                case 2: // Balanced - Blue-grey (everyday)
                                    modeText = "Balanced (17W)";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 45, 60));
                                    break;
                                case 3: // Battery - Amber-brown (saving power)
                                    modeText = "Battery (12W)";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 45, 25));
                                    break;
                                case 4: // Super Battery - Deep teal (max saving)
                                    modeText = "Super Bat (8W)";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 50, 50));
                                    break;
                                default: // Slider
                                    int customTdp = (int)(TDPSlider?.Value ?? 25);
                                    modeText = $"Slider ({customTdp}W)";
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 42, 58));
                                    break;
                            }
                        }
                    }

                    tdpTile.StateText.Text = modeText;
                    tdpTile.StateText.Foreground = accentForeground;
                    tdpTile.TileButton.Background = tdpModeBrush;
                }

                // AutoTDP tile
                if (qsTileMap.TryGetValue("AutoTDP", out var autoTdpTile) && autoTdpTile.TileButton != null)
                {
                    bool enabled = AutoTDPToggle?.IsOn ?? false;
                    int targetFps = (int)(AutoTDPTargetFPSSlider?.Value ?? 60);
                    string stateText = enabled ? $"{targetFps} FPS" : "Off";
                    autoTdpTile.StateText.Text = stateText;
                    autoTdpTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    autoTdpTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Profile tile
                if (qsTileMap.TryGetValue("Profile", out var profileTile) && profileTile.TileButton != null)
                {
                    bool perGame = perGameProfile?.Value ?? false;
                    bool defaultProfileActive = defaultGameProfileEnabled?.Value ?? false;
                    string gameName = (runningGame != null && runningGame.Value.IsValid()) ? runningGame.Value.GameId.Name : "Per-Game";

                    // Show game name with gradient when default game profile is active
                    string profileName;
                    if (defaultProfileActive)
                    {
                        // Use game name from current profile or running game
                        profileName = currentDefaultGameProfile?.GameName ?? gameName;
                        profileTile.StateText.Text = profileName;
                        profileTile.StateText.Foreground = accentForeground;
                        profileTile.TileButton.Background = tileDefaultProfileBrush;
                    }
                    else
                    {
                        profileName = perGame ? gameName : "Global";
                        profileTile.StateText.Text = profileName;
                        profileTile.StateText.Foreground = perGame ? accentForeground : offForeground;
                        profileTile.TileButton.Background = perGame ? tileOnBrush : tileOffBrush;
                    }

                    // Update scroll animation for long profile names (per-tile loop
                    // below also runs this for every tile, but re-running it here
                    // keeps the profile tile responsive to name changes without
                    // waiting for the next tile-state refresh pass).
                    UpdateTileScrollAnimation(profileTile);
                }

                // Performance Overlay tile
                if (qsTileMap.TryGetValue("Overlay", out var overlayTile) && overlayTile.TileButton != null)
                {
                    if (osdProvider == 1) // AMD
                    {
                        string amdLevelText = amdOverlayLevel > 0 ? $"AMD {amdOverlayLevel}" : "Off";
                        overlayTile.StateText.Text = amdLevelText;
                        overlayTile.StateText.Foreground = amdOverlayLevel > 0 ? accentForeground : offForeground;
                        overlayTile.TileButton.Background = amdOverlayLevel > 0 ? tileOnBrush : tileOffBrush;
                    }
                    else // RTSS
                    {
                        int level = (int)(osd?.Value ?? 0);
                        string levelText;
                        switch (level)
                        {
                            case 0: levelText = "Off"; break;
                            case 1: levelText = "Basic"; break;
                            case 2: levelText = "Horizontal"; break;
                            case 3: levelText = "H.Detail"; break;
                            case 4: levelText = "Full"; break;
                            default: levelText = "Off"; break;
                        }
                        overlayTile.StateText.Text = levelText;
                        overlayTile.StateText.Foreground = level > 0 ? accentForeground : offForeground;
                        overlayTile.TileButton.Background = level > 0 ? tileOnBrush : tileOffBrush;
                    }
                }

                // Power Mode tile
                if (qsTileMap.TryGetValue("PowerMode", out var powerModeTile) && powerModeTile.TileButton != null)
                {
                    int mode = osPowerMode?.Value ?? 1;
                    string modeText;
                    switch (mode)
                    {
                        case 0: modeText = "Efficiency"; break;
                        case 1: modeText = "Balanced"; break;
                        case 2: modeText = "Performance"; break;
                        default: modeText = "Balanced"; break;
                    }
                    powerModeTile.StateText.Text = modeText;
                    powerModeTile.StateText.Foreground = mode != 1 ? accentForeground : offForeground;
                    powerModeTile.TileButton.Background = mode == 2 ? tileOnBrush : (mode == 0 ? tileActiveBrush : tileOffBrush);
                }

                // FPS Combined tile (RTSS + Intel merged)
                // State text: "{RTSS|Intel} · {value}"  — mode shown left, value right
                if (qsTileMap.TryGetValue("FPSCombined", out var fpsCombinedTile) && fpsCombinedTile.TileButton != null && fpsCombinedTile.StateText != null)
                {
                    bool isIntelMode = fpsCapMode?.Value == 1;
                    bool isActive;
                    string stateText;

                    if (isIntelMode)
                    {
                        int tier = intelFpsTier?.Value ?? 0;
                        string[] intelLabels = { "Off", "P60", "B40", "E30" };
                        string tierLabel = (tier >= 0 && tier < intelLabels.Length) ? intelLabels[tier] : "Off";
                        isActive = tier > 0;
                        stateText = $"Intel · {tierLabel}";
                    }
                    else
                    {
                        int limit = fpsLimit?.Value ?? 0;
                        string limitLabel = limit == 0 ? "Off" : $"{limit}";
                        isActive = limit > 0;
                        stateText = $"RTSS · {limitLabel}";
                    }

                    fpsCombinedTile.StateText.Text = stateText;
                    fpsCombinedTile.StateText.Foreground = isActive ? accentForeground : offForeground;
                    fpsCombinedTile.TileButton.Background = isActive ? tileOnBrush : tileOffBrush;
                    UpdateTileScrollAnimation(fpsCombinedTile);
                }

                // (FPSLimit + IntelFpsTier individual tiles replaced by FPSCombined above)
                // if (qsTileMap.TryGetValue("FPSLimit", ...))   { ... }
                // if (qsTileMap.TryGetValue("IntelFpsTier", ...)) { ... }

                // Resolution tile
                if (qsTileMap.TryGetValue("Resolution", out var resTile) && resTile.TileButton != null)
                {
                    string currentRes = resolution?.Value ?? "1920x1080";
                    resTile.StateText.Text = currentRes;
                    resTile.StateText.Foreground = accentForeground;
                    resTile.TileButton.Background = tileOffBrush;
                }

                // Rotation tile
                if (qsTileMap.TryGetValue("Rotation", out var rotationTile) && rotationTile.TileButton != null)
                {
                    string orientationText = displayOrientation?.GetOrientationText() ?? "Landscape";
                    bool isPortrait = (displayOrientation?.Value ?? 0) == 1 || (displayOrientation?.Value ?? 0) == 3;
                    rotationTile.StateText.Text = orientationText;
                    rotationTile.StateText.Foreground = isPortrait ? accentForeground : offForeground;
                    rotationTile.TileButton.Background = isPortrait ? tileOnBrush : tileOffBrush;
                }

                // HDR tile
                if (qsTileMap.TryGetValue("HDR", out var hdrTile) && hdrTile.TileButton != null)
                {
                    bool supported = hdrSupported?.Value ?? false;
                    bool enabled = hdrEnabled?.Value ?? false;
                    hdrTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    hdrTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    hdrTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Lossless Scaling tile
                if (qsTileMap.TryGetValue("LosslessScaling", out var lsTile) && lsTile.TileButton != null)
                {
                    bool enabled = losslessScalingEnabled?.Value ?? false;
                    lsTile.StateText.Text = enabled ? "On" : "Off";
                    lsTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    lsTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // RIS (Radeon Image Sharpening) tile
                if (qsTileMap.TryGetValue("RIS", out var risTile) && risTile.TileButton != null)
                {
                    bool supported = amdImageSharpeningSupported?.Value ?? false;
                    bool enabled = amdImageSharpeningEnabled?.Value ?? false;
                    risTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    risTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    risTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // AFMF tile
                if (qsTileMap.TryGetValue("AFMF", out var afmfTile) && afmfTile.TileButton != null)
                {
                    bool supported = amdFluidMotionFrameSupported?.Value ?? false;
                    bool enabled = amdFluidMotionFrameEnabled?.Value ?? false;
                    afmfTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    afmfTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    afmfTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // RSR tile
                if (qsTileMap.TryGetValue("RSR", out var rsrTile) && rsrTile.TileButton != null)
                {
                    bool supported = amdRadeonSuperResolutionSupported?.Value ?? false;
                    bool enabled = amdRadeonSuperResolutionEnabled?.Value ?? false;
                    rsrTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    rsrTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    rsrTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Anti-Lag tile
                if (qsTileMap.TryGetValue("AntiLag", out var antiLagTile) && antiLagTile.TileButton != null)
                {
                    bool supported = amdRadeonAntiLagSupported?.Value ?? false;
                    bool enabled = amdRadeonAntiLagEnabled?.Value ?? false;
                    antiLagTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    antiLagTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    antiLagTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Radeon Chill tile
                if (qsTileMap.TryGetValue("RadeonChill", out var chillTile) && chillTile.TileButton != null)
                {
                    bool supported = amdRadeonChillSupported?.Value ?? false;
                    bool enabled = amdRadeonChillEnabled?.Value ?? false;
                    chillTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    chillTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    chillTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // CPU Boost tile
                if (qsTileMap.TryGetValue("CPUBoost", out var boostTile) && boostTile.TileButton != null)
                {
                    bool enabled = cpuBoost?.Value ?? false;
                    boostTile.StateText.Text = enabled ? "On" : "Off";
                    boostTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    boostTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Power tile — always "power button" red. It opens a power menu (sleep/reboot/...),
                // it's not a toggle, so there is no on/off state to show.
                if (qsTileMap.TryGetValue("Power", out var powerTile) && powerTile.TileButton != null)
                {
                    powerTile.TileButton.Background = new Windows.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0x5E, 0xC4, 0x2B, 0x1C)); // subtle, mostly-transparent power red
                    if (powerTile.StateText != null) powerTile.StateText.Text = "";
                }

                // LED lighting tile — on when RGB brightness > 0. MSI Claw uses its own LED path.
                if (qsTileMap.TryGetValue("LedToggle", out var ledTile) && ledTile.TileButton != null)
                {
                    bool on;
                    if (IsMsiClawDevice())
                    {
                        on = IsMsiLedOn();
                    }
                    else
                    {
                        int brightness = legionLightBrightness?.Value ?? (int)(LegionBrightnessSlider?.Value ?? 0);
                        on = brightness > 0;
                    }
                    ledTile.StateText.Text = on ? "On" : "Off";
                    ledTile.StateText.Foreground = on ? accentForeground : offForeground;
                    ledTile.TileButton.Background = on ? tileOnBrush : tileOffBrush;
                }

                // DISABLED: FPS Limiter tile removed from the grid (see QuickSettings.cs). State block
                // kept commented so it can be restored if the tile is ever re-enabled.
                /*
                if (qsTileMap.TryGetValue("FpsLimiter", out var fpsTile) && fpsTile.TileButton != null)
                {
                    bool isIntel = fpsCapMode?.Value == 1;
                    int cap;
                    if (isIntel)
                    {
                        int tier = intelFpsTier?.Value ?? 0;   // 0=Off,1=60,2=40,3=30
                        cap = tier == 1 ? 60 : tier == 2 ? 40 : tier == 3 ? 30 : 0;
                    }
                    else
                    {
                        cap = fpsLimit?.Value ?? 0;            // 0 = Off
                    }
                    bool active = cap > 0;
                    // Always show the active mode (RTSS or Intel) — the tile only cycles within
                    // the current mode's steps (incl. off); it never switches mode.
                    string modeLabel = isIntel ? "Intel" : "RTSS";
                    fpsTile.StateText.Text = active ? $"{modeLabel} · {cap} FPS" : $"{modeLabel} · Off";
                    fpsTile.StateText.Foreground = active ? accentForeground : offForeground;
                    fpsTile.TileButton.Background = active ? tileOnBrush : tileOffBrush;
                }
                */

                // Charge Limiter tile — on/off; locked until set up in the System tab.
                if (qsTileMap.TryGetValue("ChargeLimiter", out var chgTile) && chgTile.TileButton != null)
                {
                    if (!IsChargeLimiterInitialized())
                    {
                        chgTile.StateText.Text = "Setup in Settings";
                        chgTile.StateText.Foreground = offForeground;
                        chgTile.TileButton.Background = tileOffBrush;
                    }
                    else
                    {
                        bool on = IsChargeLimiterEnabled();
                        chgTile.StateText.Text = on ? $"On {ChargeLimiterPercent()}%" : "Off";
                        chgTile.StateText.Foreground = on ? accentForeground : offForeground;
                        chgTile.TileButton.Background = on ? tileOnBrush : tileOffBrush;
                    }
                    // Update the battery visual (SoC fill + limit line) on the main tile.
                    UpdateChargeLimitBatteryVisual();
                }

                // EPP tile
                if (qsTileMap.TryGetValue("EPP", out var eppTile) && eppTile.TileButton != null)
                {
                    int eppValue = (int)(cpuEPP?.Value ?? 0);
                    eppTile.StateText.Text = $"{eppValue}%";
                    eppTile.StateText.Foreground = accentForeground;
                    eppTile.TileButton.Background = eppValue > 50 ? tileActiveBrush : tileOffBrush;
                }

                // Keyboard trigger tile
                if (qsTileMap.TryGetValue("Keyboard", out var keyboardTile) && keyboardTile.TileButton != null)
                {
                    keyboardTile.StateText.Text = "Open";
                    keyboardTile.StateText.Foreground = accentForeground;
                    keyboardTile.TileButton.Background = tileTriggerBrush;
                }

                // Fullscreen trigger tile — momentary toggle of the foreground app's fullscreen,
                // NOT a stateful setting. There is no reliable "is fullscreen" flag to read, so show
                // a neutral action label (was a misleading, always-"Off" state) + the trigger style.
                if (qsTileMap.TryGetValue("Fullscreen", out var fullscreenTile) && fullscreenTile.TileButton != null && fullscreenTile.StateText != null)
                {
                    fullscreenTile.StateText.Text = "Toggle";
                    fullscreenTile.StateText.Foreground = accentForeground;
                    fullscreenTile.TileButton.Background = tileTriggerBrush;
                }

                // OptiScaler / ReShade tiles — momentary sends of the overlay's toggle key
                // (Insert / Home), NOT stateful settings we can read back. Show the same neutral
                // "Toggle" label + trigger style as Fullscreen instead of a misleading always-"Off".
                foreach (var overlayToggleId in new[] { "OptiScaler", "ReShade" })
                {
                    if (qsTileMap.TryGetValue(overlayToggleId, out var overlayToggleTile)
                        && overlayToggleTile.TileButton != null && overlayToggleTile.StateText != null)
                    {
                        overlayToggleTile.StateText.Text = "Toggle";
                        overlayToggleTile.StateText.Foreground = accentForeground;
                        overlayToggleTile.TileButton.Background = tileTriggerBrush;
                    }
                }

                // Custom shortcut tiles
                foreach (var shortcutTile in qsCustomShortcuts)
                {
                    if (shortcutTile.TileButton != null && shortcutTile.StateText != null)
                    {
                        shortcutTile.StateText.Text = shortcutTile.CustomShortcut ?? "Run";
                        shortcutTile.StateText.Foreground = accentForeground;
                        shortcutTile.TileButton.Background = tileTriggerBrush;
                    }
                }

                // Legion Touchpad tile
                if (qsTileMap.TryGetValue("LegionTouchpad", out var touchpadTile) && touchpadTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionTouchpadEnabled?.Value ?? false;
                        touchpadTile.StateText.Text = enabled ? "On" : "Off";
                        touchpadTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        touchpadTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Light Mode tile
                if (qsTileMap.TryGetValue("LegionLightMode", out var lightTile) && lightTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        int mode = legionLightMode?.Value ?? 0;
                        string modeText;
                        switch (mode)
                        {
                            case 0: modeText = "Off"; break;
                            case 1: modeText = "Static"; break;
                            case 2: modeText = "Breathing"; break;
                            case 3: modeText = "Rainbow"; break;
                            case 4: modeText = "Spiral"; break;
                            default: modeText = "Off"; break;
                        }
                        lightTile.StateText.Text = modeText;
                        lightTile.StateText.Foreground = mode > 0 ? accentForeground : offForeground;
                        lightTile.TileButton.Background = mode > 0 ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Desktop Controls tile
                if (qsTileMap.TryGetValue("LegionDesktopControls", out var desktopTile) && desktopTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = LegionDesktopControlsToggle?.IsOn ?? false;
                        desktopTile.StateText.Text = enabled ? "On" : "Off";
                        desktopTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        desktopTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Remap Controls tile
                if (qsTileMap.TryGetValue("LegionRemapControls", out var remapTile) && remapTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool isGameProfile = LegionControllerProfileToggle?.IsOn == true && HasValidGame(currentGameName);
                        string profileName = isGameProfile ? currentGameName : "Global";
                        // Truncate long names
                        if (profileName.Length > 10)
                            profileName = profileName.Substring(0, 9) + "…";
                        remapTile.StateText.Text = profileName;
                        remapTile.StateText.Foreground = isGameProfile ? accentForeground : offForeground;
                        remapTile.TileButton.Background = isGameProfile ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Charge Limit tile (80% battery limit)
                if (qsTileMap.TryGetValue("LegionChargeLimit", out var chargeLimitTile) && chargeLimitTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionChargeLimit?.Value ?? false;
                        chargeLimitTile.StateText.Text = enabled ? "80%" : "Off";
                        chargeLimitTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        chargeLimitTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Power Light tile
                if (qsTileMap.TryGetValue("LegionPowerLight", out var powerLightTile) && powerLightTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionPowerLight?.Value ?? false;
                        powerLightTile.StateText.Text = enabled ? "On" : "Off";
                        powerLightTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        powerLightTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Controller Emulation tile — label reflects whichever backend is
                // currently selected (Legacy ViGEm vs VIIPER). For Legacy, show the
                // mode index (Mouse / Xbox / DS4 / DS4 Stick). For VIIPER, show the
                // active virtual-device tag (Xbox / DS4 / DS Edge / Elite 2 / Steam /
                // Switch). Without this split the tile always read the legacy mode
                // and was stuck on "Xbox" while VIIPER was actually presenting a
                // different device — see issue #79 round-2 reply.
                if (qsTileMap.TryGetValue("ControllerEmulation", out var ceTile) && ceTile.TileButton != null)
                {
                    bool available = controllerEmulationAvailable?.Value == true;
                    bool enabled = available && (controllerEmulationEnabled?.Value == true);
                    string label;
                    if (!available)
                    {
                        label = "N/A";
                    }
                    else if (!enabled)
                    {
                        label = "Off";
                    }
                    else if (emulationBackend?.Value == true)
                    {
                        // VIIPER backend — label is the active virtual device type.
                        string device = viiperDeviceType?.Value ?? "";
                        switch (device)
                        {
                            case "xbox360": label = "Xbox"; break;
                            case "dualshock4": label = "DS4"; break;
                            case "dualsenseedge": label = "DS Edge"; break;
                            case "xboxelite2": label = "Elite 2"; break;
                            case "steam-generic": label = "Steam"; break;
                            case "switchpro": label = "Switch"; break;
                            default: label = "On"; break;
                        }
                    }
                    else
                    {
                        // Legacy backend — label is the ControllerEmulationMode index
                        // (0=Mouse, 1=Xbox, 2=DS4 Motion, 3=DS4 Stick).
                        int mode = controllerEmulationMode?.Value ?? 1;
                        switch (mode)
                        {
                            case 0: label = "Mouse"; break;
                            case 1: label = "Xbox"; break;
                            case 2: label = "DS4"; break;
                            case 3: label = "DS4 Stick"; break;
                            default: label = "On"; break;
                        }
                    }
                    // StateText can be null if the tile was rebuilt mid-update during a
                    // foreground-window-change cascade — null-check before assigning.
                    if (ceTile.StateText != null)
                    {
                        ceTile.StateText.Text = label;
                        ceTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    }
                    ceTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // MSI Claw mode toggle tile (Controller ↔ Mouse)
                // Only shown on MSI Claw (ShouldSkipTile hides it on other devices).
                // "Controller" = ClawButtonMonitor running (virtual Xbox 360 controller via ViGEm)
                // "Mouse"      = MSIClawDesktopModeForwarder active (RS→cursor, LS→scroll, LB/RB→clicks)
                if (qsTileMap.TryGetValue("MSIClawDesktopMode", out var clawModeTile) && clawModeTile.TileButton != null)
                {
                    bool emulationActive = controllerEmulationAvailable?.Value == true
                                          && controllerEmulationEnabled?.Value == true
                                          && msiCenterActive?.Value != true;

                    if (!emulationActive)
                    {
                        // Controller emulation not available — show hint, disable tile
                        if (clawModeTile.StateText != null)
                        {
                            clawModeTile.StateText.Text = "Emulation off";
                            clawModeTile.StateText.Foreground = offForeground;
                        }
                        clawModeTile.TileButton.Background = tileOffBrush;
                        clawModeTile.TileButton.IsEnabled = false;
                    }
                    else
                    {
                        clawModeTile.TileButton.IsEnabled = true;
                        bool controllerOn = msiClawControllerMode?.Value != false;
                        string modeLabel = controllerOn ? "Controller" : "Mouse";
                        if (clawModeTile.StateText != null)
                        {
                            clawModeTile.StateText.Text = modeLabel;
                            clawModeTile.StateText.Foreground = controllerOn ? accentForeground : offForeground;
                        }
                        clawModeTile.TileButton.Background = controllerOn ? tileOnBrush : tileOffBrush;
                    }
                }

                // MSI Center M OEM software toggle tile
                if (qsTileMap.TryGetValue("MsiCenter", out var msiCenterTile) && msiCenterTile.TileButton != null)
                {
                    bool active = msiCenterActive?.Value == true;
                    if (msiCenterTile.StateText != null)
                    {
                        msiCenterTile.StateText.Text = active ? "On" : "Off";
                        msiCenterTile.StateText.Foreground = active ? accentForeground : offForeground;
                    }
                    msiCenterTile.TileButton.Background = active ? tileOnBrush : tileOffBrush;
                }

                // External Gamepad Mode tile (hide all handheld controllers for an external gamepad).
                // Active state is shown in purple to match the Controller-Status card's
                // "External Gamepad Mode" color.
                if (qsTileMap.TryGetValue("ExternalGamepadMode", out var extPadTile) && extPadTile.TileButton != null)
                {
                    bool on = externalGamepadMode?.Value == true;
                    var externalPurple = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 186, 104, 255));
                    if (extPadTile.StateText != null)
                    {
                        extPadTile.StateText.Text = on ? "On" : "Off";
                        extPadTile.StateText.Foreground = on ? externalPurple : offForeground;
                    }
                    extPadTile.TileButton.Background = on
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 40, 80)) // purple-tinted active background
                        : tileOffBrush;
                }

                // HW-mouse killswitch tile (forces the Claw FIRMWARE mouse — works on the UAC secure
                // desktop). It BREAKS controller input while active, so the ON state is highlighted
                // strongly in red/warning. Disabled with a hint when the virtual controller isn't running
                // (there is nothing to switch away from — EnterHwMouseKillswitch would no-op).
                if (qsTileMap.TryGetValue("MsiClawHwMouse", out var hwMouseTile) && hwMouseTile.TileButton != null)
                {
                    bool emulationActive = controllerEmulationAvailable?.Value == true
                                          && controllerEmulationEnabled?.Value == true
                                          && msiCenterActive?.Value != true;
                    bool on = msiClawHwMouse?.Value == true;
                    var warnForeground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 120, 120)); // red-ish

                    if (!emulationActive && !on)
                    {
                        if (hwMouseTile.StateText != null)
                        {
                            hwMouseTile.StateText.Text = "Emulation off";
                            hwMouseTile.StateText.Foreground = offForeground;
                        }
                        hwMouseTile.TileButton.Background = tileOffBrush;
                        hwMouseTile.TileButton.IsEnabled = false;
                    }
                    else
                    {
                        hwMouseTile.TileButton.IsEnabled = true;
                        if (hwMouseTile.StateText != null)
                        {
                            hwMouseTile.StateText.Text = on ? "HW Mouse" : "Controller";
                            hwMouseTile.StateText.Foreground = on ? warnForeground : offForeground;
                        }
                        hwMouseTile.TileButton.Background = on
                            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 90, 30, 30)) // strong red active background
                            : tileOffBrush;
                    }
                }

                // Fan Full Speed tile (Legion or GPD)
                if (qsTileMap.TryGetValue("LegionFanFullSpeed", out var fanFullSpeedTile) && fanFullSpeedTile.TileButton != null)
                {
                    bool enabled = false;
                    if (legionGoDetected?.Value == true)
                    {
                        enabled = legionFanFullSpeed?.Value ?? false;
                    }
                    else if (gpdDetected?.Value == true)
                    {
                        enabled = gpdFanMaxActive;
                    }
                    fanFullSpeedTile.StateText.Text = enabled ? "On" : "Off";
                    fanFullSpeedTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    fanFullSpeedTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Screen Saver tile
                if (qsTileMap.TryGetValue("ScreenSaver", out var screenSaverTile) && screenSaverTile.TileButton != null)
                {
                    bool enabled = screenSaverEnabled;
                    if (enabled)
                    {
                        // Don't overwrite countdown text — let the timer handle it
                        screenSaverTile.StateText.Foreground = accentForeground;
                    }
                    else
                    {
                        screenSaverTile.StateText.Text = "Off";
                        screenSaverTile.StateText.Foreground = offForeground;
                    }
                    screenSaverTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Battery tile - device battery in title, controllers in state text
                if (qsTileMap.TryGetValue("Battery", out var batteryTile) && batteryTile.TileButton != null)
                {
                    // Get device battery info (hide bolt at 100%)
                    int deviceBat = PowerManager.RemainingChargePercent;
                    bool deviceCharging = PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate;
                    string deviceIndicator = (deviceCharging && deviceBat < 100) ? "⚡" : "";

                    // Get the tile content elements
                    var content = batteryTile.TileButton.Content as StackPanel;
                    var iconElement = content?.Children.Count >= 1 ? content.Children[0] as FontIcon : null;
                    var labelText = content?.Children.Count >= 2 ? content.Children[1] as TextBlock : null;

                    // Update battery icon based on level and charging state
                    // Battery icons: \uE850-\uE859 (0-9), \uE83F (full)
                    // Charging icons: \uE85A-\uE863 (0-9), \uEBB5 (charging full)
                    if (iconElement != null)
                    {
                        string glyph;
                        if (deviceCharging)
                        {
                            // Charging icons
                            if (deviceBat >= 90) glyph = "\uEBB5";      // Full charging
                            else if (deviceBat >= 70) glyph = "\uE862"; // Charging 8
                            else if (deviceBat >= 50) glyph = "\uE85F"; // Charging 5
                            else if (deviceBat >= 30) glyph = "\uE85C"; // Charging 2
                            else glyph = "\uE85A";                       // Charging 0
                        }
                        else
                        {
                            // Normal battery icons
                            if (deviceBat >= 90) glyph = "\uE83F";      // Full
                            else if (deviceBat >= 70) glyph = "\uE858"; // Battery 8
                            else if (deviceBat >= 50) glyph = "\uE855"; // Battery 5
                            else if (deviceBat >= 30) glyph = "\uE852"; // Battery 2
                            else glyph = "\uE850";                       // Battery 0 (low)
                        }
                        iconElement.Glyph = glyph;
                    }

                    string stateText;
                    SolidColorBrush bgBrush;
                    int minBat = deviceBat; // Start with device battery

                    // Update title with device battery
                    if (labelText != null)
                    {
                        labelText.Text = $"{deviceBat}%{deviceIndicator}";
                    }

                    if (legionGoDetected?.Value == true)
                    {
                        int leftBat = controllerBatteryLeft?.Value ?? -1;
                        int rightBat = controllerBatteryRight?.Value ?? -1;
                        bool leftCharging = controllerChargingLeft?.Value ?? false;
                        bool rightCharging = controllerChargingRight?.Value ?? false;

                        if (leftBat > 0 && rightBat > 0)
                        {
                            // Controllers connected - show L/R with % (hide bolt at 100%)
                            string leftIndicator = (leftCharging && leftBat < 100) ? "⚡" : "";
                            string rightIndicator = (rightCharging && rightBat < 100) ? "⚡" : "";
                            stateText = $"L:{leftBat}%{leftIndicator} R:{rightBat}%{rightIndicator}";

                            // Color based on lowest of all batteries
                            minBat = Math.Min(deviceBat, Math.Min(leftBat, rightBat));
                        }
                        else
                        {
                            // Controllers not connected
                            stateText = "No Ctrl";
                        }
                    }
                    else
                    {
                        // Not Legion Go - just show "Device" in state
                        stateText = "Device";
                    }

                    // Color based on minimum battery level
                    if (minBat < 20)
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 35, 35)); // Red
                    else if (minBat < 50)
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 55, 35)); // Yellow
                    else
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 55, 40)); // Green

                    batteryTile.StateText.Text = stateText;
                    batteryTile.StateText.Foreground = accentForeground;
                    batteryTile.TileButton.Background = bgBrush;
                }

                // Re-evaluate scrolling for every tile whose state text may have
                // changed above. Text that fits centers; text that overflows the
                // tile's column starts the marquee loop.
                foreach (var t in qsTileDefinitions)
                {
                    if (t?.StateText != null && t.StateTextCanvas != null)
                    {
                        UpdateTileScrollAnimation(t);
                    }
                }

                Logger.Debug("Quick Settings tile states updated");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating Quick Settings tile states: {ex.Message}");
            }
        }

    }
}
