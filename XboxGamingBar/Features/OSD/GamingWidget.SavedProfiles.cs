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

        private void SavedProfilesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isSavedProfilesExpanded = !isSavedProfilesExpanded;

            if (SavedProfilesContent != null)
            {
                SavedProfilesContent.Visibility = isSavedProfilesExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SavedProfilesExpandIcon != null)
            {
                SavedProfilesExpandIcon.Glyph = isSavedProfilesExpanded ? "\uE70E" : "\uE70D";
            }

            // Refresh the list when expanding
            if (isSavedProfilesExpanded)
            {
                RefreshSavedProfilesList();
            }
        }

        // Gamepad action names for profile summary display
        private static readonly string[] GamepadActionShortNames = new[]
        {
            "-", "LSC", "LSU", "LSD", "LSL", "LSR", "RSC", "RSU", "RSD", "RSL", "RSR",
            "DU", "DD", "DL", "DR", "A", "B", "X", "Y", "LB", "LT", "RB", "RT", "View", "Menu"
        };

        private void RefreshSavedProfilesList()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var savedProfiles = new List<SavedProfileInfo>();

                // Look for all controller profile containers
                foreach (var containerName in settings.Containers.Keys)
                {
                    if (!containerName.StartsWith("ControllerProfile_"))
                        continue;

                    var container = settings.Containers[containerName];
                    string displayName;
                    bool isGlobal = false;

                    if (containerName == "ControllerProfile_Global")
                    {
                        displayName = "Global (Default)";
                        isGlobal = true;
                    }
                    else if (containerName.StartsWith("ControllerProfile_Game_"))
                    {
                        // Extract game name: "ControllerProfile_Game_{gameName}"
                        displayName = containerName.Substring("ControllerProfile_Game_".Length).Replace("_", " ");
                    }
                    else
                    {
                        continue; // Unknown format
                    }

                    // Build settings summary
                    var summaryParts = new List<string>();

                    // Check for custom button mappings and show which buttons are remapped
                    var remapParts = new List<string>();
                    foreach (var btnName in new[] { "Y1", "Y2", "Y3", "M1", "M2", "M3", "Desktop", "Page" })
                    {
                        if (container.Values.TryGetValue($"Button{btnName}", out var mappingVal) && mappingVal is string mappingJson)
                        {
                            var mapping = ButtonMapping.FromJson(mappingJson);
                            if (mapping != null)
                            {
                                if (mapping.Type == 0)
                                {
                                    if (mapping.GamepadMode == 1 && mapping.GamepadActions != null && mapping.GamepadActions.Count > 0)
                                    {
                                        var comboNames = mapping.GamepadActions
                                            .Where(action => action > 0 && action < GamepadActionShortNames.Length)
                                            .Select(action => GamepadActionShortNames[action])
                                            .ToList();
                                        if (comboNames.Count > 0)
                                        {
                                            string comboText = string.Join("+", comboNames);
                                            remapParts.Add(mapping.Turbo ? $"{btnName}:{comboText}(T)" : $"{btnName}:{comboText}");
                                        }
                                    }
                                    else if (mapping.GamepadAction > 0 && mapping.GamepadAction < GamepadActionShortNames.Length)
                                    {
                                        // Gamepad remap
                                        string single = GamepadActionShortNames[mapping.GamepadAction];
                                        remapParts.Add(mapping.Turbo ? $"{btnName}:{single}(T)" : $"{btnName}:{single}");
                                    }
                                }
                                else if (mapping.Type == 1 && mapping.KeyboardKeys != null && mapping.KeyboardKeys.Count > 0)
                                {
                                    // Keyboard remap - show actual keys
                                    var keyNames = mapping.KeyboardKeys.Select(k => GetKeyDisplayName(k));
                                    remapParts.Add($"{btnName}:{string.Join("+", keyNames)}");
                                }
                                else if (mapping.Type == 2 && mapping.MouseButton > 0)
                                {
                                    // Mouse remap - show which button
                                    var mouseButtons = new[] { "", "Left", "Right", "Middle", "Back", "Forward" };
                                    var mouseName = mapping.MouseButton < mouseButtons.Length ? mouseButtons[mapping.MouseButton] : "Mouse";
                                    remapParts.Add($"{btnName}:{mouseName}Click");
                                }
                            }
                        }
                    }
                    if (remapParts.Count > 0)
                    {
                        summaryParts.Add(string.Join(" ", remapParts));
                    }

                    // Check gyro settings
                    if (container.Values.TryGetValue("GyroTarget", out var gyroTarget) && (int)gyroTarget > 0)
                    {
                        var gyroTargets = new[] { "", "LStick", "RStick", "Mouse" };
                        var targetIdx = (int)gyroTarget;
                        if (targetIdx > 0 && targetIdx < gyroTargets.Length)
                            summaryParts.Add($"Gyro:{gyroTargets[targetIdx]}");
                    }

                    // Check deadzones
                    if (container.Values.TryGetValue("LeftStickDeadzone", out var lsDz) && (int)lsDz != 4)
                    {
                        summaryParts.Add($"LDZ:{lsDz}%");
                    }
                    if (container.Values.TryGetValue("RightStickDeadzone", out var rsDz) && (int)rsDz != 4)
                    {
                        summaryParts.Add($"RDZ:{rsDz}%");
                    }

                    // Check joystick as mouse
                    if (container.Values.TryGetValue("JoystickAsMouseMode", out var jamMode) && (int)jamMode > 0)
                    {
                        summaryParts.Add("JoyMouse");
                    }

                    // Check RGB lighting settings
                    if (container.Values.TryGetValue("LightMode", out var lightModeVal))
                    {
                        int lightMode = (int)lightModeVal;
                        if (lightMode > 0) // 0 = Off
                        {
                            var lightModes = new[] { "Off", "Solid", "Breathe", "Rainbow", "Spiral" };
                            string modeName = lightMode < lightModes.Length ? lightModes[lightMode] : $"Mode{lightMode}";

                            // Get color if solid or breathe mode
                            if (lightMode == 1 || lightMode == 2) // Solid or Breathe
                            {
                                if (container.Values.TryGetValue("LightColorR", out var r) &&
                                    container.Values.TryGetValue("LightColorG", out var g) &&
                                    container.Values.TryGetValue("LightColorB", out var b))
                                {
                                    summaryParts.Add($"RGB:{modeName}({r},{g},{b})");
                                }
                                else
                                {
                                    summaryParts.Add($"RGB:{modeName}");
                                }
                            }
                            else
                            {
                                summaryParts.Add($"RGB:{modeName}");
                            }
                        }
                    }

                    // Check brightness
                    if (container.Values.TryGetValue("LightBrightness", out var brightnessVal) && (int)brightnessVal != 50)
                    {
                        summaryParts.Add($"Bright:{brightnessVal}%");
                    }

                    // Check power light
                    if (container.Values.TryGetValue("PowerLight", out var powerLightVal) && !(bool)powerLightVal)
                    {
                        summaryParts.Add("PwrLight:Off");
                    }

                    var summary = summaryParts.Count > 0 ? string.Join(" | ", summaryParts) : "Default settings";

                    // Get stored game exe path for icon loading
                    string gameExePath = null;
                    if (!isGlobal && container.Values.TryGetValue("GameExePath", out var exePathObj) && exePathObj is string exePath)
                    {
                        gameExePath = exePath;
                    }

                    savedProfiles.Add(new SavedProfileInfo
                    {
                        ProfileKey = containerName,
                        GameName = displayName,
                        SettingsSummary = summary,
                        IsGlobal = isGlobal,
                        GameExePath = gameExePath
                    });
                }

                // Sort: Global first, then alphabetically by game name
                savedProfiles.Sort((a, b) =>
                {
                    if (a.IsGlobal && !b.IsGlobal) return -1;
                    if (!a.IsGlobal && b.IsGlobal) return 1;
                    return string.Compare(a.GameName, b.GameName, StringComparison.OrdinalIgnoreCase);
                });

                // Update UI
                SavedProfilesList.ItemsSource = savedProfiles;
                NoSavedProfilesText.Visibility = savedProfiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Load icons asynchronously for saved profiles
                _ = LoadSavedProfileIconsAsync(savedProfiles);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to refresh saved profiles list: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads icons for saved profiles asynchronously.
        /// </summary>
        private async Task LoadSavedProfileIconsAsync(List<SavedProfileInfo> profiles)
        {
            Logger.Info($"LoadSavedProfileIconsAsync: Loading icons for {profiles.Count} profiles");

            foreach (var profile in profiles)
            {
                if (profile.IsGlobal)
                {
                    Logger.Debug($"LoadSavedProfileIconsAsync: Skipping global profile");
                    continue;
                }

                if (string.IsNullOrEmpty(profile.GameExePath))
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: No exe path for {profile.GameName}");
                    continue;
                }

                try
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: Loading icon for {profile.GameName} from {profile.GameExePath}");
                    var icon = await LoadSavedProfileIconAsync(profile.GameExePath);
                    if (icon != null)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            profile.IconSource = icon;
                        });
                        Logger.Info($"LoadSavedProfileIconsAsync: Icon loaded for {profile.GameName}");
                    }
                    else
                    {
                        Logger.Info($"LoadSavedProfileIconsAsync: No icon found for {profile.GameName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: Error loading icon for {profile.GameName}: {ex.Message}");
                }
            }
        }

        private void DeleteSavedProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string profileKey)
            {
                try
                {
                    // Don't allow deleting Global profile
                    if (profileKey == "ControllerProfile_Global")
                    {
                        Logger.Warn("Cannot delete Global controller profile");
                        return;
                    }

                    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                    // Delete the controller profile container
                    if (settings.Containers.ContainsKey(profileKey))
                    {
                        settings.DeleteContainer(profileKey);
                        Logger.Info($"Deleted controller profile: {profileKey}");
                    }

                    // Refresh the list
                    RefreshSavedProfilesList();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to delete profile {profileKey}: {ex.Message}");
                }
            }
        }

    }
}
