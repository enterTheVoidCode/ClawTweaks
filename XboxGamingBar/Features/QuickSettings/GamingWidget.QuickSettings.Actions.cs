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
        /// Handle Quick Settings tile clicks
        /// </summary>
        private void QuickSettingsTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tileTag)
            {
                try
                {
                    // Custom action tiles (predefined action type) and keyboard shortcut tiles
                    // are handled first and return early. Standard built-in tiles fall through
                    // to the switch block below.
                    if (qsTileMap.TryGetValue(tileTag, out var mappedTile))
                    {
                        // Check for predefined action first
                        if (mappedTile.ActionType != TileActionType.None &&
                            mappedTile.ActionType != TileActionType.KeyboardShortcut)
                        {
                            _ = ExecuteTileActionAsync(mappedTile.ActionType, mappedTile.Name);
                            UpdateQuickSettingsTileStates();
                            return;
                        }
                        // Keyboard shortcut tile
                        if (!string.IsNullOrEmpty(mappedTile.CustomShortcut))
                        {
                            _ = SendCustomShortcutAsync(mappedTile.CustomShortcut, mappedTile.Name);
                            UpdateQuickSettingsTileStates();
                            return;
                        }
                        // Standard tile with no action/shortcut — fall through to switch below
                    }
                    // Fallback: Check QuickSettingsConfig by ID (tile IDs are now GUIDs for custom tiles)
                    else if (QuickSettings.QuickSettingsConfig.Instance.GetTile(tileTag) is QuickSettings.QuickSettingsTile configTile
                             && configTile.Type == QuickSettings.TileType.CustomShortcut
                             && !string.IsNullOrEmpty(configTile.CustomShortcut))
                    {
                        _ = SendCustomShortcutAsync(configTile.CustomShortcut, configTile.Name);
                    }

                    // Standard built-in tiles: always reached when no action/shortcut matched above
                    switch (tileTag)
                    {
                        // NOTE: this block was previously inside `else` which made it unreachable
                        // for tiles already found in qsTileMap. Moved outside so standard tiles work.
                            case "TDPMode":
                                if (_isControllerTriggered) CycleTDPMode();
                                else ShowTDPDropdown(button);
                                break;
                            case "AutoTDP":
                                ToggleAutoTDPTile();
                                break;
                            case "Profile":
                                TogglePerGameProfile();
                                break;
                            case "Overlay":
                                if (_isControllerTriggered) CyclePerformanceOverlay();
                                else ShowOverlayDropdown(button);
                                break;
                            case "PowerMode":
                                CyclePowerMode();
                                break;
                            case "FPSCombined":
                                if (_isControllerTriggered) CycleFPSForCurrentMode();
                                else ShowFPSCombinedDropdown(button);
                                break;
                            case "FPSLimit":               // kept for legacy/settings persistence
                                CycleFPSLimit();
                                break;
                            case "IntelFpsTier":           // kept for legacy/settings persistence
                                CycleIntelFpsTier();
                                break;
                            case "Resolution":
                                CycleResolution();
                                break;
                            case "Rotation":
                                CycleRotation();
                                break;
                            case "HDR":
                                ToggleHDR();
                                break;
                            case "LosslessScaling":
                                ToggleLosslessScaling();
                                break;
                            case "OptiScaler":
                                // Close Game Bar, then send Insert key (OptiScaler overlay toggle)
                                _ = SendCustomShortcutAsync("Insert", "OptiScaler");
                                break;
                            case "ReShade":
                                // Close Game Bar, then send Home/Pos1 key (ReShade overlay toggle)
                                _ = SendCustomShortcutAsync("Home", "ReShade");
                                break;
                            case "RIS":
                                ToggleRIS();
                                break;
                            case "AFMF":
                                ToggleAFMF();
                                break;
                            case "RSR":
                                ToggleRSR();
                                break;
                            case "AntiLag":
                                ToggleAntiLag();
                                break;
                            case "RadeonChill":
                                ToggleRadeonChill();
                                break;
                            case "CPUBoost":
                                ToggleCPUBoost();
                                break;
                            case "EPP":
                                CycleEPP();
                                break;
                            case "ScreenSaver":
                                ToggleScreenSaver();
                                break;
                            case "Keyboard":
                                TriggerOnScreenKeyboard();
                                break;
                            case "LegionTouchpad":
                                ToggleLegionTouchpad();
                                break;
                            case "LegionLightMode":
                                CycleLegionLightMode();
                                break;
                            case "LegionDesktopControls":
                                ToggleLegionDesktopControls();
                                break;
                            case "LegionRemapControls":
                                ToggleRemapControlsProfile();
                                break;
                            case "LegionChargeLimit":
                                ToggleLegionChargeLimit();
                                break;
                            // Action tiles
                            case "ActionTaskManager":
                                LaunchTaskManager();
                                break;
                            case "ActionExplorer":
                                LaunchExplorer();
                                break;
                            case "ActionEndTask":
                                SendAltF4();
                                break;
                            case "Fullscreen":
                                ToggleFullscreen();
                                break;
                            case "ActionHibernate":
                                ExecuteHibernate();
                                break;
                            case "LegionPowerLight":
                                ToggleLegionPowerLight();
                                break;
                            case "LegionFanFullSpeed":
                                ToggleLegionFanFullSpeed();
                                break;
                            case "ControllerEmulation":
                                ToggleControllerEmulation();
                                break;
                            case "MSIClawDesktopMode":
                                ToggleMSIClawDesktopMode();
                                break;
                            case "MsiCenter":
                                ToggleMsiCenter();
                                break;
                    }

                    // Update tile states after action
                    UpdateQuickSettingsTileStates();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error handling Quick Settings tile click: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Called when the helper fires a tile hotkey for a widget-side action (standard tile or
        /// action type 21-24). Simulates a tile button click by reusing the existing click dispatcher.
        /// </summary>
        // Set while a controller hotkey fires a tile action — tiles that normally open a
        // flyout dropdown switch to headless cycle behavior instead (no visual anchor needed).
        private bool _isControllerTriggered = false;

        private void SimulateTileHotkeyFired(string tileId)
        {
            if (string.IsNullOrEmpty(tileId)) return;
            Logger.Info($"SimulateTileHotkeyFired: '{tileId}'");

            // Left MSI button Action mode: "__action__{TileActionType int}"
            // Game Bar is NOT open when the physical button fires — skip Win+G close step.
            if (tileId.StartsWith("__action__"))
            {
                if (int.TryParse(tileId.Substring("__action__".Length), out int actionInt))
                {
                    var actionType = (XboxGamingBar.QuickSettings.TileActionType)actionInt;
                    string displayName = XboxGamingBar.QuickSettings.TileActionHelper.GetDisplayName(actionType);
                    Logger.Info($"SimulateTileHotkeyFired: dispatching physical-button action {actionType} ({displayName})");
                    _ = ExecutePhysicalButtonActionAsync(actionType, displayName);
                }
                return;
            }
            _isControllerTriggered = true;
            try
            {
                var fakeBtn = new Button { Tag = tileId };
                QuickSettingsTile_Click(fakeBtn, new Windows.UI.Xaml.RoutedEventArgs());
            }
            finally { _isControllerTriggered = false; }

            // Show the new tile state in the RTSS OSD so the user sees what changed.
            // UpdateQuickSettingsTileStates() has already run inside QuickSettingsTile_Click,
            // so the StateText now reflects the new state.
            if (qsTileMap.TryGetValue(tileId, out var tile) && tile.StateText != null)
            {
                string tileName  = tile.Name  ?? tileId;
                string stateText = tile.StateText.Text ?? "";
                string notif = string.IsNullOrEmpty(stateText) || stateText == tileName
                    ? tileName
                    : $"{tileName}\n{stateText}";
                _ = SendActionNotificationAsync(notif);
            }
        }

        private void CycleTDPMode()
        {
            if (TDPModeComboBox == null) return;

            // If default game profile is active, turn it off first
            if (defaultGameProfileEnabled?.Value == true && DefaultProfileToggle != null)
            {
                Logger.Info("TDP Mode tile clicked - turning off Default Game Profile");
                DefaultProfileToggle.IsOn = false;
            }

            bool isLegion = legionGoDetected?.Value == true;

            // ── Legion non-custom: hardware mode cycle (unchanged) ─────────────────
            if (isLegion && !(useCustomTDPPresets && tdpPresets != null && tdpPresets.Count > 0))
            {
                int[] modeValues = { 1, 2, 3, 255 };
                int currentMode = legionPerformanceMode?.Value ?? 2;
                int nextMode;
                switch (currentMode)
                {
                    case 1: nextMode = 2; break;
                    case 2: nextMode = 3; break;
                    case 3: nextMode = 255; break;
                    case 255: nextMode = 1; break;
                    default: nextMode = 2; break;
                }
                legionPerformanceMode?.SetValue(nextMode);
                int nextIdx = Array.IndexOf(modeValues, nextMode);
                if (nextIdx >= 0) { lastTDPModeIndex = nextIdx; TDPModeComboBox.SelectedIndex = nextIdx; }
                if (nextMode == 255) ScheduleQsTdpReapply();
                Logger.Info($"TDP Mode cycled (Legion): {currentMode} → {nextMode}");
                return;
            }

            // ── Non-Legion OR custom-presets: preset cycle via SelectionChanged ──────
            // Determine the ordered list of presets to cycle through.

            int itemCount = TDPModeComboBox.Items.Count;
            int presetCount;
            if (useCustomTDPPresets && tdpPresets != null && tdpPresets.Count > 0)
                presetCount = tdpPresets.Count;
            else
                presetCount = itemCount > 1 ? itemCount - 1 : itemCount;

            // Use lastTDPModeIndex as source of truth — SelectedIndex can be corrupted by
            // helper pipe echo-backs that remap the index differently after SetValue.
            int cur = lastTDPModeIndex >= 0 ? lastTDPModeIndex : TDPModeComboBox.SelectedIndex;

            // ── Diagnostic log (remove once cycle confirmed working) ──────────────
            Logger.Info($"[CycleTDP] ENTER — useCustomPresets={useCustomTDPPresets}, " +
                        $"tdpPresets.Count={tdpPresets?.Count ?? -1}, " +
                        $"itemCount={itemCount}, presetCount={presetCount}, " +
                        $"selectedIndex(cur)={cur}, lastTDPModeIndex={lastTDPModeIndex}, " +
                        $"tdp?={(tdp == null ? "NULL" : "ok")}, " +
                        $"isApplyingHelperUpdate={isApplyingHelperUpdate}, " +
                        $"isLoadingProfile={isLoadingProfile}, isInitialSync={isInitialSync}");

            if (presetCount <= 0) { Logger.Warn("[CycleTDP] presetCount=0 → abort"); return; }

            if (cur < 0 || cur >= presetCount) cur = 0;
            int next = (cur + 1) % presetCount;

            // Resolve preset TDP and name
            int presetTdp  = 0;
            int legionMode = 255;
            string name    = $"Preset {next}";

            if (useCustomTDPPresets && tdpPresets != null && next < tdpPresets.Count)
            {
                var p  = tdpPresets[next];
                presetTdp  = p.TdpWatts;
                legionMode = p.LegionModeValue ?? 255;
                name       = p.Name;
            }
            else
            {
                int[] defaultWatts = { 30, 25, 17, 12, 8 };
                string[] defaultNames = { "Max", "Standard", "Balanced", "Battery", "Super Battery" };
                if (next < defaultWatts.Length)  presetTdp = defaultWatts[next];
                if (next < defaultNames.Length)  name = defaultNames[next];
            }

            Logger.Info($"[CycleTDP] {cur} → {next} | preset='{name}' {presetTdp}W | legionMode={legionMode}");

            // Delegate to the same code path as SetTDPModeByIndex so SelectionChanged runs its
            // full TDP-apply logic (legionPerformanceMode, ForceSetValue, boost preset, profile save).
            // Do NOT pre-set lastTDPModeIndex here — SelectionChanged must see selectedIndex != last
            // in order to proceed past the equality guard.
            isUserInitiatedTDPModeChange = true;
            try { TDPModeComboBox.SelectedIndex = next; }
            finally { isUserInitiatedTDPModeChange = false; }

            Logger.Info($"[CycleTDP] after SelectedIndex={TDPModeComboBox.SelectedIndex}, lastTDPModeIndex={lastTDPModeIndex}");

            // Refresh Quick Settings tile label (SelectionChanged already handles the rest)
            UpdateQuickSettingsTileStates();
        }

        private void ToggleAutoTDPTile()
        {
            if (AutoTDPToggle != null)
            {
                AutoTDPToggle.IsOn = !AutoTDPToggle.IsOn;
                Logger.Info($"AutoTDP tile toggled to: {AutoTDPToggle.IsOn}");
            }
        }

        /// <summary>
        /// Called when the dropdown chevron on a split tile is tapped.
        /// The tag on the button is "{TileId}_dropdown".
        /// </summary>
        private void TileDropdown_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            string tag = btn.Tag as string ?? "";

            if (tag == "TDPMode_dropdown")
                ShowTDPDropdown(btn);
            else if (tag == "FPSCombined_dropdown")
                SwitchFPSCapMode();
            else if (tag == "IntelFpsTier_dropdown")
                ShowIntelFpsDropdown(btn);
            else if (tag == "Overlay_dropdown")
                ShowOverlayDropdown(btn);
        }

        /// <summary>
        /// Left sub-button on a dual-button tile — tag is "{TileId}_left".
        /// FPSCombined: switches between RTSS and Intel mode.
        /// </summary>
        private void TileLeftButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            string tag = btn.Tag as string ?? "";

            if (tag == "FPSCombined_left")
                SwitchFPSCapMode();
        }

        /// <summary>
        /// Right sub-button on a dual-button tile — tag is "{TileId}_right".
        /// FPSCombined: opens a dropdown with all available FPS values for the current mode.
        /// </summary>
        private void TileRightButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            string tag = btn.Tag as string ?? "";

            if (tag == "FPSCombined_right")
                ShowFPSCombinedDropdown(btn);
        }

        /// <summary>
        /// Opens a dynamic MenuFlyout with the FPS values for the currently active mode.
        /// RTSS: Off, 30, 40, 60, 90, 120 FPS
        /// Intel: Off, 60 FPS — Performance, 40 FPS — Balanced, 30 FPS — Efficiency
        /// </summary>
        private void ShowFPSCombinedDropdown(Button anchor)
        {
            bool isIntel = fpsCapMode?.Value == 1;
            var flyout = new MenuFlyout();

            if (isIntel)
            {
                if (intelFpsTier == null) return;
                int current = intelFpsTier.Value;
                string[] labels = { "Off", "60 FPS — Performance", "40 FPS — Balanced", "30 FPS — Efficiency" };
                for (int i = 0; i < labels.Length; i++)
                {
                    int tier = i;
                    var item = new MenuFlyoutItem { Text = labels[i] };
                    if (tier == current)
                        item.Icon = new FontIcon { Glyph = "", FontSize = 12 };  // checkmark
                    item.Click += (s, ev) =>
                    {
                        intelFpsTier.SetValue(tier);
                        UpdateQuickSettingsTileStates();
                        if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile && !isRestoringFromDefaultProfile)
                            SaveCurrentSettingsToProfile(currentProfileName);
                    };
                    flyout.Items.Add(item);
                }
            }
            else
            {
                if (fpsLimit == null) return;
                int current = fpsLimit.Value;
                int[] values = { 0, 30, 40, 60, 90, 120 };
                string[] labels = { "Off", "30 FPS", "40 FPS", "60 FPS", "90 FPS", "120 FPS" };
                for (int i = 0; i < values.Length; i++)
                {
                    int fps = values[i];
                    var item = new MenuFlyoutItem { Text = labels[i] };
                    if (fps == current)
                        item.Icon = new FontIcon { Glyph = "", FontSize = 12 };  // checkmark
                    item.Click += (s, ev) =>
                    {
                        fpsLimit.SetValue(fps);
                        // Sync the Performance tab controls
                        isApplyingHelperUpdate = true;
                        try
                        {
                            if (fps > 0) { FPSLimitToggle.IsOn = true; FPSLimitSlider.Value = fps; }
                            else         { FPSLimitToggle.IsOn = false; }
                        }
                        finally { isApplyingHelperUpdate = false; }
                        UpdateQuickSettingsTileStates();
                        if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile && !isRestoringFromDefaultProfile)
                            SaveCurrentSettingsToProfile(currentProfileName);
                    };
                    flyout.Items.Add(item);
                }
            }

            flyout.ShowAt(anchor);
        }

        /// <summary>
        /// Switch FPS cap mode between RTSS and Intel.
        /// Mirrors the mutual-exclusion logic from FPSModeRadio_Changed:
        /// disables the old limiter immediately so it doesn't overlap with the new mode.
        /// </summary>
        private void SwitchFPSCapMode()
        {
            if (fpsCapMode == null) return;

            bool switchToIntel = fpsCapMode.Value == 0;  // currently RTSS → switch to Intel
            fpsCapMode.SetValue(switchToIntel ? 1 : 0);
            Logger.Info($"FPS mode switched from tile to {(switchToIntel ? "Intel" : "RTSS")}");

            // Mutual exclusion: disable old limiter, reset new one to 60fps
            if (switchToIntel)
            {
                Logger.Info("[FPS-Mode] Tile: switching to Intel, disabling RTSS, setting Intel to 60fps");
                fpsLimit?.SetValue(0);
                intelFpsTier?.SetValue(1); // Performance = 60fps
                isApplyingHelperUpdate = true;
                try
                {
                    if (IntelFpsTierComboBox != null) IntelFpsTierComboBox.SelectedIndex = 1;
                    if (FPSLimitToggle != null) FPSLimitToggle.IsOn = true;
                }
                finally { isApplyingHelperUpdate = false; }
            }
            else
            {
                Logger.Info("[FPS-Mode] Tile: switching to RTSS, disabling Intel, setting RTSS to 60fps");
                intelFpsTier?.SetValue(0);
                fpsLimit?.SetValue(60);
                isApplyingHelperUpdate = true;
                try
                {
                    if (IntelFpsTierComboBox != null) IntelFpsTierComboBox.SelectedIndex = 0;
                    if (FPSLimitSlider != null) FPSLimitSlider.Value = 60;
                    if (FPSLimitToggle != null) FPSLimitToggle.IsOn = true;
                }
                finally { isApplyingHelperUpdate = false; }
            }

            // Sync Performance tab radio button state
            UpdateFPSLimiterPanels();
            UpdateFPSCapDisplayText();
            UpdateQuickSettingsTileStates();

            // Persist to profile
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile && !isRestoringFromDefaultProfile)
                SaveCurrentSettingsToProfile(currentProfileName);
        }

        /// <summary>
        /// Cycle FPS values for whichever mode (RTSS or Intel) is currently active.
        /// Called by both the main tile click and the right sub-button.
        /// </summary>
        private void CycleFPSForCurrentMode()
        {
            bool isIntel = fpsCapMode?.Value == 1;
            if (isIntel)
                CycleIntelFpsTier();
            else
                CycleFPSLimit();
        }

        /// <summary>
        /// Opens a MenuFlyout listing all TDP presets / modes for direct selection.
        /// The currently active mode is indicated with a checkmark icon.
        /// </summary>
        private void ShowTDPDropdown(Button anchor)
        {
            var flyout = new MenuFlyout();
            int currentIndex = TDPModeComboBox?.SelectedIndex ?? 0;

            if (useCustomTDPPresets && tdpPresets != null && tdpPresets.Count > 0)
            {
                for (int i = 0; i < tdpPresets.Count; i++)
                {
                    int capturedIndex = i;
                    var preset = tdpPresets[i];
                    string label = preset.TdpWatts > 0
                        ? $"{preset.Name} ({preset.TdpWatts}W)"
                        : preset.Name;
                    var item = new MenuFlyoutItem { Text = label };
                    if (i == currentIndex)
                        item.Icon = new FontIcon { Glyph = "", FontSize = 12 }; // ✓
                    item.Click += (s, ev) => SetTDPModeByIndex(capturedIndex);
                    flyout.Items.Add(item);
                }
                int sliderIdx = tdpPresets.Count;
                var sliderItem = new MenuFlyoutItem { Text = "Slider (Manual)" };
                if (currentIndex == sliderIdx)
                    sliderItem.Icon = new FontIcon { Glyph = "", FontSize = 12 };
                sliderItem.Click += (s, ev) => SetTDPModeByIndex(sliderIdx);
                flyout.Items.Add(sliderItem);
            }
            else if (legionGoDetected?.Value == true)
            {
                string[] modes = { "Quiet", "Balanced", "Performance", "Slider" };
                for (int i = 0; i < modes.Length; i++)
                {
                    int capturedIndex = i;
                    var item = new MenuFlyoutItem { Text = modes[i] };
                    if (i == currentIndex)
                        item.Icon = new FontIcon { Glyph = "", FontSize = 12 };
                    item.Click += (s, ev) => SetTDPModeByIndex(capturedIndex);
                    flyout.Items.Add(item);
                }
            }
            else
            {
                // MSI Claw default modes
                string[] modes = { "Max (30W)", "Standard (25W)", "Balanced (17W)", "Battery (12W)", "Super Battery (8W)", "Slider" };
                for (int i = 0; i < modes.Length; i++)
                {
                    int capturedIndex = i;
                    var item = new MenuFlyoutItem { Text = modes[i] };
                    if (i == currentIndex)
                        item.Icon = new FontIcon { Glyph = "", FontSize = 12 }; // ✓ active mode
                    item.Click += (s, ev) => SetTDPModeByIndex(capturedIndex);
                    flyout.Items.Add(item);
                }
            }

            flyout.ShowAt(anchor);
        }

        /// <summary>
        /// Opens a MenuFlyout listing the four Intel IGCL Endurance Gaming tiers for direct selection.
        /// </summary>
        private void ShowIntelFpsDropdown(Button anchor)
        {
            if (intelFpsTier == null) return;

            var flyout = new MenuFlyout();
            int current = intelFpsTier.Value;

            string[] labels = { "Off", "60 FPS — Performance", "40 FPS — Balanced", "30 FPS — Efficiency" };
            for (int i = 0; i < labels.Length; i++)
            {
                int tier = i;
                var item = new MenuFlyoutItem { Text = labels[i] };
                if (tier == current)
                    item.Icon = new FontIcon { Glyph = "", FontSize = 12 };
                item.Click += (s, ev) =>
                {
                    intelFpsTier.SetValue(tier);
                    UpdateQuickSettingsTileStates();
                };
                flyout.Items.Add(item);
            }

            flyout.ShowAt(anchor);
        }

        /// <summary>
        /// Opens a MenuFlyout listing RTSS overlay levels (0–4) for direct selection.
        /// </summary>
        private void ShowOverlayDropdown(Button anchor)
        {
            if (osd == null) return;

            var flyout = new MenuFlyout();
            int current = (int)osd.Value;

            string[] labels = { "Off", "Basic", "Horizontal", "H. Detailed", "Full" };
            for (int i = 0; i < labels.Length; i++)
            {
                int level = i;
                var item = new MenuFlyoutItem { Text = labels[i] };
                if (level == current)
                    item.Icon = new FontIcon { Glyph = "", FontSize = 12 };
                item.Click += (s, ev) =>
                {
                    osd.SetValue(level);
                    UpdateQuickSettingsTileStates();
                };
                flyout.Items.Add(item);
            }

            flyout.ShowAt(anchor);
        }

        /// <summary>
        /// Directly applies TDP mode by ComboBox index — used by the dropdown for direct selection.
        /// Mirrors CycleTDPMode() but for a specific target index instead of next-in-cycle.
        /// </summary>
        private void SetTDPModeByIndex(int index)
        {
            // Turn off default game profile if active (same guard as CycleTDPMode)
            if (defaultGameProfileEnabled?.Value == true && DefaultProfileToggle != null)
            {
                Logger.Info("TDP Mode dropdown selection - turning off Default Game Profile");
                DefaultProfileToggle.IsOn = false;
            }

            bool isLegion = legionGoDetected?.Value == true;

            // Apply via ComboBox — SelectionChanged handler applies the actual TDP to hardware
            if (TDPModeComboBox != null)
            {
                isUserInitiatedTDPModeChange = true;
                try { TDPModeComboBox.SelectedIndex = index; }
                finally { isUserInitiatedTDPModeChange = false; }
            }

            // For Legion: also set the hardware performance mode property
            if (isLegion && legionPerformanceMode != null)
            {
                int[] modeValues = { 1, 2, 3, 255 };
                int legionMode = (index >= 0 && index < modeValues.Length) ? modeValues[index] : 255;
                legionPerformanceMode.SetValue(legionMode);
                if (legionMode == 255)
                    ScheduleQsTdpReapply();
            }

            UpdateQuickSettingsTileStates();
            Logger.Info($"TDP Mode set from dropdown: index={index}, isLegion={isLegion}");
        }

        private void ScheduleQsTdpReapply()
        {
            try
            {
                // Cancel existing timer
                if (qsTdpReapplyTimer != null)
                {
                    qsTdpReapplyTimer.Stop();
                }

                // Create new timer
                qsTdpReapplyTimer = new Windows.UI.Xaml.DispatcherTimer();
                qsTdpReapplyTimer.Interval = TimeSpan.FromSeconds(5);
                qsTdpReapplyTimer.Tick += async (s, e) =>
                {
                    qsTdpReapplyTimer.Stop();
                    // Reapply TDP - still in Custom/Slider mode?
                    bool isCustomMode = IsCustomTdpModeSelected();
                    if (isCustomMode)
                    {
                        // Read TDP value NOW (at timer fire time), not when scheduled
                        // This ensures we use the current profile's TDP if profile switched
                        int currentTdpValue = (int)(TDPSlider?.Value ?? 15);

                        // Ask helper to re-push current TDP to hardware. The previous
                        // N-1/N trick corrupted global.xml by briefly writing TDP-1.
                        try
                        {
                            if (App.IsConnected)
                            {
                                var request = new Windows.Foundation.Collections.ValueSet();
                                request.Add("ReapplyTDP", true);
                                await App.SendMessageAsync(request);
                                Logger.Info($"Quick Settings: Asked helper to reapply current TDP ({currentTdpValue}W)");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Quick Settings: ReapplyTDP send failed: {ex.Message}");
                        }
                    }
                };
                qsTdpReapplyTimer.Start();
                Logger.Info($"Quick Settings: Scheduled TDP reapply in 5 seconds");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scheduling TDP reapply: {ex.Message}");
            }
        }

        private void TogglePerGameProfile()
        {
            // If Default Game Profile is active, toggle it off instead
            if (defaultGameProfileEnabled?.Value == true && DefaultProfileToggle != null)
            {
                Logger.Info("Profile tile clicked - turning off Default Game Profile");
                DefaultProfileToggle.IsOn = false;
                return;
            }

            // Only allow toggling when a game is detected
            if (perGameProfile != null && runningGame != null && runningGame.Value.IsValid())
            {
                bool newValue = !perGameProfile.Value;
                isUserInitiatedProfileToggle = true; // Flag this as user-initiated
                perGameProfile.SetValue(newValue);
                isUserInitiatedProfileToggle = false;
                Logger.Info($"Per-game profile toggled to {newValue}");
            }
            else
            {
                Logger.Info("Per-game profile toggle ignored - no game detected");
            }
        }

        private async void TriggerOnScreenKeyboard()
        {
            await ToggleTouchKeyboard();
        }

        /// <summary>
        /// Toggle the Windows touch keyboard using COM interop
        /// </summary>
        private async Task ToggleTouchKeyboard()
        {
            try
            {
                // Use helper to toggle touch keyboard via COM interop
                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet { { "ToggleTouchKeyboard", true } };
                    await App.SendMessageAsync(message);
                    Logger.Info("Touch keyboard toggle requested via helper");
                }
                else
                {
                    // Fallback to Win+Ctrl+O (accessibility keyboard shortcut)
                    QuickSettings.KeyboardShortcutHelper.SendShortcut("Win+Ctrl+O");
                    Logger.Info("On-screen keyboard triggered via Win+Ctrl+O (fallback)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling touch keyboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle RTSS OSD between off and last used level
        /// </summary>
        private void ToggleRTSSOsd()
        {
            try
            {
                if (osd == null)
                {
                    Logger.Warn("ToggleRTSSOsd: osd property is null");
                    return;
                }

                int currentLevel = (int)osd.Value;

                if (currentLevel > 0)
                {
                    // Currently on - save level and turn off
                    lastNonZeroOsdLevel = currentLevel;
                    osd.SetValue(0);
                    Logger.Info($"RTSS OSD toggled OFF (was level {currentLevel})");
                }
                else
                {
                    // Currently off - restore to last level
                    osd.SetValue(lastNonZeroOsdLevel);
                    Logger.Info($"RTSS OSD toggled ON to level {lastNonZeroOsdLevel}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling RTSS OSD: {ex.Message}");
            }
        }

        /// <summary>
        /// Launch Task Manager via helper
        /// </summary>
        private void LaunchTaskManager()
        {
            try
            {
                _ = SendKeyboardShortcutViaHelper("Ctrl+Shift+Escape");
                Logger.Info("Task Manager launched via Ctrl+Shift+Escape");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching Task Manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Launch File Explorer via helper
        /// </summary>
        private void LaunchExplorer()
        {
            try
            {
                _ = SendKeyboardShortcutViaHelper("Win+E");
                Logger.Info("Explorer launched via Win+E");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching Explorer: {ex.Message}");
            }
        }

        /// <summary>
        /// Close the foreground game window
        /// Uses Alt+Tab to switch to game, then Alt+F4 to close it
        /// </summary>
        private async void SendAltF4()
        {
            try
            {
                // Alt+Tab to switch focus to the game (away from Game Bar)
                _ = SendKeyboardShortcutViaHelper("Alt+Tab");
                Logger.Info("Alt+Tab sent to focus game");

                // Wait for focus switch
                await Task.Delay(200);

                // Now send Alt+F4 to close the focused game
                _ = SendKeyboardShortcutViaHelper("Alt+F4");
                Logger.Info("Alt+F4 sent to close game");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error closing game: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle fullscreen via F11
        /// Uses Alt+Tab first to focus the game
        /// </summary>
        private async void ToggleFullscreen()
        {
            try
            {
                // Alt+Tab to switch focus to the game (away from Game Bar)
                _ = SendKeyboardShortcutViaHelper("Alt+Tab");
                Logger.Info("Alt+Tab sent to focus game");

                // Wait for focus switch
                await Task.Delay(200);

                // F11 is the most universal fullscreen toggle
                _ = SendKeyboardShortcutViaHelper("F11");
                Logger.Info("Fullscreen toggled via F11");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling fullscreen: {ex.Message}");
            }
        }

        // Resolutions to exclude from quick cycling (odd resolutions that don't scale well)
        private static readonly HashSet<string> excludedQuickResolutions = new HashSet<string>
        {
            "1680x1050"  // Odd 16:10 resolution that doesn't scale cleanly
        };

        private void CycleResolution()
        {
            if (resolution != null && resolutions?.Value != null && resolutions.Value.Count > 0)
            {
                // Filter out excluded resolutions for quick cycling
                var quickResolutions = resolutions.Value
                    .Where(r => !excludedQuickResolutions.Contains(r))
                    .ToList();

                if (quickResolutions.Count == 0)
                {
                    quickResolutions = resolutions.Value; // Fallback to all if filter removes everything
                }

                string currentRes = resolution.Value;
                int currentIndex = quickResolutions.IndexOf(currentRes);

                // If current resolution is not in quick list, start from first
                if (currentIndex < 0) currentIndex = -1;

                int nextIndex = (currentIndex + 1) % quickResolutions.Count;
                string nextRes = quickResolutions[nextIndex];
                resolution.SetValue(nextRes);
                Logger.Info($"Resolution cycled from {currentRes} to {nextRes}");
            }
        }

        /// <summary>
        /// Cycles display orientation between Landscape (0) and Portrait (1).
        /// </summary>
        private void CycleRotation()
        {
            if (displayOrientation != null)
            {
                int currentOrientation = displayOrientation.Value;
                // Cycle between Landscape (0) and Portrait (1)
                // Skip flipped modes (2, 3) for simple toggle behavior
                int nextOrientation = (currentOrientation == 0) ? 1 : 0;
                displayOrientation.SetValue(nextOrientation);
                Logger.Info($"Display orientation cycled from {currentOrientation} to {nextOrientation}");
            }
        }

        private void ToggleHDR()
        {
            if (hdrEnabled != null && (hdrSupported?.Value ?? false))
            {
                bool newValue = !hdrEnabled.Value;
                hdrEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (HDRToggle != null)
                    HDRToggle.IsOn = newValue;
                Logger.Info($"HDR toggled to {newValue}");
            }
        }

        private void ToggleLosslessScaling()
        {
            if (losslessScalingEnabled != null)
            {
                bool newValue = !losslessScalingEnabled.Value;
                losslessScalingEnabled.SetValue(newValue);
                Logger.Info($"Lossless Scaling toggled to {newValue}");
            }
        }

        private void ToggleAFMF()
        {
            if (amdFluidMotionFrameEnabled != null && (amdFluidMotionFrameSupported?.Value ?? false))
            {
                bool newValue = !amdFluidMotionFrameEnabled.Value;
                amdFluidMotionFrameEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDFluidMotionFrameToggle != null)
                    AMDFluidMotionFrameToggle.IsOn = newValue;
                Logger.Info($"AFMF toggled to {newValue}");
            }
        }

        private void ToggleRSR()
        {
            if (amdRadeonSuperResolutionEnabled != null && (amdRadeonSuperResolutionSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonSuperResolutionEnabled.Value;
                amdRadeonSuperResolutionEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonSuperResolutionToggle != null)
                    AMDRadeonSuperResolutionToggle.IsOn = newValue;
                Logger.Info($"RSR toggled to {newValue}");
            }
        }

        private void ToggleRIS()
        {
            if (amdImageSharpeningEnabled != null && (amdImageSharpeningSupported?.Value ?? false))
            {
                bool newValue = !amdImageSharpeningEnabled.Value;
                amdImageSharpeningEnabled.SetValue(newValue);
                AMDImageSharpeningToggle.IsOn = newValue;
                Logger.Info($"RIS toggled to {newValue}");
            }
        }

        private void ToggleAntiLag()
        {
            if (amdRadeonAntiLagEnabled != null && (amdRadeonAntiLagSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonAntiLagEnabled.Value;
                amdRadeonAntiLagEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonAntiLagToggle != null)
                    AMDRadeonAntiLagToggle.IsOn = newValue;
                Logger.Info($"Anti-Lag toggled to {newValue}");
            }
        }

        private void ToggleRadeonChill()
        {
            if (amdRadeonChillEnabled != null && (amdRadeonChillSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonChillEnabled.Value;
                amdRadeonChillEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonChillToggle != null)
                    AMDRadeonChillToggle.IsOn = newValue;
                Logger.Info($"Radeon Chill toggled to {newValue}");
            }
        }

        private void ToggleCPUBoost()
        {
            if (cpuBoost != null)
            {
                bool newValue = !cpuBoost.Value;
                cpuBoost.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (CPUBoostToggle != null)
                    CPUBoostToggle.IsOn = newValue;
                Logger.Info($"CPU Boost toggled to {newValue}");
            }
        }

        private void CyclePowerMode()
        {
            if (osPowerMode != null)
            {
                // Cycle: Efficiency (0) -> Balanced (1) -> Performance (2) -> Efficiency (0)
                int currentMode = osPowerMode.Value;
                int nextMode = (currentMode + 1) % 3;
                osPowerMode.SetValue(nextMode);

                // Update the combobox and value text in Performance tab
                isLoadingOSPowerMode = true;
                try
                {
                    OSPowerModeComboBox.SelectedIndex = nextMode;
                    OSPowerModeValue.Text = OSPowerModeNames[nextMode];
                }
                finally
                {
                    isLoadingOSPowerMode = false;
                }

                Logger.Info($"Power Mode cycled to {OSPowerModeNames[nextMode]}");

                // Save the change to profile
                if (!isInitialSync && !isApplyingHelperUpdate && !isLoadingProfile && SaveOSPowerMode)
                {
                    Logger.Info($"Saving OS Power Mode change to profile: {currentProfileName}");
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
            }
        }

        private void CycleEPP()
        {
            if (cpuEPP != null)
            {
                int currentValue = (int)cpuEPP.Value;
                int nextValue;
                switch (currentValue)
                {
                    case 0: nextValue = 30; break;
                    case 30: nextValue = 80; break;
                    case 80: nextValue = 100; break;
                    case 100: nextValue = 0; break;
                    default: nextValue = 0; break;
                }
                cpuEPP.SetValue(nextValue);

                // Update slider to match (SaveCurrentSettingsToProfile reads from it)
                if (CPUEPPSlider != null)
                {
                    CPUEPPSlider.Value = nextValue;
                }

                Logger.Info($"EPP cycled from {currentValue} to {nextValue}");

                // Save the change to profile
                // Use direct save to bypass isApplyingHelperUpdate check - this is a user-initiated action
                if (!isInitialSync && !isLoadingProfile && SaveCPUEPP && !string.IsNullOrEmpty(currentProfileName))
                {
                    try
                    {
                        var profile = GetProfile(currentProfileName);
                        profile.CPUEPP = nextValue;
                        SaveProfileToStorage(currentProfileName, profile);
                        Logger.Info($"Saved EPP {nextValue} to profile: {currentProfileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to save EPP to profile: {ex.Message}");
                    }
                }
            }
        }

        private void CyclePerformanceOverlay()
        {
            if (osdProvider == 1) // AMD
            {
                // AMD has 4 overlay levels that cycle with Ctrl+Shift+X
                // Ctrl+Shift+O toggles the overlay on/off completely
                // Cycle: Off -> Level 1 -> Level 2 -> Level 3 -> Level 4 -> Off
                if (amdOverlayLevel == 0)
                {
                    // Currently off, turn on (starts at level 1)
                    SendAMDOverlayToggle();
                    amdOverlayLevel = 1;
                    SaveAMDOverlayLevel();
                    Logger.Info("AMD Overlay toggled ON (Level 1)");
                }
                else if (amdOverlayLevel < 4)
                {
                    // Cycle to next level
                    CycleAMDOverlayLevel();
                    amdOverlayLevel++;
                    SaveAMDOverlayLevel();
                    Logger.Info($"AMD Overlay cycled to Level {amdOverlayLevel}");
                }
                else
                {
                    // At level 4, turn off
                    SendAMDOverlayToggle();
                    amdOverlayLevel = 0;
                    SaveAMDOverlayLevel();
                    Logger.Info("AMD Overlay toggled OFF");
                }
                UpdateQuickSettingsTileStates();
            }
            else // RTSS
            {
                if (osd != null)
                {
                    int currentLevel = (int)osd.Value;
                    int nextLevel = (currentLevel + 1) % 5;  // 0=Off, 1=Basic, 2=Horizontal, 3=H.Detailed, 4=Full
                    osd.SetValue(nextLevel);
                    Logger.Info($"RTSS Performance Overlay cycled from {currentLevel} to {nextLevel}");
                }
            }
        }

        /// <summary>
        /// Cycle FPS limit through: Off -> 30 -> 40 -> 60 -> 90 -> Off (fixed ascending values)
        /// </summary>
        private void CycleFPSLimit()
        {
            if (fpsLimit == null) return;

            // Get max refresh rate for slider UI sync only
            int maxRefresh = 60; // Default
            if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
            {
                maxRefresh = refreshRates.Value.Max();
            }

            // Fixed FPS cap values in ascending order
            int[] fpsValues = new int[]
            {
                0,   // Off (unlimited)
                30,  // 30 FPS — power saving
                40,  // 40 FPS — balanced
                60,  // 60 FPS — smooth
                90,  // 90 FPS — high performance
                120  // 120 FPS — high refresh
            };

            // Find current index and cycle to next
            int currentLimit = fpsLimit.Value;
            int currentIndex = 0;
            for (int i = 0; i < fpsValues.Length; i++)
            {
                if (fpsValues[i] == currentLimit)
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = (currentIndex + 1) % fpsValues.Length;
            int nextLimit = fpsValues[nextIndex];

            fpsLimit.SetValue(nextLimit);
            Logger.Info($"FPS Limit cycled from {currentLimit} to {nextLimit} (max refresh: {maxRefresh})");

            // Sync the Performance tab FPS Limit controls
            isApplyingHelperUpdate = true;
            try
            {
                // Update slider maximum to current refresh rate
                FPSLimitSlider.Maximum = maxRefresh;

                if (nextLimit > 0)
                {
                    FPSLimitToggle.IsOn = true;
                    FPSLimitSlider.Value = nextLimit;
                }
                else
                {
                    FPSLimitToggle.IsOn = false;
                }
            }
            finally
            {
                isApplyingHelperUpdate = false;
            }

            // Save to profile if FPS Limit saving is enabled
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        /// <summary>
        /// Cycle Intel IGCL Endurance Gaming tier: Off -> Performance(60) -> Balanced(40) -> Efficiency(30) -> Off.
        /// Mutual exclusion (disable RTSS limit when Intel activates, and vice versa) is enforced
        /// by the helper's FPSLimit_PropertyChanged / IntelFpsTier_PropertyChanged callbacks.
        /// Ported from IntelGameBar.
        /// </summary>
        private void CycleIntelFpsTier()
        {
            if (intelFpsTier == null) return;

            // Tiers: 0=Off, 1=Performance(60fps), 2=Balanced(40fps), 3=Efficiency(30fps)
            int currentTier = intelFpsTier.Value;
            int nextTier = (currentTier + 1) % 4;
            intelFpsTier.SetValue(nextTier);

            string[] tierLabels = { "Off", "Perf 60", "Bal 40", "Eff 30" };
            Logger.Info($"Intel FPS tier cycled from {currentTier} ({tierLabels[currentTier]}) to {nextTier} ({tierLabels[nextTier]})");
        }

        /// <summary>
        /// FPS Limit toggle changed - set FPS limit to slider value or 0 (off)
        /// </summary>
        private void FPSLimitToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isApplyingHelperUpdate) return;

            bool isIntelMode = fpsCapMode?.Value == 1;

            if (FPSLimitToggle.IsOn)
            {
                if (isIntelMode)
                {
                    // Intel mode: enable at current tier, default to Performance (60fps) if off
                    int tier = intelFpsTier?.Value ?? 0;
                    if (tier == 0) tier = 1; // default to Performance (60fps)
                    intelFpsTier?.SetValue(tier);
                    isApplyingHelperUpdate = true;
                    try
                    {
                        if (IntelFpsTierComboBox != null) IntelFpsTierComboBox.SelectedIndex = tier;
                    }
                    finally { isApplyingHelperUpdate = false; }
                    if (FPSLimitValue != null) FPSLimitValue.Text = tier == 1 ? "60 FPS" : tier == 2 ? "40 FPS" : "30 FPS";
                    Logger.Info($"FPS Limit enabled (Intel mode): tier {tier}");
                }
                else
                {
                    // RTSS mode: enable at slider value, default to 60fps if unset
                    int maxRefresh = 60;
                    if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
                        maxRefresh = refreshRates.Value.Max();
                    FPSLimitSlider.Maximum = maxRefresh;
                    int limit = (int)FPSLimitSlider.Value;
                    if (limit <= 15) { limit = 60; FPSLimitSlider.Value = limit; }
                    fpsLimit?.SetValue(limit);
                    if (FPSLimitValue != null) FPSLimitValue.Text = $"{limit} FPS";
                    Logger.Info($"FPS Limit enabled (RTSS mode): {limit}fps");
                }
            }
            else
            {
                // Toggle OFF — disable only the currently active limiter, leave fpsCapMode unchanged
                if (isIntelMode)
                {
                    intelFpsTier?.SetValue(0);
                    isApplyingHelperUpdate = true;
                    try
                    {
                        if (IntelFpsTierComboBox != null) IntelFpsTierComboBox.SelectedIndex = 0;
                    }
                    finally { isApplyingHelperUpdate = false; }
                    Logger.Info("FPS Limit disabled (Intel mode): cleared Intel tier");
                }
                else
                {
                    fpsLimit?.SetValue(0);
                    Logger.Info("FPS Limit disabled (RTSS mode): cleared RTSS limit");
                }
            }

            // Update the FPS limiter panels (show/hide RTSS or Intel section)
            UpdateFPSLimiterPanels();

            // Save to profile if FPS Limit saving is enabled
            // Don't save during DGP restoration - values being restored to original state
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile && !isRestoringFromDefaultProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        /// <summary>
        /// FPS limiter mode radio changed (RTSS ↔ Intel).
        /// Updates panel visibility and sends the new mode to the helper.
        /// </summary>
        private void FPSModeRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (isUpdatingFPSModeUI || isApplyingHelperUpdate || isLoadingProfile) return;

            bool isIntel = FPSModeIntelRadio?.IsChecked == true;

            // Show the matching sub-panel
            if (FPSLimiterRTSSPanel != null)
                FPSLimiterRTSSPanel.Visibility = isIntel ? Visibility.Collapsed : Visibility.Visible;
            if (FPSLimiterIntelPanel != null)
                FPSLimiterIntelPanel.Visibility = isIntel ? Visibility.Visible : Visibility.Collapsed;

            // Push the new mode to the helper
            if (fpsCapMode != null)
            {
                fpsCapMode.SetValue(isIntel ? 1 : 0);
                Logger.Info($"FPS limiter mode changed to {(isIntel ? "Intel" : "RTSS")}");
            }

            // Mutual exclusion: disable the old limiter and reset the new one to 60 fps.
            if (isIntel)
            {
                // Switching to Intel — disable RTSS, start Intel at 60fps (Performance tier 1)
                Logger.Info("[FPS-Mode] Switching to Intel: disabling RTSS, setting Intel to 60fps");
                if (fpsLimit != null)
                    fpsLimit.SetValue(0);
                intelFpsTier?.SetValue(1);
                isApplyingHelperUpdate = true;
                try
                {
                    if (IntelFpsTierComboBox != null) IntelFpsTierComboBox.SelectedIndex = 1;
                    if (FPSLimitToggle != null) FPSLimitToggle.IsOn = true;
                }
                finally { isApplyingHelperUpdate = false; }
            }
            else
            {
                // Switching to RTSS — disable Intel, start RTSS at 60fps
                Logger.Info("[FPS-Mode] Switching to RTSS: disabling Intel, setting RTSS to 60fps");
                if (intelFpsTier != null)
                    intelFpsTier.SetValue(0);
                fpsLimit?.SetValue(60);
                isApplyingHelperUpdate = true;
                try
                {
                    if (IntelFpsTierComboBox != null) IntelFpsTierComboBox.SelectedIndex = 0;
                    if (FPSLimitSlider != null) FPSLimitSlider.Value = 60;
                    if (FPSLimitToggle != null) FPSLimitToggle.IsOn = true;
                }
                finally { isApplyingHelperUpdate = false; }
            }

            // Update the green FPS display to reflect the now-active limiter
            UpdateFPSCapDisplayText();

            // Persist to profile
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile && !isRestoringFromDefaultProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        /// <summary>
        /// Guard flag to prevent FPSModeRadio_Changed from firing when UpdateFPSLimiterPanels
        /// programmatically syncs the radio button state from fpsCapMode.Value.
        /// </summary>
        private bool isUpdatingFPSModeUI = false;

        /// <summary>
        /// Syncs the FPS limiter panel visibility (show/hide mode panel, RTSS vs Intel sub-panels)
        /// based on FPSLimitToggle.IsOn and fpsCapMode.Value.
        /// Call whenever the toggle state or fpsCapMode changes.
        /// </summary>
        private void UpdateFPSLimiterPanels()
        {
            if (FPSLimiterModePanel == null) return;

            bool isOn = FPSLimitToggle?.IsOn == true;
            FPSLimiterModePanel.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;

            if (!isOn) return;

            bool isIntel = fpsCapMode?.Value == 1;

            if (FPSLimiterRTSSPanel != null)
                FPSLimiterRTSSPanel.Visibility = isIntel ? Visibility.Collapsed : Visibility.Visible;
            if (FPSLimiterIntelPanel != null)
                FPSLimiterIntelPanel.Visibility = isIntel ? Visibility.Visible : Visibility.Collapsed;

            // Sync radio buttons without re-triggering FPSModeRadio_Changed
            isUpdatingFPSModeUI = true;
            try
            {
                if (FPSModeIntelRadio != null) FPSModeIntelRadio.IsChecked = isIntel;
                if (FPSModeRTSSRadio != null)  FPSModeRTSSRadio.IsChecked  = !isIntel;
            }
            finally
            {
                isUpdatingFPSModeUI = false;
            }

            // Update the green FPS cap number above the toggle to reflect the active limiter
            UpdateFPSCapDisplayText();
        }

        /// <summary>
        /// Updates the green FPS value display (FPSLimitValue) above the toggle.
        /// In RTSS mode: shows the slider value (already kept up-to-date by FPSLimitSlider_ValueChanged).
        /// In Intel mode: shows the tier's fixed FPS (60 / 40 / 30) instead of the stale RTSS value.
        /// </summary>
        private void UpdateFPSCapDisplayText()
        {
            if (FPSLimitValue == null || FPSLimitToggle?.IsOn != true) return;

            bool isIntel = fpsCapMode?.Value == 1;
            if (isIntel && IntelFpsTierComboBox != null)
            {
                int tier = IntelFpsTierComboBox.SelectedIndex;
                string intelText = tier == 1 ? "60 FPS"
                                 : tier == 2 ? "40 FPS"
                                 : tier == 3 ? "30 FPS"
                                 : "Intel";   // tier 0 = Off, shouldn't normally be shown active
                FPSLimitValue.Text = intelText;
            }
            // RTSS mode: FPSLimitSlider_ValueChanged already keeps the text current — nothing to do
        }

        /// <summary>
        /// RSR toggle changed - disable RIS if RSR is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonSuperResolutionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // RSR and RIS are mutually exclusive - enabling one disables the other
            if (AMDRadeonSuperResolutionToggle.IsOn && AMDImageSharpeningToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("RSR enabled - disabling RIS (mutually exclusive)");
                AMDImageSharpeningToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// RIS toggle changed - disable RSR if RIS is enabled (mutually exclusive)
        /// </summary>
        private void AMDImageSharpeningToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // RSR and RIS are mutually exclusive - enabling one disables the other
            if (AMDImageSharpeningToggle.IsOn && AMDRadeonSuperResolutionToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("RIS enabled - disabling RSR (mutually exclusive)");
                AMDRadeonSuperResolutionToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Anti-Lag toggle changed - disable Chill if Anti-Lag is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonAntiLagToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Anti-Lag and Chill are mutually exclusive
            if (AMDRadeonAntiLagToggle.IsOn && AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("Anti-Lag enabled - disabling Chill (mutually exclusive)");
                AMDRadeonChillToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Boost toggle changed - disable Chill if Boost is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Boost and Chill are mutually exclusive
            if (AMDRadeonBoostToggle.IsOn && AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("Boost enabled - disabling Chill (mutually exclusive)");
                AMDRadeonChillToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Chill toggle changed - disable Anti-Lag and Boost if Chill is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonChillToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Chill is mutually exclusive with Anti-Lag and Boost
            if (AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                if (AMDRadeonAntiLagToggle.IsOn)
                {
                    Logger.Info("Chill enabled - disabling Anti-Lag (mutually exclusive)");
                    AMDRadeonAntiLagToggle.IsOn = false;
                }
                if (AMDRadeonBoostToggle.IsOn)
                {
                    Logger.Info("Chill enabled - disabling Boost (mutually exclusive)");
                    AMDRadeonBoostToggle.IsOn = false;
                }
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// FPS Limit slider changed - update FPS limit if toggle is on (with debouncing)
        /// </summary>
        private void FPSLimitSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Always update the display text
            if (FPSLimitValue != null)
            {
                FPSLimitValue.Text = $"{(int)e.NewValue} FPS";
            }

            if (fpsLimit == null || isApplyingHelperUpdate) return;

            if (FPSLimitToggle.IsOn)
            {
                int limit = (int)e.NewValue;
                fpsLimitPendingValue = limit;

                // Initialize debounce timer if needed
                if (fpsLimitDebounceTimer == null)
                {
                    fpsLimitDebounceTimer = new DispatcherTimer();
                    fpsLimitDebounceTimer.Interval = TimeSpan.FromMilliseconds(FPS_LIMIT_DEBOUNCE_MS);
                    fpsLimitDebounceTimer.Tick += FPSLimitDebounceTimer_Tick;
                }

                // Restart the debounce timer
                fpsLimitDebounceTimer.Stop();
                fpsLimitDebounceTimer.Start();
            }
        }

        /// <summary>
        /// Debounce timer tick - apply the pending FPS limit value
        /// </summary>
        private void FPSLimitDebounceTimer_Tick(object sender, object e)
        {
            fpsLimitDebounceTimer?.Stop();

            if (fpsLimit != null && FPSLimitToggle.IsOn)
            {
                fpsLimit.SetValue(fpsLimitPendingValue);
                Logger.Info($"FPS Limit changed (debounced): {fpsLimitPendingValue}");

                // Save to profile if FPS Limit saving is enabled
                if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile)
                {
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
            }
        }

        /// <summary>
        /// Update FPS Limit controls based on RTSS installed status and current fpsLimit value
        /// </summary>
        private void UpdateFPSLimitControls()
        {
            UpdateFPSLimitControls(rtssInstalled?.Value == true);
        }

        /// <summary>
        /// Update FPS Limit controls based on RTSS installed status
        /// </summary>
        private void UpdateFPSLimitControls(bool rtssAvailable)
        {
            // Dispatch to UI thread since this may be called from property callback on non-UI thread
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    if (isUnloading) return;

                    // Guard against null controls during initialization or shutdown
                    if (FPSLimitToggle == null || FPSLimitSlider == null) return;

                    FPSLimitToggle.IsEnabled = rtssAvailable;

                    // Update slider maximum to current refresh rate
                    int maxRefresh = 60; // Default
                    if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
                    {
                        maxRefresh = refreshRates.Value.Max();
                    }
                    FPSLimitSlider.Maximum = maxRefresh;

                    // Set tick frequency based on max refresh rate (show ~5-8 ticks)
                    int tickFreq;
                    if (maxRefresh >= 144)
                        tickFreq = 24;
                    else if (maxRefresh >= 120)
                        tickFreq = 20;
                    else if (maxRefresh >= 90)
                        tickFreq = 15;
                    else
                        tickFreq = 10;
                    FPSLimitSlider.TickFrequency = tickFreq;

                    // Sync toggle/slider with fpsLimit value
                    if (fpsLimit != null)
                    {
                        isApplyingHelperUpdate = true;
                        try
                        {
                            int limit = fpsLimit.Value;
                            if (limit > 0)
                            {
                                FPSLimitToggle.IsOn = true;
                                // Clamp value to slider range
                                FPSLimitSlider.Value = Math.Min(limit, maxRefresh);
                            }
                            else
                            {
                                // When Intel mode is selected, fpsLimit (RTSS) is silenced to 0
                                // but the overall FPS limiter is still active via Intel tier.
                                // Turn the toggle ON for active Intel, OFF only when both are inactive.
                                bool intelActive = fpsCapMode?.Value == 1 && intelFpsTier?.Value > 0;
                                if (intelActive)
                                    FPSLimitToggle.IsOn = true;
                                else
                                    FPSLimitToggle.IsOn = false;
                            }
                        }
                        finally
                        {
                            isApplyingHelperUpdate = false;
                        }
                    }

                    // Sync Intel FPS tier ComboBox (0=Off, 1=Performance, 2=Balanced, 3=Efficiency)
                    if (intelFpsTier != null && IntelFpsTierComboBox != null)
                    {
                        isApplyingHelperUpdate = true;
                        try
                        {
                            int tier = Math.Max(0, Math.Min(3, intelFpsTier.Value));
                            IntelFpsTierComboBox.SelectedIndex = tier;
                        }
                        finally
                        {
                            isApplyingHelperUpdate = false;
                        }
                    }

                    // Sync the FPS limiter panels (RTSS/Intel selection + visibility)
                    UpdateFPSLimiterPanels();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in UpdateFPSLimitControls: {ex.Message}");
                }
            });
        }
        /// <summary>
        /// Intel FPS tier ComboBox selection changed in the Performance tab.
        /// Sends the selected tier to the helper; mutual exclusion with RTSS is handled there.
        /// </summary>
        private void IntelFpsTierComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (intelFpsTier == null || isApplyingHelperUpdate) return;
            int tier = IntelFpsTierComboBox.SelectedIndex;
            if (tier < 0) return;
            intelFpsTier.SetValue(tier);
            Logger.Info($"Intel FPS tier set from Performance tab: {tier}");
            // Refresh the green FPS number above the toggle to show the new Intel tier FPS
            UpdateFPSCapDisplayText();
        }

        private void ToggleLegionTouchpad()
        {
            if (legionGoDetected?.Value == true && legionTouchpadEnabled != null)
            {
                bool newValue = !legionTouchpadEnabled.Value;
                legionTouchpadEnabled.SetValue(newValue);
                Logger.Info($"Legion Touchpad toggled to {newValue}");
            }
        }

        private void CycleLegionLightMode()
        {
            if (legionGoDetected?.Value == true && legionLightMode != null)
            {
                int currentMode = legionLightMode.Value;
                int nextMode = (currentMode + 1) % 5; // 0-4: Off, Static, Breathing, Rainbow, Spiral
                legionLightMode.SetValue(nextMode);
                Logger.Info($"Legion Light Mode cycled from {currentMode} to {nextMode}");
            }
        }

        private void ToggleLegionDesktopControls()
        {
            if (legionGoDetected?.Value == true && LegionDesktopControlsToggle != null)
            {
                bool newValue = !LegionDesktopControlsToggle.IsOn;
                LegionDesktopControlsToggle.IsOn = newValue;
                // The Toggled event handler will apply the mappings
                Logger.Info($"Legion Desktop Controls toggled to {newValue}");
            }
        }

        private void ToggleLegionChargeLimit()
        {
            if (legionGoDetected?.Value == true && legionChargeLimit != null)
            {
                bool newValue = !legionChargeLimit.Value;
                legionChargeLimit.SetValue(newValue);
                // Also update the toggle in Legion tab if it exists
                if (LegionChargeLimitToggle != null)
                {
                    LegionChargeLimitToggle.IsOn = newValue;
                }
                Logger.Info($"Legion Charge Limit toggled to {(newValue ? "80%" : "Off")}");
            }
        }

        // Cycle order for the Quick-tab Controller tile.
        //   Legacy: every supported mode (0=Mouse, 1=Xbox Stick, 2=DS4 Motion, 3=DS4 Stick).
        //   VIIPER: every supported virtual-device tag from ViiperDeviceTypeComboBox.
        // Both cycles end at "Off" so the user can always get the tile back to disabled.
        private static readonly int[] ControllerEmulationLegacyCycle = new[] { 0, 1, 2, 3 };
        private static readonly string[] ControllerEmulationViiperCycle = new[]
        {
            "xbox360", "dualshock4", "dualsenseedge", "xboxelite2", "steam-generic", "switchpro"
        };

        private void ToggleControllerEmulation()
        {
            if (controllerEmulationEnabled == null)
            {
                return;
            }
            if (controllerEmulationAvailable?.Value != true)
            {
                Logger.Info("Controller Emulation tile click ignored — emulation not available on this device.");
                return;
            }

            bool isViiper = emulationBackend?.Value == true;
            bool currentlyEnabled = controllerEmulationEnabled.Value;

            if (isViiper)
            {
                CycleControllerEmulationViiper(currentlyEnabled);
            }
            else
            {
                CycleControllerEmulationLegacy(currentlyEnabled);
            }
        }

        private void CycleControllerEmulationLegacy(bool currentlyEnabled)
        {
            int[] cycle = ControllerEmulationLegacyCycle;

            if (!currentlyEnabled)
            {
                int firstMode = cycle[0];
                controllerEmulationMode?.SetValue(firstMode);
                controllerEmulationEnabled.SetValue(true);
                if (ControllerEmulationEnabledToggle != null)
                {
                    ControllerEmulationEnabledToggle.IsOn = true;
                }
                Logger.Info($"Controller Emulation (Legacy) cycled: Off -> mode {firstMode}");
                return;
            }

            int current = controllerEmulationMode?.Value ?? cycle[0];
            int currentIndex = Array.IndexOf(cycle, current);
            int nextIndex = currentIndex + 1;

            if (currentIndex < 0 || nextIndex >= cycle.Length)
            {
                // Current mode is outside the cycle (e.g. Mouse or PS4-Stick set via System tab),
                // or we're at the end — flip to Off.
                controllerEmulationEnabled.SetValue(false);
                if (ControllerEmulationEnabledToggle != null)
                {
                    ControllerEmulationEnabledToggle.IsOn = false;
                }
                Logger.Info("Controller Emulation (Legacy) cycled: -> Off");
            }
            else
            {
                int nextMode = cycle[nextIndex];
                controllerEmulationMode?.SetValue(nextMode);
                Logger.Info($"Controller Emulation (Legacy) cycled: mode {current} -> mode {nextMode}");
            }
        }

        private void CycleControllerEmulationViiper(bool currentlyEnabled)
        {
            string[] cycle = ControllerEmulationViiperCycle;

            if (!currentlyEnabled)
            {
                string firstDevice = cycle[0];
                viiperDeviceType?.SetValue(firstDevice);
                controllerEmulationEnabled.SetValue(true);
                if (ControllerEmulationEnabledToggle != null)
                {
                    ControllerEmulationEnabledToggle.IsOn = true;
                }
                Logger.Info($"Controller Emulation (VIIPER) cycled: Off -> {firstDevice}");
                return;
            }

            string current = viiperDeviceType?.Value ?? cycle[0];
            int currentIndex = Array.IndexOf(cycle, current);
            int nextIndex = currentIndex + 1;

            if (currentIndex < 0 || nextIndex >= cycle.Length)
            {
                controllerEmulationEnabled.SetValue(false);
                if (ControllerEmulationEnabledToggle != null)
                {
                    ControllerEmulationEnabledToggle.IsOn = false;
                }
                Logger.Info("Controller Emulation (VIIPER) cycled: -> Off");
            }
            else
            {
                string nextDevice = cycle[nextIndex];
                viiperDeviceType?.SetValue(nextDevice);
                Logger.Info($"Controller Emulation (VIIPER) cycled: {current} -> {nextDevice}");
            }
        }

        private void ToggleRemapControlsProfile()
        {
            if (legionGoDetected?.Value != true)
                return;

            if (LegionControllerProfileToggle == null)
                return;

            // Toggle the per-game controller profile
            LegionControllerProfileToggle.IsOn = !LegionControllerProfileToggle.IsOn;
            Logger.Info($"Toggled per-game controller profile to: {LegionControllerProfileToggle.IsOn}");

            // Update Quick Settings tiles
            UpdateQuickSettingsTileStates();
        }

        /// <summary>
        /// Show/hide customization panel
        /// </summary>
        private void QuickSettingsCustomize_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                // Enter edit mode
                qsEditMode = true;
                qsSelectedTileForMove = null;

                QuickSettingsCustomizePanel.Visibility = Visibility.Visible;
                QuickSettingsCustomizeButton.Visibility = Visibility.Collapsed;

                // Register keyboard handler for B/Escape to deselect
                QuickSettingsCustomizePanel.KeyDown -= QuickSettingsCustomizePanel_KeyDown;
                QuickSettingsCustomizePanel.KeyDown += QuickSettingsCustomizePanel_KeyDown;

                // Update column button visuals
                UpdateColumnButtonVisuals();

                // Rebuild UIs with edit mode enabled
                BuildSortableGrid();
                RebuildQuickSettingsTiles();  // Shows hidden tiles with overlay in edit mode
            }
        }


        /// <summary>
        /// Toggle visibility of a tile via the split-tile Show/Hide sub-button
        /// </summary>
        private void SortableTileVisibility_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button button) || !(button.Tag is string tagId))
                    return;

                // Strip "_vis" suffix added in CreateMiniTileForSort
                string tileId = tagId.EndsWith("_vis") ? tagId.Substring(0, tagId.Length - 4) : tagId;

                if (!qsTileMap.TryGetValue(tileId, out var tile))
                    return;

                tile.IsVisible = !tile.IsVisible;
                qsSelectedTileForMove = null;
                UpdateSelectedTileIndicator(null);
                BuildSortableGridPreserveScroll(tileId);
                Logger.Info($"Toggled visibility for {tile.Name} via Show/Hide button: {tile.IsVisible}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling tile visibility click: {ex.Message}");
            }
        }

        /// <summary>
        /// Open the controller-button binding Flyout for this tile (delegated to Grid.cs).
        /// </summary>
        private void SortableTileButtonBind_Click(object sender, RoutedEventArgs e)
            => OpenBtnBindFlyout(sender, e);

        /// <summary>
        /// Handle keyboard input in customize panel (B/Escape to deselect)
        /// </summary>
        private void QuickSettingsCustomizePanel_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape ||
                e.Key == Windows.System.VirtualKey.GamepadB)
            {
                if (qsSelectedTileForMove != null)
                {
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGrid();
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Close customization panel
        /// </summary>
        private void QuickSettingsCustomizeDone_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                // Exit edit mode
                qsEditMode = false;
                qsSelectedTileForMove = null;
                UpdateSelectedTileIndicator(null);

                QuickSettingsCustomizePanel.Visibility = Visibility.Collapsed;
                QuickSettingsCustomizeButton.Visibility = Visibility.Visible;

                // Save config and rebuild tiles without edit overlays
                SaveQuickSettingsConfig();
                RebuildQuickSettingsTiles();
                UpdateQuickSettingsTileStates();
            }
        }

        /// <summary>
        /// Set column count to 3
        /// </summary>
        private void ColumnCount3_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 3)
            {
                qsColumnCount = 3;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Set column count to 4
        /// </summary>
        private void ColumnCount4_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 4)
            {
                qsColumnCount = 4;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Set column count to 5
        /// </summary>
        private void ColumnCount5_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 5)
            {
                qsColumnCount = 5;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Update column button visuals to show current selection
        /// </summary>
        private void UpdateColumnButtonVisuals()
        {
            if (Column3Button == null || Column4Button == null || Column5Button == null) return;

            var selectedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180));
            var normalBrush = tileOffBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 28, 30));

            Column3Button.Background = qsColumnCount == 3 ? selectedBrush : normalBrush;
            Column4Button.Background = qsColumnCount == 4 ? selectedBrush : normalBrush;
            Column5Button.Background = qsColumnCount == 5 ? selectedBrush : normalBrush;
        }

        /// <summary>
        /// Add a custom shortcut or action tile
        /// </summary>
        private void AddCustomShortcut_Click(object sender, RoutedEventArgs e)
        {
            string name = CustomShortcutNameBox?.Text?.Trim();

            // For shortcut tiles the name is required; for action tiles it is auto-derived below
            if (string.IsNullOrEmpty(name) && !_isTileTypeAction)
            {
                Logger.Warn("Custom tile name is empty");
                return;
            }

            if (_isTileTypeAction)
            {
                // Action tile branch
                if (CustomActionTypeComboBox == null ||
                    !(CustomActionTypeComboBox.SelectedItem is ComboBoxItem selectedItem) ||
                    !(selectedItem.Tag is TileActionType actionType))
                {
                    Logger.Warn("No action selected");
                    return;
                }

                // Auto-derive tile name from the action's short name
                name = TileActionHelper.GetShortName(actionType);

                AddActionTile(name, actionType);

                // Clear name input; leave ComboBox at last selection for convenience
                if (CustomShortcutNameBox != null) CustomShortcutNameBox.Text = "";
            }
            else
            {
                // Keyboard shortcut tile branch (original)
                string shortcut = GetCustomShortcutKeysString();
                if (string.IsNullOrEmpty(shortcut))
                {
                    Logger.Warn("No keyboard shortcut defined");
                    return;
                }

                AddCustomShortcutTile(name, shortcut);

                // Clear inputs
                if (CustomShortcutNameBox != null) CustomShortcutNameBox.Text = "";
                _customShortcutKeys.Clear();
                UpdateCustomShortcutKeyTags();
            }

            UpdateQuickSettingsTileStates();
        }

        /// <summary>
        /// Handle tile visibility checkbox changes
        /// </summary>
        private void TileVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string tileId)
            {
                bool isVisible = checkBox.IsChecked ?? true;

                if (qsTileMap.TryGetValue(tileId, out var tile))
                {
                    tile.IsVisible = isVisible;
                }
            }
        }

        /// <summary>
        /// Toggle MSI Claw mode between Xbox controller emulation and Desktop mode.
        ///
        /// Controller mode (emulation ON):
        ///   ClawButtonMonitor runs → virtual Xbox 360 controller via ViGEm → games receive gamepad input.
        ///
        /// Mouse mode (emulation OFF):
        ///   ClawButtonMonitor stops → MSIClawDesktopModeForwarder starts.
        ///   Right stick → cursor, left stick → scroll, LB → right click, RB → left click.
        ///
        /// Mirrors ToggleLegionDesktopControls() for MSI Claw: both toggle a single emulation-on/off flag.
        /// OnMSIClawEmulationEnabledChanged in the helper handles ClawButtonMonitor + mouse forwarder lifecycle.
        /// </summary>
        private void ToggleMSIClawDesktopMode()
        {
            if (msiClawControllerMode == null) return;

            bool newState = !msiClawControllerMode.Value;
            msiClawControllerMode.SetValue(newState);

            Logger.Info($"MSI Claw mode toggled → controllerOn={newState} ({(newState ? "Controller" : "Mouse")})");
        }

        /// <summary>
        /// Toggle MSI Center M OEM software on/off.
        /// When activating MSI Center: disable controller emulation FIRST to avoid
        /// duplicate controllers (ClawTweaks virtual + MSI physical both active).
        /// When deactivating: stop processes/service/tasks via helper.
        /// </summary>
        private void ToggleMsiCenter()
        {
            if (msiCenterActive == null) return;
            bool newState = !msiCenterActive.Value;
            // The helper handles stopping ClawButtonMonitor + MouseForwarder in
            // OnMsiCenterStateChanged(true) — no pre-stop needed here.
            // Sending msiClawControllerMode=false first caused a race where the
            // forwarder started (background task) and was immediately killed by
            // the msiCenterActive=true message arriving milliseconds later.
            msiCenterActive.SetValue(newState);
            Logger.Info($"MSI Center M toggled → active={newState}");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Tile type selector (Keyboard ↔ Action) in the Add Custom Tile panel
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Switch add-tile mode to "Keyboard shortcut"
        /// </summary>
        private void TileTypeKeyboard_Click(object sender, RoutedEventArgs e)
        {
            _isTileTypeAction = false;

            // Show keyboard section + name field, hide action ComboBox
            if (TileKeyboardSection != null) TileKeyboardSection.Visibility = Visibility.Visible;
            if (CustomShortcutNameBox != null) CustomShortcutNameBox.Visibility = Visibility.Visible;
            if (CustomActionTypeComboBox != null) CustomActionTypeComboBox.Visibility = Visibility.Collapsed;

            // Highlight active button
            var activeColor = Windows.UI.Color.FromArgb(255, 26, 106, 154);   // #1A6A9A — blue
            var inactiveColor = Windows.UI.Color.FromArgb(255, 26, 28, 30);   // #1A1C1E — dark
            var activeBorder = Windows.UI.Color.FromArgb(255, 34, 136, 204);  // #2288CC
            var inactiveBorder = Windows.UI.Color.FromArgb(80, 85, 92, 0);    // #50555C

            if (TileTypeKeyboardButton != null)
            {
                TileTypeKeyboardButton.Background = new SolidColorBrush(activeColor);
                TileTypeKeyboardButton.BorderBrush = new SolidColorBrush(activeBorder);
            }
            if (TileTypeActionButton != null)
            {
                TileTypeActionButton.Background = new SolidColorBrush(inactiveColor);
                TileTypeActionButton.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 85, 92, 0));
            }
        }

        /// <summary>
        /// Switch add-tile mode to "Predefined action"
        /// </summary>
        private void TileTypeAction_Click(object sender, RoutedEventArgs e)
        {
            _isTileTypeAction = true;

            // Hide keyboard section + name field, show action ComboBox
            if (TileKeyboardSection != null) TileKeyboardSection.Visibility = Visibility.Collapsed;
            if (CustomShortcutNameBox != null) CustomShortcutNameBox.Visibility = Visibility.Collapsed;
            if (CustomActionTypeComboBox != null) CustomActionTypeComboBox.Visibility = Visibility.Visible;

            // Highlight active button
            var activeColor = Windows.UI.Color.FromArgb(255, 26, 106, 154);
            var inactiveColor = Windows.UI.Color.FromArgb(255, 26, 28, 30);
            var activeBorder = Windows.UI.Color.FromArgb(255, 34, 136, 204);

            if (TileTypeActionButton != null)
            {
                TileTypeActionButton.Background = new SolidColorBrush(activeColor);
                TileTypeActionButton.BorderBrush = new SolidColorBrush(activeBorder);
            }
            if (TileTypeKeyboardButton != null)
            {
                TileTypeKeyboardButton.Background = new SolidColorBrush(inactiveColor);
                TileTypeKeyboardButton.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 85, 92, 0));
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Predefined-action execution
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Execute an action triggered by the physical left MSI button (Game Bar is NOT open).
        /// Uses SendKeyboardShortcutViaHelper directly — no Win+G prefix needed.
        /// </summary>
        private async Task ExecutePhysicalButtonActionAsync(TileActionType actionType, string actionName)
        {
            try
            {
                switch (actionType)
                {
                    case TileActionType.AltTab:
                    case TileActionType.AltTabBack:
                        await SendKeyboardShortcutViaHelper("Alt+Tab");
                        break;
                    case TileActionType.GoToDesktop:
                        await SendKeyboardShortcutViaHelper("Win+D");
                        break;
                    case TileActionType.BrightnessUp:
                    case TileActionType.BrightnessDown:
                        await AdjustBrightnessViaHelperAsync(actionType == TileActionType.BrightnessUp ? 5 : -5);
                        break;
                    case TileActionType.VolumeUp:
                    case TileActionType.VolumeDown:
                        await AdjustVolumeViaHelperAsync(actionType == TileActionType.VolumeUp ? 5 : -5);
                        break;
                    case TileActionType.CycleOverlayMode:
                        CyclePerformanceOverlay();
                        break;
                    case TileActionType.TDPIncrBy1W:
                        AdjustTDPByWatts(+1);
                        break;
                    case TileActionType.TDPDecrBy1W:
                        AdjustTDPByWatts(-1);
                        break;
                    case TileActionType.CycleLimiterMode:
                        CycleFPSForCurrentMode();
                        break;
                }
                await SendActionNotificationAsync(actionName);
                Logger.Info($"ExecutePhysicalButtonActionAsync: executed {actionType} ({actionName})");
            }
            catch (Exception ex)
            {
                Logger.Error($"ExecutePhysicalButtonActionAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute a predefined tile action and show an RTSS OSD notification.
        /// </summary>
        private async Task ExecuteTileActionAsync(TileActionType actionType, string actionName)
        {
            try
            {
                switch (actionType)
                {
                    // ── OS key actions ─────────────────────────────────────
                    // These must close Game Bar first (Win+G) before sending the
                    // shortcut — otherwise the key fires behind the open overlay
                    // and the effect is invisible to the user.
                    case TileActionType.AltTab:
                        await SendCustomShortcutAsync("Alt+Tab", actionName);
                        break;

                    case TileActionType.AltTabBack:
                        await SendCustomShortcutAsync("Alt+Tab", actionName);
                        break;

                    case TileActionType.GoToDesktop:
                        await SendCustomShortcutAsync("Win+D", actionName);
                        break;

                    // ── Brightness / Volume (via helper) ───────────────────
                    case TileActionType.BrightnessUp:
                    case TileActionType.BrightnessDown:
                        await AdjustBrightnessViaHelperAsync(actionType == TileActionType.BrightnessUp ? 5 : -5);
                        break;

                    case TileActionType.VolumeUp:
                    case TileActionType.VolumeDown:
                        await AdjustVolumeViaHelperAsync(actionType == TileActionType.VolumeUp ? 5 : -5);
                        break;

                    // ── App actions ─────────────────────────────────────────
                    case TileActionType.CycleOverlayMode:
                        CyclePerformanceOverlay();
                        break;

                    case TileActionType.CycleTDPMode:
                        CycleTDPMode();
                        break;

                    case TileActionType.TDPStepUp:
                        StepTDPMode(+1);
                        break;

                    case TileActionType.TDPStepDown:
                        StepTDPMode(-1);
                        break;

                    case TileActionType.TDPIncrBy1W:
                        AdjustTDPByWatts(+1);
                        break;

                    case TileActionType.TDPDecrBy1W:
                        AdjustTDPByWatts(-1);
                        break;

                    case TileActionType.CycleLimiterMode:
                        CycleFPSForCurrentMode();
                        break;
                }

                // Show RTSS OSD notification — tile name on line 1, action description on line 2
                string actionDesc = TileActionHelper.GetDisplayName(actionType);
                string notifText = (!string.IsNullOrEmpty(actionDesc) && actionDesc != "Unknown" &&
                                    !actionName.Equals(actionDesc, StringComparison.OrdinalIgnoreCase))
                    ? $"{actionName}\n{actionDesc}"
                    : actionName;
                await SendActionNotificationAsync(notifText);
                Logger.Info($"ExecuteTileActionAsync: executed {actionType} ({actionName})");
            }
            catch (Exception ex)
            {
                Logger.Error($"ExecuteTileActionAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Ask the helper to adjust display brightness by <paramref name="delta"/> percent (±5 typical).
        /// </summary>
        private async Task AdjustBrightnessViaHelperAsync(int delta)
        {
            try
            {
                if (!App.IsConnected) return;

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "AdjustBrightness", delta }
                };
                await App.SendMessageAsync(request);
            }
            catch (Exception ex)
            {
                Logger.Warn($"AdjustBrightnessViaHelperAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a transient line in the RTSS OSD overlay for a few seconds.
        /// Helper side: RTSSManager.ShowNotification() handles the timeout.
        /// </summary>
        private async Task SendActionNotificationAsync(string actionName)
        {
            try
            {
                if (!App.IsConnected) return;

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "ShowOSDNotification", actionName }
                };
                await App.SendMessageAsync(request);
            }
            catch (Exception ex)
            {
                Logger.Warn($"SendActionNotificationAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Step the TDP mode ComboBox up or down by one position (wraps around).
        /// +1 = next mode, -1 = previous mode.
        /// </summary>
        /// <summary>
        /// Adjust current TDP by ±1 W, staying in Custom/Slider mode.
        /// PL1 clamped to [4 W, 36 W] so PL2 = PL1+1 ≤ 37 W (MSI Claw hardware limit).
        /// </summary>
        private void AdjustTDPByWatts(int delta)
        {
            if (TDPSlider == null || tdp == null) return;

            // Switch to Custom/Slider mode so the watt value is applied directly
            int sliderIndex = useCustomTDPPresets && tdpPresets != null
                ? tdpPresets.Count          // Slider item is always last in custom preset list
                : TDPModeComboBox?.Items.Count - 1 ?? 5; // last item = Slider in default list

            if (TDPModeComboBox != null && !IsCustomTdpModeSelected())
                SetTDPModeByIndex(sliderIndex);

            const int TDP_MIN   = 4;
            const int TDP_MAX   = 36;  // PL2 = PL1+1 = 37 W ≤ MSI Claw max PL2
            int current = (int)Math.Round(TDPSlider.Value);
            int next    = Math.Max(TDP_MIN, Math.Min(TDP_MAX, current + delta));

            TDPSlider.Value = next;
            tdp.SetValue(next);
            if (SaveTDP && !isLoadingProfile)
                SaveCurrentSettingsToProfile(currentProfileName);

            Logger.Info($"AdjustTDPByWatts: {current}W → {next}W (delta={delta})");
        }

        private async Task AdjustVolumeViaHelperAsync(int delta)
        {
            try
            {
                if (!App.IsConnected) return;
                var request = new Windows.Foundation.Collections.ValueSet { { "AdjustVolume", delta } };
                await App.SendMessageAsync(request);
            }
            catch (Exception ex) { Logger.Warn($"AdjustVolumeViaHelperAsync: {ex.Message}"); }
        }

        private void StepTDPMode(int direction)
        {
            if (TDPModeComboBox == null) return;

            int count = TDPModeComboBox.Items.Count;
            if (count == 0) return;

            int current = TDPModeComboBox.SelectedIndex;
            int next = ((current + direction) % count + count) % count;  // wrap with positive modulo
            SetTDPModeByIndex(next);
        }

    }
}
