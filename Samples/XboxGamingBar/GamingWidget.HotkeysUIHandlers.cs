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
        private void HotkeysExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isHotkeysExpanded = !isHotkeysExpanded;

            if (HotkeysContent != null)
            {
                HotkeysContent.Visibility = isHotkeysExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (HotkeysExpandIcon != null)
            {
                HotkeysExpandIcon.Glyph = isHotkeysExpanded ? "\uE70E" : "\uE70D";
            }

            // Load settings when expanding for the first time
            if (isHotkeysExpanded)
            {
                LoadHotkeySettings();
            }
        }

        private void LoadHotkeySettings()
        {
            isLoadingHotkeys = true;
            try
            {
                // Ensure defaults are applied
                ApplyHotkeyDefaults();

                var settings = ApplicationData.Current.LocalSettings;

                // Menu+A
                int actionA = (int)(settings.Values["Hotkey_MenuA_Action"] ?? 0);
                SelectHotkeyComboBoxByTag(HotkeyMenuAComboBox, actionA);
                LoadKeysFromString("HotkeyMenuA", settings.Values["Hotkey_MenuA_Key"] as string ?? "", HotkeyMenuAKeyTags);
                if (HotkeyMenuAKeyPanel != null)
                    HotkeyMenuAKeyPanel.Visibility = (actionA == 1) ? Visibility.Visible : Visibility.Collapsed;

                // Menu+B
                int actionB = (int)(settings.Values["Hotkey_MenuB_Action"] ?? 0);
                SelectHotkeyComboBoxByTag(HotkeyMenuBComboBox, actionB);
                LoadKeysFromString("HotkeyMenuB", settings.Values["Hotkey_MenuB_Key"] as string ?? "", HotkeyMenuBKeyTags);
                if (HotkeyMenuBKeyPanel != null)
                    HotkeyMenuBKeyPanel.Visibility = (actionB == 1) ? Visibility.Visible : Visibility.Collapsed;

                // Menu+X
                int actionX = (int)(settings.Values["Hotkey_MenuX_Action"] ?? 0);
                SelectHotkeyComboBoxByTag(HotkeyMenuXComboBox, actionX);
                LoadKeysFromString("HotkeyMenuX", settings.Values["Hotkey_MenuX_Key"] as string ?? "", HotkeyMenuXKeyTags);
                if (HotkeyMenuXKeyPanel != null)
                    HotkeyMenuXKeyPanel.Visibility = (actionX == 1) ? Visibility.Visible : Visibility.Collapsed;

                // Menu+Y
                int actionY = (int)(settings.Values["Hotkey_MenuY_Action"] ?? 0);
                SelectHotkeyComboBoxByTag(HotkeyMenuYComboBox, actionY);
                LoadKeysFromString("HotkeyMenuY", settings.Values["Hotkey_MenuY_Key"] as string ?? "", HotkeyMenuYKeyTags);
                if (HotkeyMenuYKeyPanel != null)
                    HotkeyMenuYKeyPanel.Visibility = (actionY == 1) ? Visibility.Visible : Visibility.Collapsed;

                // Menu+DpadUp
                int actionDpadUp = (int)(settings.Values["Hotkey_MenuDpadUp_Action"] ?? 0);
                SelectHotkeyComboBoxByTag(HotkeyMenuDpadUpComboBox, actionDpadUp);
                LoadKeysFromString("HotkeyMenuDpadUp", settings.Values["Hotkey_MenuDpadUp_Key"] as string ?? "", HotkeyMenuDpadUpKeyTags);
                if (HotkeyMenuDpadUpKeyPanel != null)
                    HotkeyMenuDpadUpKeyPanel.Visibility = (actionDpadUp == 1) ? Visibility.Visible : Visibility.Collapsed;

                // Menu+DpadDown
                int actionDpadDown = (int)(settings.Values["Hotkey_MenuDpadDown_Action"] ?? 0);
                SelectHotkeyComboBoxByTag(HotkeyMenuDpadDownComboBox, actionDpadDown);
                LoadKeysFromString("HotkeyMenuDpadDown", settings.Values["Hotkey_MenuDpadDown_Key"] as string ?? "", HotkeyMenuDpadDownKeyTags);
                if (HotkeyMenuDpadDownKeyPanel != null)
                    HotkeyMenuDpadDownKeyPanel.Visibility = (actionDpadDown == 1) ? Visibility.Visible : Visibility.Collapsed;

                // Menu+DpadLeft
                int actionDpadLeft = (int)(settings.Values["Hotkey_MenuDpadLeft_Action"] ?? 0);
                SelectHotkeyComboBoxByTag(HotkeyMenuDpadLeftComboBox, actionDpadLeft);
                LoadKeysFromString("HotkeyMenuDpadLeft", settings.Values["Hotkey_MenuDpadLeft_Key"] as string ?? "", HotkeyMenuDpadLeftKeyTags);
                if (HotkeyMenuDpadLeftKeyPanel != null)
                    HotkeyMenuDpadLeftKeyPanel.Visibility = (actionDpadLeft == 1) ? Visibility.Visible : Visibility.Collapsed;

                // Menu+DpadRight
                int actionDpadRight = (int)(settings.Values["Hotkey_MenuDpadRight_Action"] ?? 0);
                SelectHotkeyComboBoxByTag(HotkeyMenuDpadRightComboBox, actionDpadRight);
                LoadKeysFromString("HotkeyMenuDpadRight", settings.Values["Hotkey_MenuDpadRight_Key"] as string ?? "", HotkeyMenuDpadRightKeyTags);
                if (HotkeyMenuDpadRightKeyPanel != null)
                    HotkeyMenuDpadRightKeyPanel.Visibility = (actionDpadRight == 1) ? Visibility.Visible : Visibility.Collapsed;

                // Sync all hotkey settings to fallback file for elevated helper
                SaveToFallbackSettingsFile(new Dictionary<string, object>
                {
                    { "Hotkey_MenuA_Action", actionA },
                    { "Hotkey_MenuB_Action", actionB },
                    { "Hotkey_MenuX_Action", actionX },
                    { "Hotkey_MenuY_Action", actionY },
                    { "Hotkey_MenuDpadUp_Action", actionDpadUp },
                    { "Hotkey_MenuDpadDown_Action", actionDpadDown },
                    { "Hotkey_MenuDpadLeft_Action", actionDpadLeft },
                    { "Hotkey_MenuDpadRight_Action", actionDpadRight }
                });

                Logger.Info("Hotkey settings loaded");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading hotkey settings: {ex.Message}");
            }
            finally
            {
                isLoadingHotkeys = false;
            }
        }

        private void SelectHotkeyComboBoxByTag(ComboBox comboBox, int tagValue)
        {
            if (comboBox == null) return;

            // Find the item with matching Tag value
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && item.Tag is string tagStr)
                {
                    if (int.TryParse(tagStr, out int itemTag) && itemTag == tagValue)
                    {
                        comboBox.SelectedIndex = i;
                        return;
                    }
                }
            }

            // Default to first item if tag not found
            comboBox.SelectedIndex = 0;
        }

        private void HotkeyMenuA_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuA", HotkeyMenuAComboBox, HotkeyMenuAKeyPanel);
        }

        private void HotkeyMenuB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuB", HotkeyMenuBComboBox, HotkeyMenuBKeyPanel);
        }

        private void HotkeyMenuX_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuX", HotkeyMenuXComboBox, HotkeyMenuXKeyPanel);
        }

        private void HotkeyMenuY_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuY", HotkeyMenuYComboBox, HotkeyMenuYKeyPanel);
        }

        private void HotkeyMenuDpadUp_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuDpadUp", HotkeyMenuDpadUpComboBox, HotkeyMenuDpadUpKeyPanel);
        }

        private void HotkeyMenuDpadDown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuDpadDown", HotkeyMenuDpadDownComboBox, HotkeyMenuDpadDownKeyPanel);
        }

        private void HotkeyMenuDpadLeft_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuDpadLeft", HotkeyMenuDpadLeftComboBox, HotkeyMenuDpadLeftKeyPanel);
        }

        private void HotkeyMenuDpadRight_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuDpadRight", HotkeyMenuDpadRightComboBox, HotkeyMenuDpadRightKeyPanel);
        }

        private void HandleHotkeySelectionChanged(string hotkeyName, ComboBox comboBox, StackPanel keyPanel)
        {
            if (isLoadingHotkeys) return;
            if (comboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                int action = int.Parse(tagStr);
                ApplicationData.Current.LocalSettings.Values[$"Hotkey_{hotkeyName}_Action"] = action;

                // Also save to JSON fallback file for elevated helper
                SaveToFallbackSettingsFile(new Dictionary<string, object>
                {
                    { $"Hotkey_{hotkeyName}_Action", action }
                });

                // Show/hide key panel based on action (1=Keyboard Shortcut)
                if (keyPanel != null)
                {
                    keyPanel.Visibility = (action == 1) ? Visibility.Visible : Visibility.Collapsed;
                }

                Logger.Info($"Hotkey {hotkeyName} action changed to {(HotkeyAction)action}");

                // Sync updated config to helper so its XInput monitor uses the new action
                SendControllerHotkeyConfigToHelper();
            }
        }

        #region Generic Key Dropdown Handling

        // Storage for selected keys (used for hotkeys, Legion buttons, scroll wheel, custom shortcuts)
        private Dictionary<string, List<int>> _selectedKeys = new Dictionary<string, List<int>>();
        private List<int> _customShortcutKeys = new List<int>();

        private List<int> GetSelectedKeys(string keyName)
        {
            if (!_selectedKeys.ContainsKey(keyName))
                _selectedKeys[keyName] = new List<int>();
            return _selectedKeys[keyName];
        }

        private void AddKeyToSelection(string keyName, int keyCode, ItemsControl keyTags, ComboBox keyComboBox, Action onKeysChanged = null)
        {
            var keys = GetSelectedKeys(keyName);
            if (keys.Count >= 5) return; // Max 5 keys
            if (!keys.Contains(keyCode) && keyCode > 0)
            {
                keys.Add(keyCode);
                UpdateKeyTagsDisplay(keyName, keyTags, onKeysChanged);
                onKeysChanged?.Invoke();
            }
            if (keyComboBox != null)
                keyComboBox.SelectedIndex = 0;
        }

        private void RemoveKeyFromSelection(string keyName, int keyCode, ItemsControl keyTags, Action onKeysChanged = null)
        {
            var keys = GetSelectedKeys(keyName);
            keys.Remove(keyCode);
            UpdateKeyTagsDisplay(keyName, keyTags, onKeysChanged);
            onKeysChanged?.Invoke();
        }

        private void UpdateKeyTagsDisplay(string keyName, ItemsControl keyTags, Action onKeysChanged = null)
        {
            if (keyTags == null) return;
            keyTags.Items.Clear();
            var keys = GetSelectedKeys(keyName);
            foreach (var key in keys)
            {
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0)
                };
                var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var keyText = new TextBlock
                {
                    Text = GetKeyDisplayName(key),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var removeButton = new Button
                {
                    Content = "×",
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    MinHeight = 0
                };
                int keyCode = key;
                removeButton.Click += (s, e) => RemoveKeyFromSelection(keyName, keyCode, keyTags, onKeysChanged);
                tagPanel.Children.Add(keyText);
                tagPanel.Children.Add(removeButton);
                tagBorder.Child = tagPanel;
                keyTags.Items.Add(tagBorder);
            }
        }

        private string GetKeysAsString(string keyName)
        {
            var keys = GetSelectedKeys(keyName);
            if (keys.Count == 0) return "";
            return string.Join("+", keys.Select(k => GetKeyDisplayName(k)));
        }

        private void LoadKeysFromString(string keyName, string keysString, ItemsControl keyTags)
        {
            var keys = GetSelectedKeys(keyName);
            keys.Clear();
            if (!string.IsNullOrEmpty(keysString))
            {
                var parts = keysString.Split('+');
                foreach (var part in parts)
                {
                    int keyCode = GetKeyCodeFromDisplayName(part.Trim());
                    if (keyCode > 0)
                        keys.Add(keyCode);
                }
            }
            UpdateKeyTagsDisplay(keyName, keyTags);
        }

        private int GetKeyCodeFromDisplayName(string name)
        {
            var keyNames = new Dictionary<string, int>
            {
                { "A", 0x04 }, { "B", 0x05 }, { "C", 0x06 }, { "D", 0x07 }, { "E", 0x08 },
                { "F", 0x09 }, { "G", 0x0A }, { "H", 0x0B }, { "I", 0x0C }, { "J", 0x0D },
                { "K", 0x0E }, { "L", 0x0F }, { "M", 0x10 }, { "N", 0x11 }, { "O", 0x12 },
                { "P", 0x13 }, { "Q", 0x14 }, { "R", 0x15 }, { "S", 0x16 }, { "T", 0x17 },
                { "U", 0x18 }, { "V", 0x19 }, { "W", 0x1A }, { "X", 0x1B }, { "Y", 0x1C },
                { "Z", 0x1D }, { "1", 0x1E }, { "2", 0x1F }, { "3", 0x20 }, { "4", 0x21 },
                { "5", 0x22 }, { "6", 0x23 }, { "7", 0x24 }, { "8", 0x25 }, { "9", 0x26 },
                { "0", 0x27 }, { "Enter", 0x28 }, { "Esc", 0x29 }, { "Backspace", 0x2A },
                { "Tab", 0x2B }, { "Space", 0x2C },
                { "F1", 0x3A }, { "F2", 0x3B }, { "F3", 0x3C }, { "F4", 0x3D }, { "F5", 0x3E },
                { "F6", 0x3F }, { "F7", 0x40 }, { "F8", 0x41 }, { "F9", 0x42 }, { "F10", 0x43 },
                { "F11", 0x44 }, { "F12", 0x45 },
                { "Right", 0x4F }, { "Left", 0x50 }, { "Down", 0x51 }, { "Up", 0x52 },
                { "Home", 0x4A }, { "PgUp", 0x4B }, { "Delete", 0x4C }, { "Del", 0x4C }, { "End", 0x4D },
                { "PgDn", 0x4E }, { "Insert", 0x49 }, { "Ins", 0x49 }, { "PrintScr", 0x46 }, { "PrtSc", 0x46 }, { "Pause", 0x48 },
                { "LCtrl", 0xE0 }, { "LShift", 0xE1 }, { "LAlt", 0xE2 }, { "LWin", 0xE3 }, { "LMeta", 0xE3 },
                { "RCtrl", 0xE4 }, { "RShift", 0xE5 }, { "RAlt", 0xE6 }, { "RWin", 0xE7 }, { "RMeta", 0xE7 },
                { "VolMute", 0x7F }, { "VolUp", 0x80 }, { "VolDown", 0x81 },
                { "[", 0x2F }, { "]", 0x30 }
            };
            return keyNames.TryGetValue(name, out int code) ? code : 0;
        }

        // Hotkey key selection handlers
        private void HotkeyMenuAKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingHotkeys || HotkeyMenuAKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(HotkeyMenuAKeyComboBox.SelectedIndex);
            AddKeyToSelection("HotkeyMenuA", keyCode, HotkeyMenuAKeyTags, HotkeyMenuAKeyComboBox, () => SaveHotkeyKeys("MenuA", "HotkeyMenuA"));
        }

        private void HotkeyMenuBKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingHotkeys || HotkeyMenuBKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(HotkeyMenuBKeyComboBox.SelectedIndex);
            AddKeyToSelection("HotkeyMenuB", keyCode, HotkeyMenuBKeyTags, HotkeyMenuBKeyComboBox, () => SaveHotkeyKeys("MenuB", "HotkeyMenuB"));
        }

        private void HotkeyMenuXKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingHotkeys || HotkeyMenuXKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(HotkeyMenuXKeyComboBox.SelectedIndex);
            AddKeyToSelection("HotkeyMenuX", keyCode, HotkeyMenuXKeyTags, HotkeyMenuXKeyComboBox, () => SaveHotkeyKeys("MenuX", "HotkeyMenuX"));
        }

        private void HotkeyMenuYKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingHotkeys || HotkeyMenuYKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(HotkeyMenuYKeyComboBox.SelectedIndex);
            AddKeyToSelection("HotkeyMenuY", keyCode, HotkeyMenuYKeyTags, HotkeyMenuYKeyComboBox, () => SaveHotkeyKeys("MenuY", "HotkeyMenuY"));
        }

        private void HotkeyMenuDpadUpKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingHotkeys || HotkeyMenuDpadUpKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(HotkeyMenuDpadUpKeyComboBox.SelectedIndex);
            AddKeyToSelection("HotkeyMenuDpadUp", keyCode, HotkeyMenuDpadUpKeyTags, HotkeyMenuDpadUpKeyComboBox, () => SaveHotkeyKeys("MenuDpadUp", "HotkeyMenuDpadUp"));
        }

        private void HotkeyMenuDpadDownKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingHotkeys || HotkeyMenuDpadDownKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(HotkeyMenuDpadDownKeyComboBox.SelectedIndex);
            AddKeyToSelection("HotkeyMenuDpadDown", keyCode, HotkeyMenuDpadDownKeyTags, HotkeyMenuDpadDownKeyComboBox, () => SaveHotkeyKeys("MenuDpadDown", "HotkeyMenuDpadDown"));
        }

        private void HotkeyMenuDpadLeftKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingHotkeys || HotkeyMenuDpadLeftKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(HotkeyMenuDpadLeftKeyComboBox.SelectedIndex);
            AddKeyToSelection("HotkeyMenuDpadLeft", keyCode, HotkeyMenuDpadLeftKeyTags, HotkeyMenuDpadLeftKeyComboBox, () => SaveHotkeyKeys("MenuDpadLeft", "HotkeyMenuDpadLeft"));
        }

        private void HotkeyMenuDpadRightKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingHotkeys || HotkeyMenuDpadRightKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(HotkeyMenuDpadRightKeyComboBox.SelectedIndex);
            AddKeyToSelection("HotkeyMenuDpadRight", keyCode, HotkeyMenuDpadRightKeyTags, HotkeyMenuDpadRightKeyComboBox, () => SaveHotkeyKeys("MenuDpadRight", "HotkeyMenuDpadRight"));
        }

        private void SaveHotkeyKeys(string hotkeyName, string keyStorageName)
        {
            var keysString = GetKeysAsString(keyStorageName);
            ApplicationData.Current.LocalSettings.Values[$"Hotkey_{hotkeyName}_Key"] = keysString;
            Logger.Info($"Hotkey {hotkeyName} keys saved: {keysString}");

            // Sync updated config to helper so its XInput monitor uses the new key
            SendControllerHotkeyConfigToHelper();
        }

        // Legion L/R key selection handlers
        private void LegionLKeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LegionLKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(LegionLKeyComboBox.SelectedIndex);
            AddKeyToSelection("LegionL", keyCode, LegionLKeyTags, LegionLKeyComboBox, SaveLegionLKeys);
        }

        private void LegionRKeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LegionRKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(LegionRKeyComboBox.SelectedIndex);
            AddKeyToSelection("LegionR", keyCode, LegionRKeyTags, LegionRKeyComboBox, SaveLegionRKeys);
        }

        private void SaveLegionLKeys()
        {
            var keysString = GetKeysAsString("LegionL");
            ApplicationData.Current.LocalSettings.Values["LegionL_Shortcut"] = keysString;
            SaveLegionRemapSettings();
            ApplyLegionButtonConfig(true);
        }

        private void SaveLegionRKeys()
        {
            var keysString = GetKeysAsString("LegionR");
            ApplicationData.Current.LocalSettings.Values["LegionR_Shortcut"] = keysString;
            SaveLegionRemapSettings();
            ApplyLegionButtonConfig(false);
        }

        // Scroll wheel key selection handlers
        private void ScrollKeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScrollKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(ScrollKeyComboBox.SelectedIndex);
            AddKeyToSelection("Scroll", keyCode, ScrollKeyTags, ScrollKeyComboBox, SaveScrollKeys);
        }

        private void ScrollClickKeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScrollClickKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(ScrollClickKeyComboBox.SelectedIndex);
            AddKeyToSelection("ScrollClick", keyCode, ScrollClickKeyTags, ScrollClickKeyComboBox, SaveScrollClickKeys);
        }

        private void SaveScrollKeys()
        {
            var keysString = GetKeysAsString("Scroll");
            ApplicationData.Current.LocalSettings.Values["Scroll_Shortcut"] = keysString;
            SaveScrollRemapSettings();
            ApplyScrollWheelConfig("Scroll");
        }

        private void SaveScrollClickKeys()
        {
            var keysString = GetKeysAsString("ScrollClick");
            ApplicationData.Current.LocalSettings.Values["ScrollClick_Shortcut"] = keysString;
            SaveScrollRemapSettings();
            ApplyScrollWheelConfig("Click");
        }

        // Custom shortcut key selection handler
        private void CustomShortcutKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomShortcutKeyComboBox?.SelectedIndex <= 0) return;
            int keyCode = GetKeyCodeFromDropdownIndex(CustomShortcutKeyComboBox.SelectedIndex);
            if (_customShortcutKeys.Count < 5 && !_customShortcutKeys.Contains(keyCode) && keyCode > 0)
            {
                _customShortcutKeys.Add(keyCode);
                UpdateCustomShortcutKeyTags();
            }
            CustomShortcutKeyComboBox.SelectedIndex = 0;
        }

        private void UpdateCustomShortcutKeyTags()
        {
            if (CustomShortcutKeyTags == null) return;
            CustomShortcutKeyTags.Items.Clear();
            foreach (var key in _customShortcutKeys)
            {
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0)
                };
                var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var keyText = new TextBlock
                {
                    Text = GetKeyDisplayName(key),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var removeButton = new Button
                {
                    Content = "×",
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    MinHeight = 0
                };
                int keyCode = key;
                removeButton.Click += (s, args) => { _customShortcutKeys.Remove(keyCode); UpdateCustomShortcutKeyTags(); };
                tagPanel.Children.Add(keyText);
                tagPanel.Children.Add(removeButton);
                tagBorder.Child = tagPanel;
                CustomShortcutKeyTags.Items.Add(tagBorder);
            }
        }

        private string GetCustomShortcutKeysString()
        {
            if (_customShortcutKeys.Count == 0) return "";
            return string.Join("+", _customShortcutKeys.Select(k => GetKeyDisplayName(k)));
        }

        #endregion

    }
}
