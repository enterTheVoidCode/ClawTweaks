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
    }
}
