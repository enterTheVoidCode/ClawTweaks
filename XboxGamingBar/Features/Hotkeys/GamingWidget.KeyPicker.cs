using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// Grouped, icon-based, D-pad-navigable keyboard key picker. Replaces the flat ~80-entry
    /// key ComboBoxes in the "Button Remapping and Macros" card. Value-based: the picker returns
    /// the HID key code directly via a callback, so it does NOT depend on the fragile
    /// index→keycode mapping (<see cref="GetKeyCodeFromDropdownIndex"/>) that the remaining
    /// (Hotkeys / Scroll / Custom-Shortcut) dropdowns still use.
    ///
    /// Reuses <see cref="BuildKeyboardKeyTagContent"/> (Kenney PNG icons) so no new assets are needed.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private sealed class KeyCategory
        {
            public string Name;
            public int[] Keys;      // ordered HID key codes
            public int[] Preview;   // 2-4 key codes shown as icons on the category entry
        }

        // Single source of truth for the picker. Order matches the physical keyboard grouping.
        private static readonly KeyCategory[] KeyPickerCategories =
        {
            new KeyCategory {
                Name = "Letters",
                Keys = new[] { 0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,0x10,
                               0x11,0x12,0x13,0x14,0x15,0x16,0x17,0x18,0x19,0x1A,0x1B,0x1C,0x1D },
                Preview = new[] { 0x04,0x16,0x07 } },            // A S D
            new KeyCategory {
                Name = "Numbers",
                Keys = new[] { 0x1E,0x1F,0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27 },
                Preview = new[] { 0x1E,0x1F,0x20 } },            // 1 2 3
            new KeyCategory {
                Name = "Function",
                Keys = new[] { 0x3A,0x3B,0x3C,0x3D,0x3E,0x3F,0x40,0x41,0x42,0x43,0x44,0x45 },
                Preview = new[] { 0x3A,0x3B,0x3C } },            // F1 F2 F3
            new KeyCategory {
                Name = "Arrows",
                Keys = new[] { 0x52,0x51,0x50,0x4F },
                Preview = new[] { 0x52,0x51,0x50,0x4F } },       // Up Down Left Right
            new KeyCategory {
                Name = "Modifiers",
                Keys = new[] { 0xE0,0xE1,0xE2,0xE3,0xE4,0xE5,0xE6,0xE7 },
                Preview = new[] { 0xE0,0xE1,0xE3 } },            // Ctrl Shift Win
            new KeyCategory {
                Name = "Navigation",
                Keys = new[] { 0x4A,0x4D,0x4B,0x4E,0x49,0x4C,0x46,0x48 },
                Preview = new[] { 0x4A,0x4B,0x4C } },            // Home PgUp Del
            new KeyCategory {
                Name = "Control",
                Keys = new[] { 0x28,0x29,0x2C,0x2B,0x2A,0x39 },
                Preview = new[] { 0x28,0x2C,0x2B } },            // Enter Space Tab
            new KeyCategory {
                Name = "Media",
                Keys = new[] { 0x80,0x81,0x7F },
                Preview = new[] { 0x80,0x81,0x7F } },            // Vol+ Vol- Mute
            new KeyCategory {
                Name = "Symbols",
                Keys = new[] { 0x2F,0x30,0x35,0x2D,0x2E,0x31,0x33,0x34,0x36,0x37,0x38 },
                Preview = new[] { 0x2D,0x2F,0x38 } },            // - [ /
        };

        private const int KeysPerRow = 3;

        private static SolidColorBrush Brush(byte r, byte g, byte b) =>
            new SolidColorBrush(Color.FromArgb(255, r, g, b));

        /// <summary>
        /// Opens the grouped key picker anchored to <paramref name="anchor"/>. When the user picks a
        /// key, <paramref name="onKeyChosen"/> is invoked with the HID key code and the picker closes.
        /// </summary>
        private void OpenKeyPicker(FrameworkElement anchor, Action<int> onKeyChosen)
        {
            if (anchor == null || onKeyChosen == null) return;

            var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };

            // Strip the presenter's default chrome so our Border paints the whole surface.
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

            // Right pane: scrollable host for the focused category's key buttons.
            var keysHost = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            var keysScroll = new ScrollViewer
            {
                Width = 224,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Disabled,
                Content = keysHost
            };
            Grid.SetColumn(keysScroll, 1);

            // Left pane: category buttons.
            var categoryPanel = new StackPanel { Width = 128 };
            Grid.SetColumn(categoryPanel, 0);

            var categoryButtons = new List<Button>();
            void SelectCategory(int index)
            {
                for (int i = 0; i < categoryButtons.Count; i++)
                    categoryButtons[i].Background = i == index ? Brush(0x2A, 0x3B, 0x54)
                                                               : new SolidColorBrush(Colors.Transparent);
                PopulateKeyPickerKeys(keysHost, KeyPickerCategories[index], flyout, onKeyChosen);
            }

            for (int i = 0; i < KeyPickerCategories.Length; i++)
            {
                int index = i;
                var cat = KeyPickerCategories[i];

                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                content.Children.Add(new TextBlock
                {
                    Text = cat.Name,
                    Foreground = Brush(0x4A, 0xB3, 0xF4),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 74
                });
                var previewPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                foreach (var pk in cat.Preview)
                    previewPanel.Children.Add(BuildKeyboardKeyTagContent(pk, 16));
                content.Children.Add(previewPanel);

                var catButton = new Button
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
                catButton.Click += (s, e) => SelectCategory(index);
                catButton.GotFocus += (s, e) => SelectCategory(index);
                categoryButtons.Add(catButton);
                categoryPanel.Children.Add(catButton);
            }

            root.Children.Add(categoryPanel);
            root.Children.Add(keysScroll);
            outer.Child = root;
            flyout.Content = outer;

            // Start on the first category so the right pane isn't empty, and drop D-pad focus there.
            SelectCategory(0);
            flyout.Opened += (s, e) =>
            {
                if (categoryButtons.Count > 0)
                    categoryButtons[0].Focus(FocusState.Programmatic);
            };

            flyout.ShowAt(anchor);
        }

        private void PopulateKeyPickerKeys(Panel host, KeyCategory cat, Flyout flyout, Action<int> onKeyChosen)
        {
            host.Children.Clear();
            StackPanel row = null;
            for (int i = 0; i < cat.Keys.Length; i++)
            {
                if (i % KeysPerRow == 0)
                {
                    row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
                    host.Children.Add(row);
                }

                int keyCode = cat.Keys[i];
                var keyButton = new Button
                {
                    Content = BuildKeyboardKeyTagContent(keyCode, 36),
                    Background = Brush(0x2A, 0x3B, 0x54),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(6),
                    MinWidth = 62,
                    MinHeight = 54,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    UseSystemFocusVisuals = true
                };
                ToolTipService.SetToolTip(keyButton, GetKeyDisplayName(keyCode));
                keyButton.Click += (s, e) =>
                {
                    onKeyChosen(keyCode);
                    flyout.Hide();
                };
                row.Children.Add(keyButton);
            }
        }

        // Click handlers for the "Create new remappings" single selector and the per-button rows.
        // Per-button rows (M1/M2/M3/Y1/Y2/Y3) are wired generically in InitializeButtonMappingEvents.
        private void LegionGamepadKeyPicker_Click(object sender, RoutedEventArgs e)
        {
            OpenKeyPicker(sender as FrameworkElement, AddGamepadKeyboardKey);
        }

        // ---- The other key-selection dropdowns (Hotkeys, Legion L/R, Scroll, Custom shortcut,
        //      left-MSI double-click). Each picker button carries its logical key name in Tag.
        //      Value-based: the picker returns the key code straight to the matching adder.
        private Dictionary<string, Action<int>> _extraKeyAdders;
        private Dictionary<string, Action<int>> ExtraKeyAdders => _extraKeyAdders ?? (_extraKeyAdders =
            new Dictionary<string, Action<int>>
            {
                ["HotkeyMenuA"]         = c => AddKeyToSelection("HotkeyMenuA", c, HotkeyMenuAKeyTags, null, () => SaveHotkeyKeys("MenuA", "HotkeyMenuA")),
                ["HotkeyMenuB"]         = c => AddKeyToSelection("HotkeyMenuB", c, HotkeyMenuBKeyTags, null, () => SaveHotkeyKeys("MenuB", "HotkeyMenuB")),
                ["HotkeyMenuX"]         = c => AddKeyToSelection("HotkeyMenuX", c, HotkeyMenuXKeyTags, null, () => SaveHotkeyKeys("MenuX", "HotkeyMenuX")),
                ["HotkeyMenuY"]         = c => AddKeyToSelection("HotkeyMenuY", c, HotkeyMenuYKeyTags, null, () => SaveHotkeyKeys("MenuY", "HotkeyMenuY")),
                ["HotkeyMenuDpadUp"]    = c => AddKeyToSelection("HotkeyMenuDpadUp", c, HotkeyMenuDpadUpKeyTags, null, () => SaveHotkeyKeys("MenuDpadUp", "HotkeyMenuDpadUp")),
                ["HotkeyMenuDpadDown"]  = c => AddKeyToSelection("HotkeyMenuDpadDown", c, HotkeyMenuDpadDownKeyTags, null, () => SaveHotkeyKeys("MenuDpadDown", "HotkeyMenuDpadDown")),
                ["HotkeyMenuDpadLeft"]  = c => AddKeyToSelection("HotkeyMenuDpadLeft", c, HotkeyMenuDpadLeftKeyTags, null, () => SaveHotkeyKeys("MenuDpadLeft", "HotkeyMenuDpadLeft")),
                ["HotkeyMenuDpadRight"] = c => AddKeyToSelection("HotkeyMenuDpadRight", c, HotkeyMenuDpadRightKeyTags, null, () => SaveHotkeyKeys("MenuDpadRight", "HotkeyMenuDpadRight")),
                ["LegionL"]             = c => AddKeyToSelection("LegionL", c, LegionLKeyTags, null, SaveLegionLKeys),
                ["LegionR"]             = c => AddKeyToSelection("LegionR", c, LegionRKeyTags, null, SaveLegionRKeys),
                ["Scroll"]              = c => AddKeyToSelection("Scroll", c, ScrollKeyTags, null, SaveScrollKeys),
                ["ScrollClick"]         = c => AddKeyToSelection("ScrollClick", c, ScrollClickKeyTags, null, SaveScrollClickKeys),
                ["CustomShortcut"]      = AddCustomShortcutKey,
                ["LeftMsiDouble"]       = AddLeftMsiDoubleKey,
            });

        private void ExtraKeyPicker_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var keyName = btn?.Tag as string;
            if (btn == null || string.IsNullOrEmpty(keyName)) return;
            if (!ExtraKeyAdders.TryGetValue(keyName, out var add)) return;
            OpenKeyPicker(btn, code => { add(code); RestoreFocusDeferred(btn); });
        }
    }
}
