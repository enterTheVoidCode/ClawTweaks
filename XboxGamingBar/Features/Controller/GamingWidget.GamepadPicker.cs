using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// Zone-grouped, icon-only, D-pad-navigable picker for the Xbox-button (gamepad-action)
    /// dropdowns — the counterpart of the keyboard <see cref="OpenKeyPicker"/>.
    ///
    /// Hybrid design (low-risk): each existing action/selector/macro ComboBox stays in the visual
    /// tree as the state store, so ALL existing save/load/index logic is untouched. We just make the
    /// combo visually nil and overlay an icon picker button that drives <c>combo.SelectedIndex</c>.
    /// The picker's zones/entries are derived from the combo's OWN items, so every variant
    /// (action incl. "Disabled", 22-entry selector, macro with "+ Button") is handled by one path.
    /// Icons come from <see cref="GamepadButtonIconConverter.Map"/> via <see cref="BuildXboxButtonTagContent"/>.
    /// </summary>
    public sealed partial class GamingWidget
    {
        // Zone order + membership test (matched against the ComboBox item strings).
        private static readonly KeyValuePair<string, Func<string, bool>>[] GamepadZones =
        {
            new KeyValuePair<string, Func<string, bool>>("Off",                n => n == "Disabled"),
            new KeyValuePair<string, Func<string, bool>>("Left Stick",         n => n == "LS" || n.StartsWith("LS ")),
            new KeyValuePair<string, Func<string, bool>>("Right Stick",        n => n == "RS" || n.StartsWith("RS ")),
            new KeyValuePair<string, Func<string, bool>>("D-Pad",              n => n.StartsWith("D-Pad")),
            new KeyValuePair<string, Func<string, bool>>("Face Buttons",       n => n == "A" || n == "B" || n == "X" || n == "Y"),
            new KeyValuePair<string, Func<string, bool>>("Bumpers & Triggers", n => n == "LB" || n == "LT" || n == "RB" || n == "RT"),
            new KeyValuePair<string, Func<string, bool>>("System",             n => n == "Select" || n == "Start" || n == "Xbox Button"),
        };

        private const int GamepadPerRow = 3;
        private HashSet<ComboBox> _gamepadPickerAttached;

        private UIElement BuildGamepadIcon(string name, double size)
        {
            if (string.IsNullOrEmpty(name) || name == "Disabled")
                return new TextBlock
                {
                    Text = "Off",
                    Foreground = Brush(0x99, 0xA5, 0xB5),
                    FontSize = size >= 30 ? 13 : 11,
                    VerticalAlignment = VerticalAlignment.Center
                };
            return BuildXboxButtonTagContent(name, size);
        }

        // Icon-only content for the closed picker button (reflects the combo's current selection).
        private UIElement BuildGamepadPickerButtonContent(ComboBox combo)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var name = combo?.SelectedItem as string;
            if (string.IsNullOrEmpty(name) || name == "+ Button")
                panel.Children.Add(new TextBlock { Text = "＋", Foreground = Brush(0xE0, 0xE0, 0xE0), FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            else
                panel.Children.Add(BuildGamepadIcon(name, 22));
            panel.Children.Add(new TextBlock { Text = "▾", Foreground = Brush(0xBB, 0xBB, 0xBB), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        /// <summary>
        /// Overlays an icon picker button on <paramref name="combo"/> and makes the combo visually nil
        /// while keeping it in the tree as the state store.
        /// </summary>
        private void AttachGamepadPicker(ComboBox combo, double width)
        {
            if (combo == null) return;
            if (_gamepadPickerAttached == null) _gamepadPickerAttached = new HashSet<ComboBox>();
            if (_gamepadPickerAttached.Contains(combo)) return; // already attached

            // At constructor time the Controller-tab subtree isn't parented yet, so combo.Parent is
            // null. Defer the attach until the combo enters the visual tree.
            if (!(combo.Parent is Panel parent))
            {
                RoutedEventHandler onLoaded = null;
                onLoaded = (s, e) => { combo.Loaded -= onLoaded; AttachGamepadPicker(combo, width); };
                combo.Loaded += onLoaded;
                return;
            }

            _gamepadPickerAttached.Add(combo);

            int idx = parent.Children.IndexOf(combo);
            if (idx < 0) return;

            var btn = new Button { Width = width, HorizontalAlignment = HorizontalAlignment.Left, Margin = combo.Margin };
            if (Resources.TryGetValue("KeyPickerButtonStyle", out object st) && st is Style style) btn.Style = style;
            Grid.SetColumn(btn, Grid.GetColumn(combo));
            Grid.SetRow(btn, Grid.GetRow(combo));
            Grid.SetColumnSpan(btn, Grid.GetColumnSpan(combo));
            btn.Visibility = combo.Visibility;
            btn.Content = BuildGamepadPickerButtonContent(combo);
            btn.Click += (s, e) => OpenGamepadPicker(btn, combo);

            // Combo stays in the tree (source of truth) but takes no space and no input.
            combo.Opacity = 0;
            combo.IsHitTestVisible = false;
            combo.MinWidth = 0;
            combo.Width = 0;
            combo.MaxWidth = 0;
            combo.Margin = new Thickness(0);

            combo.SelectionChanged += (s, e) => btn.Content = BuildGamepadPickerButtonContent(combo);
            // The app toggles combo.Visibility (type switch / combo-mode) — mirror it onto the button.
            combo.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (d, dp) => btn.Visibility = combo.Visibility);

            parent.Children.Insert(idx, btn);
        }

        /// <summary>Attaches the picker to every static Xbox-button dropdown (called once at init).</summary>
        private void WireGamepadPickers()
        {
            AttachGamepadPicker(LegionGamepadButtonSelectorComboBox, 96);
            AttachGamepadPicker(LegionGamepadActionComboBox, 130);
            foreach (var n in new[] { "M1", "M2", "M3", "Y1", "Y2", "Y3" })
                AttachGamepadPicker(FindName($"LegionButton{n}ComboBox") as ComboBox, 130);
            foreach (var n in new[] { "M1", "M2", "M3" })
                AttachGamepadPicker(FindName($"LegionButton{n}MacroButtonComboBox") as ComboBox, 130);
        }

        private void OpenGamepadPicker(Button anchor, ComboBox combo)
        {
            if (anchor == null || combo == null) return;

            var items = combo.Items
                .Select(o => o?.ToString())
                .Where(s => !string.IsNullOrEmpty(s) && s != "+ Button")
                .ToList();

            var zones = new List<KeyValuePair<string, List<string>>>();
            foreach (var z in GamepadZones)
            {
                var entries = items.Where(z.Value).ToList();
                if (entries.Count > 0) zones.Add(new KeyValuePair<string, List<string>>(z.Key, entries));
            }
            if (zones.Count == 0) return;

            var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };
            var presenterStyle = new Style(typeof(FlyoutPresenter));
            presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            presenterStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
            presenterStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            presenterStyle.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 440.0));
            presenterStyle.Setters.Add(new Setter(ScrollViewer.HorizontalScrollModeProperty, ScrollMode.Disabled));
            presenterStyle.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
            flyout.FlyoutPresenterStyle = presenterStyle;

            var outer = new Border
            {
                Background = Brush(0x1E, 0x2A, 0x3E),
                BorderBrush = Brush(0x3D, 0x5A, 0x80),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6)
            };

            var root = new Grid { XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var entriesHost = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            var entriesScroll = new ScrollViewer
            {
                Width = 232,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Disabled,
                Content = entriesHost
            };
            Grid.SetColumn(entriesScroll, 1);

            var zonePanel = new StackPanel { Width = 164 };
            Grid.SetColumn(zonePanel, 0);

            var zoneButtons = new List<Button>();
            void SelectZone(int index)
            {
                for (int i = 0; i < zoneButtons.Count; i++)
                    zoneButtons[i].Background = i == index ? Brush(0x2A, 0x3B, 0x54) : new SolidColorBrush(Colors.Transparent);
                PopulateGamepadEntries(entriesHost, zones[index].Value, combo, flyout, anchor);
            }

            for (int i = 0; i < zones.Count; i++)
            {
                int index = i;
                var zone = zones[i];

                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                content.Children.Add(new TextBlock
                {
                    Text = zone.Key,
                    Foreground = Brush(0x4A, 0xB3, 0xF4),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 104
                });
                var preview = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                foreach (var e in zone.Value.Take(2))
                    preview.Children.Add(BuildGamepadIcon(e, 16));
                content.Children.Add(preview);

                var zb = new Button
                {
                    Content = content,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(6, 5, 6, 5),
                    Margin = new Thickness(0, 0, 0, 2),
                    UseSystemFocusVisuals = true
                };
                zb.Click += (s, e) => SelectZone(index);
                zb.GotFocus += (s, e) => SelectZone(index);
                zoneButtons.Add(zb);
                zonePanel.Children.Add(zb);
            }

            root.Children.Add(zonePanel);
            root.Children.Add(entriesScroll);
            outer.Child = root;
            flyout.Content = outer;

            // Start on the zone that holds the current selection (else the first zone).
            int startZone = 0;
            var current = combo.SelectedItem as string;
            if (!string.IsNullOrEmpty(current))
            {
                for (int i = 0; i < zones.Count; i++)
                    if (zones[i].Value.Contains(current)) { startZone = i; break; }
            }
            SelectZone(startZone);
            flyout.Opened += (s, e) =>
            {
                if (startZone < zoneButtons.Count) zoneButtons[startZone].Focus(FocusState.Programmatic);
            };

            flyout.ShowAt(anchor);
        }

        private void PopulateGamepadEntries(Panel host, List<string> entries, ComboBox combo, Flyout flyout, Button anchor)
        {
            host.Children.Clear();
            StackPanel row = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (i % GamepadPerRow == 0)
                {
                    row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
                    host.Children.Add(row);
                }

                string name = entries[i];
                var btn = new Button
                {
                    Content = BuildGamepadIcon(name, 34),
                    Background = Brush(0x2A, 0x3B, 0x54),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(6),
                    MinWidth = 62,
                    MinHeight = 54,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    UseSystemFocusVisuals = true
                };
                ToolTipService.SetToolTip(btn, name);
                btn.Click += (s, e) =>
                {
                    // Drive the (hidden) combo — its existing SelectionChanged does the save/load/macro add.
                    for (int k = 0; k < combo.Items.Count; k++)
                    {
                        if ((combo.Items[k]?.ToString()) == name)
                        {
                            combo.SelectedIndex = k;
                            break;
                        }
                    }
                    flyout.Hide();
                    RestoreFocusDeferred(anchor);
                };
                row.Children.Add(btn);
            }
        }
    }
}
