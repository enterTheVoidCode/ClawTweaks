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
        // Map of button index to button name (matches dropdown order in XAML)
        private static readonly string[] GamepadButtonNames = new[]
        {
            "LSClick", "LSUp", "LSDown", "LSLeft", "LSRight",
            "RSClick", "RSUp", "RSDown", "RSLeft", "RSRight",
            "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
            "A", "B", "X", "Y",
            "LB", "LT", "RB", "RT",
            "Start", "Select"
        };

        /// <summary>
        /// Serializes gamepad button mappings dictionary to JSON string.
        /// Format: {"ButtonName":{Type:0,GamepadAction:5,...},...}
        /// </summary>
        private string SerializeGamepadButtonMappings(Dictionary<string, ButtonMapping> mappings)
        {
            if (mappings == null || mappings.Count == 0)
                return "{}";

            // Output nested JSON objects (not escaped strings)
            var entries = mappings.Select(kvp =>
                $"\"{kvp.Key}\":{kvp.Value.ToJson()}");
            return "{" + string.Join(",", entries) + "}";
        }

        /// <summary>
        /// Deserializes JSON string to gamepad button mappings dictionary.
        /// Format: {"ButtonName":{Type:0,...},...}
        /// </summary>
        private Dictionary<string, ButtonMapping> DeserializeGamepadButtonMappings(string json)
        {
            var result = new Dictionary<string, ButtonMapping>();
            if (string.IsNullOrEmpty(json) || json == "{}")
                return result;

            // Match patterns like "ButtonName":{...}
            var regex = new System.Text.RegularExpressions.Regex("\"(\\w+)\"\\s*:\\s*(\\{[^}]+\\})");
            var matches = regex.Matches(json);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    var buttonName = match.Groups[1].Value;
                    var mappingJson = match.Groups[2].Value;
                    result[buttonName] = ButtonMapping.FromJson(mappingJson);
                }
            }

            return result;
        }

        private string GetGamepadButtonNameFromIndex(int index)
        {
            if (index >= 0 && index < GamepadButtonNames.Length)
                return GamepadButtonNames[index];
            return "LSClick"; // Default
        }

        private void LoadGamepadMappingToUI(string buttonName)
        {
            if (LegionGamepadTypeComboBox == null || LegionGamepadActionComboBox == null)
                return;

            ButtonMapping mapping;
            if (gamepadButtonMappings.TryGetValue(buttonName, out mapping))
            {
                // Set type dropdown
                LegionGamepadTypeComboBox.SelectedIndex = mapping.Type;

                // Show appropriate action dropdown based on type
                UpdateGamepadMappingUI(mapping.Type);

                // Set action value
                if (mapping.Type == 0 && LegionGamepadActionComboBox != null)
                    LegionGamepadActionComboBox.SelectedIndex = mapping.GamepadAction;
                else if (mapping.Type == 2 && LegionGamepadMouseComboBox != null)
                    LegionGamepadMouseComboBox.SelectedIndex = mapping.MouseButton;
                else if (mapping.Type == 1)
                    UpdateGamepadKeyboardKeyTags(mapping.KeyboardKeys);
            }
            else
            {
                // No mapping exists - set to default (Gamepad, Disabled)
                LegionGamepadTypeComboBox.SelectedIndex = 0;
                UpdateGamepadMappingUI(0);
                if (LegionGamepadActionComboBox != null)
                    LegionGamepadActionComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Re-asserts gamepad focus on <paramref name="target"/> after a UI rebuild. Rebuilding the
        /// key-tag / summary panels (or collapsing a mode panel) destroys the element that currently
        /// holds focus, so WinUI otherwise bounces focus to the top of the page (the tab strip).
        /// Deferred to Low priority so it runs after the framework has finished its own focus juggling.
        /// </summary>
        private void RestoreFocusDeferred(Control target)
        {
            if (target == null) return;
            // Land focus on a sensible control after a rebuild. The root cause (a virtualizing summary
            // panel tearing down the focus scope) is fixed structurally, so a single deferred re-assert
            // is enough — no aggressive timers that could yank focus back while the user navigates on.
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                try { target.Focus(FocusState.Programmatic); } catch { }
            });
        }

        private void UpdateGamepadMappingUI(int type)
        {
            // Type: 0=Gamepad, 1=Keyboard, 2=Mouse
            if (LegionGamepadActionComboBox != null)
                LegionGamepadActionComboBox.Visibility = type == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (LegionGamepadMouseComboBox != null)
                LegionGamepadMouseComboBox.Visibility = type == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (LegionGamepadKeyboardPanel != null)
                LegionGamepadKeyboardPanel.Visibility = type == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateGamepadKeyboardKeyTags(List<int> keys)
        {
            if (LegionGamepadKeyTags == null) return;

            LegionGamepadKeyTags.Children.Clear();
            if (keys == null) return;

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
                    Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    MinHeight = 0
                };
                removeButton.Click += (s, e) => RemoveGamepadKeyboardKey(key);

                tagPanel.Children.Add(keyText);
                tagPanel.Children.Add(removeButton);
                tagBorder.Child = tagPanel;
                LegionGamepadKeyTags.Children.Add(tagBorder);
            }
        }

        private void RemoveGamepadKeyboardKey(int key)
        {
            if (LegionGamepadButtonSelectorComboBox == null || LegionGamepadButtonSelectorComboBox.SelectedIndex < 0)
                return;

            var buttonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);
            if (gamepadButtonMappings.TryGetValue(buttonName, out var mapping))
            {
                // Move focus off the × before the rebuild destroys it, otherwise focus bounces to
                // the top of the tab.
                LegionGamepadKeyPickerButton?.Focus(FocusState.Programmatic);
                mapping.KeyboardKeys.Remove(key);
                UpdateGamepadKeyboardKeyTags(mapping.KeyboardKeys);
                SaveAndSendGamepadMappings();
                RestoreFocusDeferred(LegionGamepadKeyPickerButton);
            }
        }

        private void LegionGamepadButtonSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            if (LegionGamepadButtonSelectorComboBox == null || LegionGamepadButtonSelectorComboBox.SelectedIndex < 0)
                return;

            var buttonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);
            LoadGamepadMappingToUI(buttonName);
        }

        private void LegionGamepadMapping_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
            {
                Logger.Info($"[SwapEdit] DROP (isLoading={isLoadingControllerProfile} isSwitching={isSwitchingControllerProfile}) game='{currentGameName}'");
                return;
            }

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
            {
                Logger.Info($"[SwapEdit] DROP (within 2s of profile apply) game='{currentGameName}'");
                return;
            }

            if (LegionGamepadButtonSelectorComboBox == null || LegionGamepadButtonSelectorComboBox.SelectedIndex < 0)
                return;

            var buttonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);
            var type = LegionGamepadTypeComboBox?.SelectedIndex ?? 0;

            // Update UI visibility if type changed
            if (sender == LegionGamepadTypeComboBox)
            {
                UpdateGamepadMappingUI(type);
            }

            // Build new mapping from UI
            var mapping = new ButtonMapping { Type = type };
            if (type == 0 && LegionGamepadActionComboBox != null)
                mapping.GamepadAction = LegionGamepadActionComboBox.SelectedIndex;
            else if (type == 2 && LegionGamepadMouseComboBox != null)
                mapping.MouseButton = LegionGamepadMouseComboBox.SelectedIndex;
            else if (type == 1)
            {
                // Keep existing keyboard keys if we have them
                if (gamepadButtonMappings.TryGetValue(buttonName, out var existingMapping))
                    mapping.KeyboardKeys = new List<int>(existingMapping.KeyboardKeys ?? new List<int>());
            }

            gamepadButtonMappings[buttonName] = mapping;
            SaveAndSendGamepadMappings();

            // Keep focus on the control the user just used, otherwise the summary rebuild drops focus
            // and it jumps to the tab strip. Skip combos that are hidden behind an icon picker (the
            // action dropdown) — OpenGamepadPicker re-asserts focus on their picker button instead.
            var senderCombo = sender as ComboBox;
            if (senderCombo != null && (_gamepadPickerAttached == null || !_gamepadPickerAttached.Contains(senderCombo)))
                RestoreFocusDeferred(senderCombo);
        }

        /// <summary>
        /// Adds a keyboard key to the currently selected gamepad button (single-selector
        /// "Create new remappings" path). Value-based: the key code comes straight from the
        /// grouped key picker, so there is no dependency on dropdown item order.
        /// </summary>
        private void AddGamepadKeyboardKey(int keyCode)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
                return;

            if (keyCode <= 0)
                return;

            if (LegionGamepadButtonSelectorComboBox == null || LegionGamepadButtonSelectorComboBox.SelectedIndex < 0)
                return;

            var buttonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);

            // Get or create mapping
            if (!gamepadButtonMappings.TryGetValue(buttonName, out var mapping))
            {
                mapping = new ButtonMapping { Type = 1, KeyboardKeys = new List<int>() };
                gamepadButtonMappings[buttonName] = mapping;
            }
            if (mapping.KeyboardKeys == null)
                mapping.KeyboardKeys = new List<int>();

            if (mapping.KeyboardKeys.Count >= 5)
                return; // Max 5 keys per combo

            if (!mapping.KeyboardKeys.Contains(keyCode))
            {
                mapping.KeyboardKeys.Add(keyCode);
                UpdateGamepadKeyboardKeyTags(mapping.KeyboardKeys);
                SaveAndSendGamepadMappings();
            }

            // The picker flyout closed on selection — return focus to its launcher button so the
            // controller stays in the remapping area instead of snapping to the tab strip.
            RestoreFocusDeferred(LegionGamepadKeyPickerButton);
        }

        private void SaveAndSendGamepadMappings()
        {
            // Diagnostic snapshot of the decision inputs (so per-game vs global swap persistence
            // is fully traceable in the logs — mirrors the [CtrlSave] line for M1/M2/gyro).
            int swapCount = gamepadButtonMappings?.Count ?? 0;
            bool toggleOn = LegionControllerProfileToggle?.IsOn == true;
            bool validGame = HasValidGame(currentGameName);

            // Don't save during profile loading - we're just applying the profile, not modifying it
            // The profile will be fully applied and any saves will happen after isLoadingControllerProfile is cleared
            if (isLoadingControllerProfile)
            {
                // Skip sending if this is a duplicate call during profile loading
                // (the main send will happen via SendButtonMappingsToHelper at the end of ApplyControllerProfile)
                Logger.Info($"[SwapSave] SKIP (isLoadingControllerProfile) — toggleOn={toggleOn} validGame={validGame} game='{currentGameName}' swaps={swapCount}");
                return;
            }

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            // HID commands take ~1.5s to complete, so use 2 second window
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
            {
                Logger.Info($"[SwapSave] SKIP (within 2s of profile apply) — toggleOn={toggleOn} validGame={validGame} game='{currentGameName}' swaps={swapCount}");
                return;
            }

            // Get current profile
            ControllerProfile currentProfile;
            if (toggleOn && validGame)
            {
                gameControllerProfile = GetCurrentControllerProfileFromUI();
                SaveControllerProfileToStorage($"Game_{currentGameName}", gameControllerProfile);
                currentProfile = gameControllerProfile;
                Logger.Info($"[SwapSave] -> PER-GAME 'Game_{currentGameName}' (toggleOn={toggleOn} validGame={validGame} swaps={swapCount})");
            }
            else
            {
                globalControllerProfile = GetCurrentControllerProfileFromUI();
                SaveControllerProfileToStorage("Global", globalControllerProfile);
                currentProfile = globalControllerProfile;
                Logger.Info($"[SwapSave] -> GLOBAL (toggleOn={toggleOn} validGame={validGame} game='{currentGameName}' swaps={swapCount})");
            }

            // Send to helper
            SendButtonMappingsToHelper(currentProfile);

            // Update the summary display
            UpdateGamepadMappingSummary();
        }
        private void LegionGamepadResetAll_Click(object sender, RoutedEventArgs e)
        {
            // Reset ALL gamepad buttons (including LS and RS stick directions) to their defaults
            // This ensures any button that might have been remapped is reset, not just those in the dictionary
            foreach (var buttonName in GamepadButtonNames)
            {
                gamepadButtonMappings[buttonName] = new ButtonMapping { Type = 0, GamepadAction = 0 };
            }

            // Send reset commands for all buttons - helper will clear then remap to self
            SaveAndSendGamepadMappings();

            Logger.Info($"Sent reset HID commands for all {GamepadButtonNames.Length} gamepad buttons");

            // Now clear the dictionary (buttons are now at default, no need to track them)
            gamepadButtonMappings.Clear();

            // Reset UI to defaults
            if (LegionGamepadTypeComboBox != null)
                LegionGamepadTypeComboBox.SelectedIndex = 0;
            if (LegionGamepadActionComboBox != null)
                LegionGamepadActionComboBox.SelectedIndex = 0;
            UpdateGamepadMappingUI(0);
            if (LegionGamepadKeyTags != null)
                LegionGamepadKeyTags.Children.Clear();

            // Update summary display (now empty)
            UpdateGamepadMappingSummary();

            // Keep focus on the Reset button instead of bouncing to the tab strip.
            RestoreFocusDeferred(sender as Control);

            Logger.Info("Reset all gamepad button mappings");
        }

        /// <summary>
        /// Updates the summary display showing which gamepad buttons are remapped.
        /// </summary>
        private void UpdateGamepadMappingSummary()
        {
            if (LegionGamepadRemappedTags == null || LegionGamepadRemappedLabel == null || LegionGamepadNoRemapsLabel == null)
                return;

            // Get list of remapped buttons (those with non-default mappings)
            var remappedButtons = gamepadButtonMappings
                .Where(kvp => IsButtonRemapped(kvp.Value))
                .Select(kvp => kvp.Key)
                .ToList();

            // Rebuild via a plain StackPanel's .Children (non-virtualizing) in manual rows. The old
            // virtualizing ItemsControl+ItemsWrapGrid tore down the gamepad focus scope on Clear(),
            // bouncing focus to the tab strip on every edit. This mirrors the (working) key-tag panels.
            LegionGamepadRemappedTags.Children.Clear();

            if (remappedButtons.Count == 0)
            {
                LegionGamepadRemappedLabel.Visibility = Visibility.Collapsed;
                LegionGamepadNoRemapsLabel.Visibility = Visibility.Visible;
                return;
            }

            LegionGamepadRemappedLabel.Visibility = Visibility.Visible;
            LegionGamepadNoRemapsLabel.Visibility = Visibility.Collapsed;

            const double rowBudget = 500;   // approx. card content width; wrap chips into new rows
            StackPanel row = null;
            double rowWidth = 0;
            foreach (var buttonName in remappedButtons)
            {
                var mapping = gamepadButtonMappings[buttonName];
                var tag = CreateRemappedButtonTag(buttonName, mapping);
                double est = EstimateRemappedTagWidth(mapping);
                if (row == null || rowWidth + est > rowBudget)
                {
                    row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                    LegionGamepadRemappedTags.Children.Add(row);
                    rowWidth = 0;
                }
                row.Children.Add(tag);
                rowWidth += est + 6;
            }
        }

        // Rough chip-width estimate for wrapping the summary into rows (source icon + arrow + × +
        // padding ≈ 92px, plus ~32px per target icon).
        private double EstimateRemappedTagWidth(ButtonMapping mapping)
        {
            int targets;
            if (mapping.Type == 1) targets = mapping.KeyboardKeys?.Count ?? 1;
            else if (mapping.Type == 0) targets = (mapping.GamepadActions != null && mapping.GamepadActions.Count > 0)
                ? mapping.GamepadActions.Count : 1;
            else targets = 1;
            if (targets < 1) targets = 1;
            return 92 + targets * 32;
        }

        private bool IsButtonRemapped(ButtonMapping mapping)
        {
            if (mapping == null) return false;
            bool hasGamepadCombo = mapping.GamepadActions != null && mapping.GamepadActions.Count > 0;
            // Type 0 (Gamepad) with action 0 (Disabled) means default/cleared
            // Keyboard or Mouse type means remapped
            // Gamepad type with action > 0 means remapped
            return mapping.Type != 0 || mapping.GamepadAction > 0 || hasGamepadCombo ||
                   (mapping.KeyboardKeys != null && mapping.KeyboardKeys.Count > 0);
        }

        private Border CreateRemappedButtonTag(string buttonName, ButtonMapping mapping)
        {
            var displayName = GetGamepadButtonDisplayName(buttonName);
            var mappingDesc = GetMappingDescription(mapping);

            var tagBorder = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 70, 90)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 4, 0)
            };

            var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Icon-based summary: source button icon → key icons (keyboard) or target text (gamepad/mouse).
            // Icons sized to match the selection dropdowns (28px) so they are actually recognisable.
            const double SummaryIconSize = 28;
            var sourceContent = BuildXboxButtonTagContent(displayName, SummaryIconSize);
            var arrowText = new TextBlock
            {
                Text = " → ",
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };

            var clearButton = new Button
            {
                Content = "×",
                FontSize = 14,
                // Focusable hit target so the controller D-pad can actually reach the × and remove a
                // mapping from the summary (the old 10px / 0-padding, non-tab-stop × was unreachable).
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(2, 0, 0, 0),
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center,
                IsTabStop = true,
                UseSystemFocusVisuals = true,
                MinWidth = 0,
                MinHeight = 0,
                Tag = buttonName
            };
            clearButton.Click += (s, e) => ClearSingleGamepadMapping((string)((Button)s).Tag);

            tagPanel.Children.Add(sourceContent);
            tagPanel.Children.Add(arrowText);
            if (mapping.Type == 1 && mapping.KeyboardKeys != null && mapping.KeyboardKeys.Count > 0)
            {
                for (int i = 0; i < mapping.KeyboardKeys.Count; i++)
                {
                    var kc = BuildKeyboardKeyTagContent(mapping.KeyboardKeys[i], SummaryIconSize);
                    if (kc is FrameworkElement fe) fe.Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0);
                    tagPanel.Children.Add(kc);
                }
            }
            else if (mapping.Type == 0)
            {
                // Gamepad target(s) — show the Xbox-button icon(s), not text. Combo mode maps to
                // several actions; a plain remap has the single GamepadAction.
                var acts = (mapping.GamepadActions != null && mapping.GamepadActions.Count > 0)
                    ? mapping.GamepadActions
                    : new List<int> { mapping.GamepadAction };
                for (int i = 0; i < acts.Count; i++)
                {
                    var gc = BuildXboxButtonTagContent(GetGamepadActionName(acts[i]), SummaryIconSize);
                    if (gc is FrameworkElement fe) fe.Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0);
                    tagPanel.Children.Add(gc);
                }
            }
            else
            {
                tagPanel.Children.Add(new TextBlock
                {
                    Text = mappingDesc,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            tagPanel.Children.Add(clearButton);
            tagBorder.Child = tagPanel;

            // Click on tag to select that button
            tagBorder.Tapped += (s, e) =>
            {
                var index = Array.IndexOf(GamepadButtonNames, buttonName);
                if (index >= 0 && LegionGamepadButtonSelectorComboBox != null)
                    LegionGamepadButtonSelectorComboBox.SelectedIndex = index;
            };

            return tagBorder;
        }

        private void ClearSingleGamepadMapping(string buttonName)
        {
            if (gamepadButtonMappings.ContainsKey(buttonName))
            {
                // Move focus onto a stable control before the summary rebuild destroys the × we just
                // clicked, otherwise focus bounces to the top of the tab.
                LegionGamepadTypeComboBox?.Focus(FocusState.Programmatic);

                // Set to reset state (Type=0, GamepadAction=0) to trigger HID command
                // This maps the button back to itself (default behavior)
                gamepadButtonMappings[buttonName] = new ButtonMapping { Type = 0, GamepadAction = 0 };
                SaveAndSendGamepadMappings();

                // Now remove from dictionary (button is at default, no need to track)
                gamepadButtonMappings.Remove(buttonName);

                // Update the summary display
                UpdateGamepadMappingSummary();

                // If this was the currently selected button, reload UI
                if (LegionGamepadButtonSelectorComboBox != null)
                {
                    var currentButtonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);
                    if (currentButtonName == buttonName)
                        LoadGamepadMappingToUI(buttonName);
                }

                // The × we just clicked was destroyed with the summary rebuild — keep focus in the
                // remapping area instead of letting it jump to the tab strip. (The button selector is
                // now a hidden state-store combo, so anchor on the visible type dropdown.)
                RestoreFocusDeferred(LegionGamepadTypeComboBox);

                Logger.Info($"Cleared gamepad button mapping for {buttonName} (sent HID reset command)");
            }
        }

        private string GetGamepadButtonDisplayName(string buttonName)
        {
            // Convert internal names to display names
            switch (buttonName)
            {
                case "LSClick": return "LS Click";
                case "LSUp": return "LS Up";
                case "LSDown": return "LS Down";
                case "LSLeft": return "LS Left";
                case "LSRight": return "LS Right";
                case "RSClick": return "RS Click";
                case "RSUp": return "RS Up";
                case "RSDown": return "RS Down";
                case "RSLeft": return "RS Left";
                case "RSRight": return "RS Right";
                case "DPadUp": return "D-Pad Up";
                case "DPadDown": return "D-Pad Down";
                case "DPadLeft": return "D-Pad Left";
                case "DPadRight": return "D-Pad Right";
                default: return buttonName;
            }
        }

        private string GetMappingDescription(ButtonMapping mapping)
        {
            if (mapping == null) return "Default";

            switch (mapping.Type)
            {
                case 0: // Gamepad
                    if (mapping.GamepadMode == 1)
                    {
                        var comboActions = mapping.GamepadActions ?? new List<int>();
                        if (comboActions.Count == 0 && mapping.GamepadAction > 0)
                        {
                            comboActions = new List<int> { mapping.GamepadAction };
                        }

                        if (comboActions.Count == 0)
                        {
                            return "Disabled";
                        }

                        string comboText = string.Join("+", comboActions.Select(GetGamepadActionName));
                        return mapping.Turbo ? $"{comboText} (Turbo)" : comboText;
                    }

                    if (mapping.GamepadAction == 0) return "Disabled";
                    string single = GetGamepadActionName(mapping.GamepadAction);
                    return mapping.Turbo ? $"{single} (Turbo)" : single;
                case 1: // Keyboard
                    if (mapping.KeyboardKeys == null || mapping.KeyboardKeys.Count == 0)
                        return "Keys";
                    return string.Join("+", mapping.KeyboardKeys.Select(k => GetKeyDisplayName(k)));
                case 2: // Mouse
                    return GetMouseActionName(mapping.MouseButton);
                default:
                    return "Unknown";
            }
        }

        private string GetGamepadActionName(int action)
        {
            string[] names = { "Disabled", "LS Click", "LS Up", "LS Down", "LS Left", "LS Right",
                              "RS Click", "RS Up", "RS Down", "RS Left", "RS Right",
                              "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right",
                              "A", "B", "X", "Y", "LB", "LT", "RB", "RT", "Select", "Start",
                              "Xbox Button" };
            return action >= 0 && action < names.Length ? names[action] : $"Action{action}";
        }

        private string GetMouseActionName(int action)
        {
            string[] names = { "Left Click", "Right Click", "Middle Click",
                              "Scroll Up", "Scroll Down", "Scroll Left", "Scroll Right" };
            return action >= 0 && action < names.Length ? names[action] : $"Mouse{action}";
        }

    }
}
