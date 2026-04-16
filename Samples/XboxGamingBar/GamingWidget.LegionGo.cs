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
        /// Shows or hides the Legion tab based on device detection
        /// </summary>
        private void SetLegionTabVisibility(bool visible)
        {
            if (LegionNavItem != null)
            {
                LegionNavItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Legion tab visibility set to: {visible}");
            }

            // TDP Mode card is always visible for all devices
            // Legion devices: uses hardware presets (Quiet/Balanced/Performance/Custom)
            // Generic devices: uses TDP value presets (8W/15W/25W/Custom)
            if (TDPModeCard != null)
            {
                TDPModeCard.Visibility = Visibility.Visible;
                Logger.Info($"TDP Mode card visibility set to: Visible (Legion={visible})");

                // Update XY focus bindings - TDP Mode card is always present now
                UpdatePerformanceTabXYFocus(true);

                // Sync TDP Mode with Legion Performance Mode if Legion device
                // Skip during initial sync - ApplyProfileTDPToHelper will set the correct value
                if (visible && LegionPerformanceModeComboBox != null && TDPModeComboBox != null && !isInitialSync)
                {
                    TDPModeComboBox.SelectedIndex = LegionPerformanceModeComboBox.SelectedIndex;
                }
            }

            // Show/hide Manufacturer WMI option in TDP Method dropdown based on Legion detection
            if (TdpMethodWmiItem != null)
            {
                TdpMethodWmiItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"TDP Method WMI option visibility set to: {visible}");

                // If Legion detected and WMI option now visible, select it if not already selected
                if (visible && TdpMethodComboBox != null && TdpMethodComboBox.SelectedIndex < 0)
                {
                    TdpMethodComboBox.SelectedIndex = 0; // ManufacturerWMI
                }
                // If Legion not detected and WMI was selected, switch to PawnIO
                else if (!visible && TdpMethodComboBox != null)
                {
                    var selectedItem = TdpMethodComboBox.SelectedItem as ComboBoxItem;
                    if (selectedItem?.Tag is string tag && tag == "ManufacturerWMI")
                    {
                        // Find and select PawnIO
                        for (int i = 0; i < TdpMethodComboBox.Items.Count; i++)
                        {
                            if (TdpMethodComboBox.Items[i] is ComboBoxItem item && item.Tag is string t && t == "PawnIO")
                            {
                                TdpMethodComboBox.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
            }

            // Update the TDP Method description based on Legion detection
            if (TdpMethodDescription != null)
            {
                if (visible)
                {
                    TdpMethodDescription.Text = "Select TDP control method. Manufacturer WMI (Legion) and PawnIO are anti-cheat safe.";
                }
                else
                {
                    TdpMethodDescription.Text = "Select TDP control method. PawnIO is anti-cheat safe. WinRing0 may trigger anti-cheat.";
                }
            }

            // Refresh Quick Settings tiles to show/hide Legion-specific tiles
            RefreshQuickSettingsForLegion();
        }

        /// <summary>
        /// Updates the device name display in the Legion tab header.
        /// </summary>
        private void SetLegionDeviceName(string name)
        {
            if (LegionDeviceNameText != null && !string.IsNullOrEmpty(name))
            {
                LegionDeviceNameText.Text = name;
                Logger.Info($"Legion device name set to: {name}");
            }
        }

        /// <summary>
        /// Shows or hides the Controller Remapping section based on device support.
        /// Legion Go S has a different HID structure, so controller remapping doesn't work.
        /// </summary>
        private void SetControllerRemappingSectionVisibility(bool visible)
        {
            if (ControllerRemappingSection != null)
            {
                ControllerRemappingSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Controller Remapping section visibility set to: {visible}");

                if (visible)
                {
                    RefreshLegionEnhancedRemapUi();
                }
            }
        }

        /// <summary>
        /// Shows or hides the Lighting section based on device support.
        /// Legion Go S has a different HID structure, so RGB lighting control doesn't work.
        /// </summary>
        private void SetLightingSectionVisibility(bool visible)
        {
            if (LightingSection != null)
            {
                LightingSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Lighting section visibility set to: {visible}");
            }
        }

        /// <summary>
        /// Shows or hides the Gyro Settings card based on device support.
        /// Legion Go S has a different HID structure, so gyro configuration doesn't work.
        /// </summary>
        private void SetGyroSectionVisibility(bool visible)
        {
            if (GyroSection != null)
            {
                GyroSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Gyro section visibility set to: {visible}");
            }
        }

        private void SetScrollWheelSectionVisibility(bool visible)
        {
            if (ScrollWheelSection != null)
            {
                ScrollWheelSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Scroll wheel section visibility set to: {visible}");
            }
        }

        private void SetControllerBatterySectionVisibility(bool visible)
        {
            if (ControllerBatterySection != null)
            {
                ControllerBatterySection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Controller battery section visibility set to: {visible}");
            }
        }

        private void SetTouchpadVibrationSectionVisibility(bool visible)
        {
            if (TouchpadVibrationSection != null)
            {
                TouchpadVibrationSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Touchpad & Vibration section visibility set to: {visible}");
            }
        }
        /// <summary>
        /// Updates XY focus bindings in Performance tab based on Legion detection
        /// </summary>
        private void UpdatePerformanceTabXYFocus(bool isLegion)
        {
            if (PerformanceOverlayComboBox != null && TDPModeComboBox != null && TDPSlider != null)
            {
                if (isLegion)
                {
                    // Legion: PerformanceOverlay -> TDPMode -> TDPSlider
                    PerformanceOverlayComboBox.XYFocusDown = TDPModeComboBox;
                    TDPSlider.XYFocusUp = TDPModeComboBox;
                }
                else
                {
                    // Non-Legion: PerformanceOverlay -> TDPSlider
                    PerformanceOverlayComboBox.XYFocusDown = TDPSlider;
                    TDPSlider.XYFocusUp = PerformanceOverlayComboBox;
                }
            }
        }

        /// <summary>
        /// Shows or hides the Default Game Profile card based on profile availability.
        /// </summary>
        private void SetDefaultProfileCardVisibility(bool isVisible)
        {
            if (DefaultGameProfileCard != null)
            {
                DefaultGameProfileCard.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Default Game Profile card visibility set to: {isVisible}");

                // Update XY navigation when DGP visibility changes
                UpdatePerformanceTabXYNavigation();
            }
        }

        /// <summary>
        /// Updates XY focus navigation for the Performance tab based on current state.
        /// Flow: Nav -> DGP Toggle (if visible) -> PerGameProfile Toggle (if game detected) -> Performance Overlay -> ...
        /// When DGP is ON: Nav -> DGP Toggle -> TDP Extras (skip disabled TDP/FPS controls)
        /// </summary>
        private void UpdatePerformanceTabXYNavigation()
        {
            // Early exit if UI elements aren't ready
            if (PerformanceNavItem == null || PerformanceOverlayComboBox == null) return;

            bool dgpVisible = DefaultGameProfileCard?.Visibility == Visibility.Visible;
            bool dgpEnabled = defaultGameProfileEnabled?.Value == true;
            bool gameDetected = runningGame?.Value.IsValid() == true;

            Logger.Debug($"UpdatePerformanceTabXYNavigation: dgpVisible={dgpVisible}, dgpEnabled={dgpEnabled}, gameDetected={gameDetected}");

            // Determine the chain of focusable elements
            // Start from PerformanceNavItem going down

            if (dgpVisible && DefaultProfileToggle != null)
            {
                // DGP is visible: Nav -> DefaultProfileToggle
                PerformanceNavItem.XYFocusDown = DefaultProfileToggle;
                DefaultProfileToggle.XYFocusUp = PerformanceNavItem;

                if (dgpEnabled && TDPExtrasExpandToggle != null)
                {
                    // DGP is ON: skip disabled TDP/FPS controls, go to TDP Extras dropdown
                    // (OS Power Mode and CPU Extras are still available)
                    DefaultProfileToggle.XYFocusDown = TDPExtrasExpandToggle;
                    TDPExtrasExpandToggle.XYFocusUp = DefaultProfileToggle;

                    // Set navigation from TDP Extras down to OS Power Mode, and OS Power Mode up to TDP Extras
                    if (OSPowerModeComboBox != null)
                    {
                        TDPExtrasExpandToggle.XYFocusDown = OSPowerModeComboBox;
                        OSPowerModeComboBox.XYFocusUp = TDPExtrasExpandToggle;
                    }
                }
                else if (gameDetected && PerGameProfileToggle != null)
                {
                    // DGP visible but OFF, game detected: DGP Toggle -> PerGameProfile Toggle -> Overlay
                    DefaultProfileToggle.XYFocusDown = PerGameProfileToggle;
                    PerGameProfileToggle.XYFocusUp = DefaultProfileToggle;
                    PerGameProfileToggle.XYFocusDown = PerformanceOverlayComboBox;
                    PerformanceOverlayComboBox.XYFocusUp = PerGameProfileToggle;
                }
                else
                {
                    // DGP visible but OFF, no game: DGP Toggle -> Overlay (skip disabled PerGameProfile)
                    DefaultProfileToggle.XYFocusDown = PerformanceOverlayComboBox;
                    PerformanceOverlayComboBox.XYFocusUp = DefaultProfileToggle;
                }
            }
            else
            {
                // DGP not visible
                if (gameDetected && PerGameProfileToggle != null)
                {
                    // No DGP, game detected: Nav -> PerGameProfile Toggle -> Overlay
                    PerformanceNavItem.XYFocusDown = PerGameProfileToggle;
                    PerGameProfileToggle.XYFocusUp = PerformanceNavItem;
                    PerGameProfileToggle.XYFocusDown = PerformanceOverlayComboBox;
                    PerformanceOverlayComboBox.XYFocusUp = PerGameProfileToggle;
                }
                else
                {
                    // No DGP, no game: Nav -> Overlay (skip disabled PerGameProfile)
                    PerformanceNavItem.XYFocusDown = PerformanceOverlayComboBox;
                    PerformanceOverlayComboBox.XYFocusUp = PerformanceNavItem;
                }
            }
        }

        /// <summary>
        /// Updates the Default Game Profile card display with profile settings.
        /// </summary>
        private void UpdateDefaultProfileDisplay(Shared.Data.DefaultGameProfile? profile)
        {
            if (profile.HasValue)
            {
                var p = profile.Value;

                // Store current profile first (needed by UpdateDefaultProfileGameIcon)
                currentDefaultGameProfile = p;

                // Update game name
                if (DefaultProfileGameName != null)
                {
                    DefaultProfileGameName.Text = p.GameName ?? "";
                }

                // Update game icon from Steam CDN if available
                UpdateDefaultProfileGameIcon();

                // Update settings text
                if (DefaultProfileSettingsText != null)
                {
                    var settings = new System.Collections.Generic.List<string>();

                    settings.Add($"{p.TDP}W");

                    if (p.FrameCap.HasValue && p.FrameCap.Value > 0)
                    {
                        settings.Add($"{p.FrameCap.Value}fps");
                    }

                    if (!string.IsNullOrEmpty(p.ResolutionCap))
                    {
                        settings.Add(p.ResolutionCap);
                    }

                    DefaultProfileSettingsText.Text = string.Join(" - ", settings);
                    Logger.Info($"Default Game Profile display updated: {DefaultProfileSettingsText.Text}");
                }

                // Update "Optimizing for Z2/Z1 Extreme" text based on hardware model
                if (DefaultProfileOptimizingText != null && DefaultProfileSeparator != null)
                {
                    string optimizingText = "Optimizing for your device";
                    if (!string.IsNullOrEmpty(p.HardwareModel))
                    {
                        if (p.HardwareModel == "HORSEM4N")
                        {
                            optimizingText = "Optimizing for Z2 Extreme";
                        }
                        else if (p.HardwareModel == "OMNI")
                        {
                            optimizingText = "Optimizing for Z1 Extreme";
                        }
                    }
                    DefaultProfileOptimizingText.Text = optimizingText;
                    DefaultProfileOptimizingText.Visibility = Visibility.Visible;
                    DefaultProfileSeparator.Visibility = Visibility.Visible;
                }
            }
            else
            {
                // Hide elements when no profile
                if (DefaultProfileOptimizingText != null)
                {
                    DefaultProfileOptimizingText.Visibility = Visibility.Collapsed;
                }
                if (DefaultProfileSeparator != null)
                {
                    DefaultProfileSeparator.Visibility = Visibility.Collapsed;
                }
                if (DefaultProfileGameName != null)
                {
                    DefaultProfileGameName.Text = "";
                }
                currentDefaultGameProfile = null;
            }
        }

        /// <summary>
        /// Updates the game icon in the Default Game Profile card.
        /// First tries helper-cached icons, then Steam's local cache.
        /// </summary>
        private async void UpdateDefaultProfileGameIcon()
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.HasThreadAccess)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => UpdateDefaultProfileGameIcon());
                return;
            }

            if (DefaultProfileGameIcon == null)
            {
                Logger.Info("UpdateDefaultProfileGameIcon: DefaultProfileGameIcon element is null");
                return;
            }

            Logger.Info($"UpdateDefaultProfileGameIcon: Starting, currentGameExePath={currentGameExePath ?? "null"}");

            string iconPath = null;

            // First, try to use the helper-cached icon for the current running game
            // (Default profiles are shown when a matching game is detected)
            if (!string.IsNullOrEmpty(currentGameExePath))
            {
                iconPath = GetCachedIconPath(currentGameExePath);
                if (!string.IsNullOrEmpty(iconPath))
                {
                    Logger.Info($"UpdateDefaultProfileGameIcon: Using helper-cached icon: {iconPath}");
                }
                else
                {
                    Logger.Info($"UpdateDefaultProfileGameIcon: No cached icon found for {currentGameExePath}");
                }
            }

            // Fall back to Steam icon if we have a Steam App ID
            if (string.IsNullOrEmpty(iconPath) && currentDefaultGameProfile.HasValue)
            {
                iconPath = currentDefaultGameProfile.Value.GetSteamIconPath();
                if (!string.IsNullOrEmpty(iconPath))
                {
                    Logger.Info($"UpdateDefaultProfileGameIcon: Using Steam icon: {iconPath}");
                }
            }

            // Try to load the icon
            if (!string.IsNullOrEmpty(iconPath))
            {
                try
                {
                    // Load from local file using StorageFile for UWP compatibility
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(iconPath);
                    using (var stream = await file.OpenReadAsync())
                    {
                        var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                        await bitmap.SetSourceAsync(stream);
                        DefaultProfileGameIcon.Source = bitmap;
                        DefaultProfileGameIcon.Visibility = Visibility.Visible;
                        Logger.Info($"UpdateDefaultProfileGameIcon: Icon loaded successfully");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"UpdateDefaultProfileGameIcon: Failed to load icon from {iconPath}: {ex.Message}");
                }
            }
            else
            {
                Logger.Info("UpdateDefaultProfileGameIcon: No icon path available");
            }

            // Hide the icon if no icon available
            DefaultProfileGameIcon.Visibility = Visibility.Collapsed;
        }

        // Cached default game profile for UI state management
        private Shared.Data.DefaultGameProfile? currentDefaultGameProfile;

        /// <summary>
        /// Called when the Default Game Profile enabled state changes.
        /// Greys out TDP controls and syncs FPS limit when enabled.
        /// </summary>
        private void OnDefaultProfileEnabledChanged(bool enabled)
        {
            Logger.Info($"Default Game Profile enabled changed to: {enabled}");

            // IMPORTANT: When DISABLING, load the appropriate profile for current power state!
            // Don't restore saved values - they may be from a different power state (e.g., DC when now on AC)
            // Set flag to suppress profile saves during restoration (toggle handlers would otherwise save wrong values)
            if (!enabled)
            {
                isRestoringFromDefaultProfile = true;
                try
                {
                    // Clear saved state - we'll load from profile instead of restoring
                    // FPS limit and TDP can differ between AC/DC profiles, so restoring pre-DGP state is wrong
                    originalFpsLimitToggleState = null;
                    originalFpsLimitSliderValue = null;
                    originalTdpSliderValue = null;

                    // Load the appropriate profile for current power state
                    // This ensures AC profile is loaded when on AC, DC profile when on DC
                    // Profile loading handles TDP, FPS limit, and all other settings
                    string targetProfile = GetTargetProfileName();
                    Logger.Info($"DGP disabled - loading profile for current power state: {targetProfile}");
                    LoadProfileSettings(targetProfile, isExplicitSwitch: false);
                }
                finally
                {
                    isRestoringFromDefaultProfile = false;
                }
            }

            // Update TDP controls enabled state (now with correct values if disabling)
            UpdateTDPControlsForDefaultProfile(enabled);

            // Update per-game profile toggle state
            UpdatePerGameProfileForDefaultProfile(enabled);

            if (enabled && currentDefaultGameProfile.HasValue)
            {
                var profile = currentDefaultGameProfile.Value;

                // Save original FPS limit state before changing
                if (FPSLimitToggle != null && !originalFpsLimitToggleState.HasValue)
                {
                    originalFpsLimitToggleState = FPSLimitToggle.IsOn;
                    originalFpsLimitSliderValue = FPSLimitSlider?.Value ?? 60;
                    Logger.Info($"Saved original FPS limit state: toggle={originalFpsLimitToggleState}, value={originalFpsLimitSliderValue}");
                }

                // Save original TDP slider value before changing
                if (TDPSlider != null && !originalTdpSliderValue.HasValue)
                {
                    originalTdpSliderValue = TDPSlider.Value;
                    Logger.Info($"Saved original TDP slider value: {originalTdpSliderValue}W");
                }

                // Sync FPS limit toggle and slider to match profile
                if (profile.FrameCap.HasValue && profile.FrameCap.Value > 0)
                {
                    if (FPSLimitToggle != null)
                    {
                        FPSLimitToggle.IsOn = true;
                    }
                    if (FPSLimitSlider != null)
                    {
                        FPSLimitSlider.Value = profile.FrameCap.Value;
                    }
                    Logger.Info($"FPS limit synced to default profile: {profile.FrameCap.Value}fps");
                }

                // Sync TDP slider to match profile
                if (profile.TDP > 0 && TDPSlider != null)
                {
                    TDPSlider.Value = profile.TDP;
                    Logger.Info($"TDP slider synced to default profile: {profile.TDP}W");
                }
            }

            // Update Quick tab tile styling
            UpdateQuickSettingsTileStates();

            // Update XY navigation for controller support
            UpdatePerformanceTabXYNavigation();
        }

        // Store original state for restoration when default profile is disabled
        private bool? originalFpsLimitToggleState;
        private double? originalFpsLimitSliderValue;
        private double? originalTdpSliderValue;
        private bool isRestoringFromDefaultProfile; // Flag to suppress profile saves during DGP restoration

        /// <summary>
        /// Updates per-game profile toggle state based on Default Game Profile.
        /// </summary>
        private void UpdatePerGameProfileForDefaultProfile(bool defaultProfileEnabled)
        {
            if (defaultProfileEnabled)
            {
                // Hide the Active Profile card when default game profile is enabled
                if (ActiveProfileCard != null)
                {
                    ActiveProfileCard.Visibility = Visibility.Collapsed;
                }

                Logger.Debug("Active Profile card hidden - Default Game Profile is active");
            }
            else
            {
                // Show the Active Profile card when default game profile is disabled
                if (ActiveProfileCard != null)
                {
                    ActiveProfileCard.Visibility = Visibility.Visible;
                }

                // Re-enable the per-game profile toggle
                if (PerGameProfileToggle != null)
                {
                    PerGameProfileToggle.IsEnabled = runningGame?.Value.IsValid() == true;
                }

                Logger.Debug("Active Profile card shown - Default Game Profile is inactive");
            }
        }

        /// <summary>
        /// Updates TDP control enabled states based on Default Game Profile.
        /// </summary>
        private void UpdateTDPControlsForDefaultProfile(bool defaultProfileEnabled)
        {
            if (defaultProfileEnabled)
            {
                // Disable TDP controls when default profile is active
                if (TDPModeComboBox != null)
                {
                    TDPModeComboBox.IsEnabled = false;
                }
                if (TDPSlider != null)
                {
                    TDPSlider.IsEnabled = false;
                }
                if (TDPBoostToggle != null)
                {
                    TDPBoostToggle.IsEnabled = false;
                }
                if (AutoTDPToggle != null)
                {
                    AutoTDPToggle.IsEnabled = false;
                }
                if (StickyTDPToggle != null)
                {
                    StickyTDPToggle.IsEnabled = false;
                }

                // Also disable FPS limit controls (controlled by Default Game Profile)
                if (FPSLimitToggle != null)
                {
                    FPSLimitToggle.IsEnabled = false;
                }
                if (FPSLimitSlider != null)
                {
                    FPSLimitSlider.IsEnabled = false;
                }

                Logger.Debug("TDP and FPS controls disabled - Default Game Profile is active");
            }
            else
            {
                // Re-enable TDP controls based on current mode
                if (TDPModeComboBox != null)
                {
                    TDPModeComboBox.IsEnabled = true;
                }

                // Re-enable FPS limit controls
                if (FPSLimitToggle != null)
                {
                    FPSLimitToggle.IsEnabled = true;
                }
                if (FPSLimitSlider != null)
                {
                    FPSLimitSlider.IsEnabled = true;
                }

                // Re-evaluate other controls based on current TDP mode
                UpdateTDPSliderEnabledState();

                Logger.Debug("TDP and FPS controls re-enabled - Default Game Profile is inactive");
            }
        }

        /// <summary>
        /// Updates WinRing0 option visibility in TDP Method dropdown based on file availability.
        /// </summary>
        private void UpdateWinRing0Visibility(bool available)
        {
            if (TdpMethodWinRing0Item != null)
            {
                TdpMethodWinRing0Item.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"WinRing0 TDP option visibility set to: {available}");

                // If WinRing0 was selected but is no longer available, switch to WMI or PawnIO
                if (!available && TdpMethodComboBox != null)
                {
                    var selectedItem = TdpMethodComboBox.SelectedItem as ComboBoxItem;
                    if (selectedItem?.Tag is string tag && tag == "WinRing0")
                    {
                        // Try to select ManufacturerWMI first, then PawnIO
                        if (TdpMethodWmiItem?.Visibility == Visibility.Visible)
                        {
                            TdpMethodComboBox.SelectedItem = TdpMethodWmiItem;
                        }
                        else if (TdpMethodPawnIOItem?.Visibility == Visibility.Visible)
                        {
                            TdpMethodComboBox.SelectedItem = TdpMethodPawnIOItem;
                        }
                    }
                }
            }

            // Ensure a valid option is selected after visibility changes
            EnsureValidTdpMethodSelected();
        }

        /// <summary>
        /// Ensures a valid (visible and enabled) TDP method is selected in the dropdown.
        /// IMPORTANT: Never auto-select WinRing0 - user must explicitly choose it.
        /// </summary>
        private void EnsureValidTdpMethodSelected()
        {
            if (TdpMethodComboBox == null) return;

            var selectedItem = TdpMethodComboBox.SelectedItem as ComboBoxItem;
            var selectedIndex = TdpMethodComboBox.SelectedIndex;

            // If current selection is valid (visible and enabled), do nothing
            if (selectedItem != null && selectedItem.Visibility == Visibility.Visible && selectedItem.IsEnabled)
            {
                return;
            }

            // If ManufacturerWMI is selected but collapsed, wait for Legion detection
            // Don't auto-select PawnIO - Legion detection will make WMI visible if it's a Legion device
            if (selectedItem != null && selectedItem == TdpMethodWmiItem && selectedItem.Visibility == Visibility.Collapsed)
            {
                Logger.Debug("EnsureValidTdpMethodSelected: ManufacturerWMI selected but collapsed, waiting for Legion detection");
                return;
            }

            // If selectedIndex is 0 (ManufacturerWMI position) but selectedItem isn't matching,
            // it means WMI was intended but may be collapsed - wait for Legion detection
            if (selectedIndex == 0 && TdpMethodWmiItem?.Visibility == Visibility.Collapsed)
            {
                Logger.Debug("EnsureValidTdpMethodSelected: SelectedIndex=0 (WMI) but WMI collapsed, waiting for Legion detection");
                return;
            }

            // If nothing is selected yet and WMI is collapsed, wait for Legion detection
            // This handles the case where the ComboBox rejected the initial Collapsed selection
            if (selectedItem == null && TdpMethodWmiItem?.Visibility == Visibility.Collapsed)
            {
                Logger.Debug("EnsureValidTdpMethodSelected: No selection and WMI collapsed, waiting for Legion detection");
                return;
            }

            // Find the first visible and enabled option and select it
            // Priority: ManufacturerWMI > PawnIO (if installed)
            // NEVER auto-select WinRing0 - it's a legacy option that may trigger anti-cheat
            if (TdpMethodWmiItem?.Visibility == Visibility.Visible && TdpMethodWmiItem.IsEnabled)
            {
                TdpMethodComboBox.SelectedItem = TdpMethodWmiItem;
                Logger.Info("TDP Method auto-selected: ManufacturerWMI");
            }
            else if (TdpMethodPawnIOItem?.Visibility == Visibility.Visible && TdpMethodPawnIOItem.IsEnabled)
            {
                TdpMethodComboBox.SelectedItem = TdpMethodPawnIOItem;
                Logger.Info("TDP Method auto-selected: PawnIO");
            }
            else
            {
                // Don't auto-select WinRing0 - user must explicitly choose it
                Logger.Warn("No safe TDP method available - user must select WinRing0 manually if desired");
            }
        }

        /// <summary>
        /// Updates the PawnIO install button state and dropdown option based on driver installation status.
        /// PawnIO option is always visible but disabled if not installed.
        /// </summary>
        private void UpdatePawnIOInstalledUI(bool installed)
        {
            // PawnIO option is always visible, but enable/disable based on installation status
            // This prevents WinRing0 from being auto-selected when PawnIO detection is delayed
            if (TdpMethodPawnIOItem != null)
            {
                // Keep PawnIO visible always - just update text to show status
                TdpMethodPawnIOItem.Visibility = Visibility.Visible;
                TdpMethodPawnIOItem.IsEnabled = installed;
                TdpMethodPawnIOItem.Content = installed ? "PawnIO" : "PawnIO (Not Installed)";
                Logger.Info($"PawnIO TDP option enabled: {installed}");

                // If PawnIO was selected but is no longer installed, switch to WMI only
                // NEVER auto-switch to WinRing0 - user must explicitly choose it
                if (!installed && TdpMethodComboBox != null)
                {
                    var selectedItem = TdpMethodComboBox.SelectedItem as ComboBoxItem;
                    if (selectedItem?.Tag is string tag && tag == "PawnIO")
                    {
                        // Try to select ManufacturerWMI, don't fall back to WinRing0
                        if (TdpMethodWmiItem?.Visibility == Visibility.Visible)
                        {
                            TdpMethodComboBox.SelectedItem = TdpMethodWmiItem;
                        }
                        // If WMI not available, leave selection as-is or clear it
                        // User will need to reinstall PawnIO or manually select WinRing0
                    }
                }
            }

            if (InstallPawnIOButton != null)
            {
                InstallPawnIOButton.Content = installed ? "Installed" : "Install";
                InstallPawnIOButton.IsEnabled = !installed;
                Logger.Info($"PawnIO install button updated: installed={installed}");

                // Update XY navigation to skip disabled button
                // TDPSettingsExpandButton.XYFocusUp must always point to SystemNavItem (for navigating out of card)
                if (TdpMethodComboBox != null && TDPSettingsExpandButton != null)
                {
                    if (installed)
                    {
                        // Skip disabled Install button: ComboBox -> next slider
                        TdpMethodComboBox.XYFocusDown = TDPLimitsMinSlider;
                        if (TDPLimitsMinSlider != null)
                        {
                            TDPLimitsMinSlider.XYFocusUp = TdpMethodComboBox;
                        }
                    }
                    else
                    {
                        TdpMethodComboBox.XYFocusDown = InstallPawnIOButton;
                        if (TDPLimitsMinSlider != null && InstallPawnIOButton != null)
                        {
                            TDPLimitsMinSlider.XYFocusUp = InstallPawnIOButton;
                        }
                    }
                    // Always allow navigating up from card header to nav bar
                    TDPSettingsExpandButton.XYFocusUp = SystemNavItem;
                }
            }

            if (PawnIOStatusText != null)
            {
                if (installed)
                {
                    PawnIOStatusText.Text = "PawnIO driver is installed. Signed kernel driver for anti-cheat safe hardware access.";
                }
                else
                {
                    PawnIOStatusText.Text = "Signed kernel driver for hardware access. Replaces WinRing0.";
                }
            }

            // Ensure a valid option is selected after visibility changes
            EnsureValidTdpMethodSelected();
        }

        /// <summary>
        /// Handles the PawnIO install button click.
        /// After installation, the helper restarts to reinitialize with PawnIO support.
        /// </summary>
        private async void InstallPawnIOButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("InstallPawnIOButton clicked - triggering PawnIO installation");

                // Update button to show installing state
                if (InstallPawnIOButton != null)
                {
                    InstallPawnIOButton.Content = "Installing...";
                    InstallPawnIOButton.IsEnabled = false;
                }

                // Trigger the installation via the property
                installPawnIO?.TriggerInstall();

                // Wait for helper to complete installation and exit
                // The helper exits after successful PawnIO installation
                Logger.Info("Waiting for PawnIO installation to complete...");
                await Task.Delay(5000);

                // Check if helper is still connected, if not, relaunch it
                // The helper will have exited after successful installation
                Logger.Info("Relaunching helper after PawnIO installation...");
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();

                // Wait for helper to start and reinitialize
                await Task.Delay(2000);
                Logger.Info("Helper relaunched after PawnIO installation");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during PawnIO installation: {ex.Message}");
                // Reset button state on error
                if (InstallPawnIOButton != null)
                {
                    InstallPawnIOButton.Content = "Install";
                    InstallPawnIOButton.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// Updates the ViGEmBus install button state based on driver installation status.
        /// </summary>
        private void UpdateViGEmBusInstalledUI(bool installed)
        {
            if (ViGEmBusStatusText != null)
            {
                ViGEmBusStatusText.Text = installed ? "Status: Installed" : "Status: Not Installed";
                ViGEmBusStatusText.Foreground = installed
                    ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.LimeGreen)
                    : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
            }

            if (ViGEmBusInstallButton != null)
            {
                ViGEmBusInstallButton.Content = installed ? "Installed" : "Install ViGEmBus";
                ViGEmBusInstallButton.IsEnabled = !installed;
            }

            if (ControllerEmulationViGEmBusStatusText != null)
            {
                ControllerEmulationViGEmBusStatusText.Text = installed ? "ViGEmBus: Installed" : "ViGEmBus: Not Installed";
                ControllerEmulationViGEmBusStatusText.Foreground = installed
                    ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.LimeGreen)
                    : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
            }

            if (ControllerEmulationViGEmBusInstallButton != null)
            {
                ControllerEmulationViGEmBusInstallButton.Content = installed ? "Installed" : "Install ViGEmBus";
                ControllerEmulationViGEmBusInstallButton.IsEnabled = !installed;
            }

            Logger.Info($"ViGEmBus install UI updated: installed={installed}");
        }

        /// <summary>
        /// Handles the ViGEmBus install button click.
        /// </summary>
        private async void ViGEmBusInstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("ViGEmBusInstallButton clicked - triggering ViGEmBus installation");

                // Update button to show installing state
                if (ViGEmBusInstallButton != null)
                {
                    ViGEmBusInstallButton.Content = "Installing...";
                    ViGEmBusInstallButton.IsEnabled = false;
                }

                if (ControllerEmulationViGEmBusInstallButton != null)
                {
                    ControllerEmulationViGEmBusInstallButton.Content = "Installing...";
                    ControllerEmulationViGEmBusInstallButton.IsEnabled = false;
                }

                if (ViGEmBusStatusText != null)
                {
                    ViGEmBusStatusText.Text = "Status: Installing...";
                }

                if (ControllerEmulationViGEmBusStatusText != null)
                {
                    ControllerEmulationViGEmBusStatusText.Text = "ViGEmBus: Installing...";
                }

                // Trigger the installation via the property
                installViGEmBus?.TriggerInstall();

                // The helper will send an updated status after installation completes
                Logger.Info("ViGEmBus installation triggered, waiting for helper response...");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during ViGEmBus installation: {ex.Message}");
                // Reset button state on error
                if (ViGEmBusInstallButton != null)
                {
                    ViGEmBusInstallButton.Content = "Install ViGEmBus";
                    ViGEmBusInstallButton.IsEnabled = true;
                }
                if (ControllerEmulationViGEmBusInstallButton != null)
                {
                    ControllerEmulationViGEmBusInstallButton.Content = "Install ViGEmBus";
                    ControllerEmulationViGEmBusInstallButton.IsEnabled = true;
                }
                if (ViGEmBusStatusText != null)
                {
                    ViGEmBusStatusText.Text = "Status: Error";
                }
                if (ControllerEmulationViGEmBusStatusText != null)
                {
                    ControllerEmulationViGEmBusStatusText.Text = "ViGEmBus: Error";
                }
            }
        }

        /// <summary>
        /// Updates the HidHide install button state based on installation status.
        /// </summary>
        private void UpdateHidHideInstalledUI(bool installed)
        {
            if (ControllerEmulationHidHideStatusText != null)
            {
                ControllerEmulationHidHideStatusText.Text = installed ? "HidHide: Installed" : "HidHide: Not Installed";
                ControllerEmulationHidHideStatusText.Foreground = installed
                    ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.LimeGreen)
                    : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
            }

            if (ControllerEmulationHidHideInstallButton != null)
            {
                ControllerEmulationHidHideInstallButton.Content = installed ? "Installed" : "Install HidHide";
                ControllerEmulationHidHideInstallButton.IsEnabled = !installed;
            }

            Logger.Info($"HidHide install UI updated: installed={installed}");
        }

        /// <summary>
        /// Handles the HidHide install button click.
        /// </summary>
        private void HidHideInstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("HidHideInstallButton clicked - triggering HidHide installation");

                if (ControllerEmulationHidHideInstallButton != null)
                {
                    ControllerEmulationHidHideInstallButton.Content = "Installing...";
                    ControllerEmulationHidHideInstallButton.IsEnabled = false;
                }

                if (ControllerEmulationHidHideStatusText != null)
                {
                    ControllerEmulationHidHideStatusText.Text = "HidHide: Installing...";
                }

                installHidHide?.TriggerInstall();
                Logger.Info("HidHide installation triggered, waiting for helper response...");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during HidHide installation: {ex.Message}");
                if (ControllerEmulationHidHideInstallButton != null)
                {
                    ControllerEmulationHidHideInstallButton.Content = "Install HidHide";
                    ControllerEmulationHidHideInstallButton.IsEnabled = true;
                }

                if (ControllerEmulationHidHideStatusText != null)
                {
                    ControllerEmulationHidHideStatusText.Text = "HidHide: Error";
                }
            }
        }

        /// <summary>
        /// Shows or hides the Custom TDP card based on performance mode
        /// </summary>
        private void SetCustomTDPVisibility(bool visible)
        {
            if (LegionCustomTDPCard != null)
            {
                LegionCustomTDPCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Custom TDP card visibility set to: {visible}");
            }

            // Update XY focus navigation for controller navigation
            // When Custom TDP card is hidden, focus should skip directly to fan controls
            if (LegionPerformanceModeComboBox != null)
            {
                if (visible && LegionCustomTDPSlowSlider != null)
                {
                    LegionPerformanceModeComboBox.XYFocusDown = LegionCustomTDPSlowSlider;
                }
                else if (LegionFanFullSpeedToggle != null)
                {
                    LegionPerformanceModeComboBox.XYFocusDown = LegionFanFullSpeedToggle;
                }
            }

            // Also update XYFocusUp on fan controls for navigation back up
            if (LegionFanFullSpeedToggle != null)
            {
                if (visible && LegionCustomTDPPeakSlider != null)
                {
                    // When Custom TDP visible, navigate up to the last slider (Peak/Sustained)
                    LegionFanFullSpeedToggle.XYFocusUp = LegionCustomTDPPeakSlider;
                }
                else if (LegionPerformanceModeComboBox != null)
                {
                    // When Custom TDP hidden, navigate up directly to performance mode dropdown
                    LegionFanFullSpeedToggle.XYFocusUp = LegionPerformanceModeComboBox;
                }
            }

            // Enable/disable fan curve card based on Custom mode
            // Preset modes (Quiet, Balanced, Performance) have built-in fan curves managed by hardware
            // Custom fan curves should only be editable in Custom mode
            if (LegionFanCurveCard != null)
            {
                // Don't hide the card, just disable interaction when not in Custom mode
                LegionFanCurveCard.IsHitTestVisible = visible;
                LegionFanCurveCard.Opacity = visible ? 1.0 : 0.5;
                Logger.Info($"Fan curve card enabled: {visible} (Custom mode: {visible})");
            }

            // Update the fan curve preset dropdown to show the mode restriction
            if (FanCurvePresetComboBox != null)
            {
                FanCurvePresetComboBox.IsEnabled = visible;
            }
        }

        /// <summary>
        /// Toggles the ColorPicker visibility
        /// </summary>
        private void LegionColorExpandButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (LegionColorPicker != null)
                {
                    bool isExpanded = LegionColorPicker.Visibility == Visibility.Visible;
                    LegionColorPicker.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;

                    // Update button icon (chevron down/up)
                    if (LegionColorExpandButton != null)
                    {
                        LegionColorExpandButton.Content = isExpanded ? "\uE70D" : "\uE70E";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionColorExpandButton_Click: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles ColorPicker color changes and updates the preview
        /// </summary>
        private void LegionColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            try
            {
                // Update color preview
                if (LegionColorPreview != null)
                {
                    LegionColorPreview.Background = new SolidColorBrush(args.NewColor);
                }

                legionLightColor?.OnColorChanged(args.NewColor);

                // Save to controller profile (handler is detached during profile loading)
                ControllerSettingChanged(sender, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionColorPicker_ColorChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles brightness slider changes
        /// </summary>
        private void LegionBrightnessSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            try
            {
                if (LegionBrightnessSlider != null && LegionBrightnessValue != null)
                {
                    int brightness = (int)LegionBrightnessSlider.Value;
                    LegionBrightnessValue.Text = $"{brightness}%";
                }
                // Save to controller profile
                ControllerSettingChanged(sender, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionBrightnessSlider_ValueChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles speed slider changes
        /// </summary>
        private void LegionSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            try
            {
                if (LegionSpeedSlider != null && LegionSpeedValue != null)
                {
                    int speed = (int)LegionSpeedSlider.Value;
                    LegionSpeedValue.Text = $"{speed}%";
                }

                // Save to controller profile (ControllerSettingChanged checks for loading state)
                ControllerSettingChanged(sender, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionSpeedSlider_ValueChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles light mode ComboBox selection - shows/hides appropriate controls
        /// Mode options visibility:
        /// - Off (0): hide all
        /// - Solid (1): Color + Brightness
        /// - Pulse (2): Color + Speed
        /// - Dynamic (3): Brightness + Speed
        /// - Spiral (4): Brightness + Speed
        /// </summary>
        private void LegionLightModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                UpdateLegionLightControlsVisibility();

                // Save to controller profile (handler is detached during profile loading)
                ControllerSettingChanged(sender, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionLightModeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the visibility of Legion light controls based on the selected mode
        /// </summary>
        private void UpdateLegionLightControlsVisibility()
        {
            if (LegionLightModeComboBox == null || LegionColorCard == null ||
                LegionBrightnessCard == null || LegionSpeedCard == null)
                return;

            int mode = LegionLightModeComboBox.SelectedIndex;

            // Off (0): hide all
            // Solid (1): Color + Brightness
            // Pulse (2): Color + Brightness + Speed
            // Dynamic (3): Brightness + Speed
            // Spiral (4): Brightness + Speed

            bool showColor = mode == 1 || mode == 2; // Solid, Pulse
            bool showBrightness = mode >= 1; // All modes except Off have brightness
            bool showSpeed = mode == 2 || mode == 3 || mode == 4; // Pulse, Dynamic, Spiral

            LegionColorCard.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
            LegionBrightnessCard.Visibility = showBrightness ? Visibility.Visible : Visibility.Collapsed;
            LegionSpeedCard.Visibility = showSpeed ? Visibility.Visible : Visibility.Collapsed;

            Logger.Info($"Legion light mode {mode}: Color={showColor}, Brightness={showBrightness}, Speed={showSpeed}");
        }

        /// <summary>
        /// Handles performance mode ComboBox selection in Legion tab
        /// </summary>
        private void LegionPerformanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Check if this change came from a pipe/property sync (not user interaction).
            // When true, TDPModeComboBox_SelectionChanged must skip profile saves to prevent
            // corrupting game profiles with global values during game exit transitions.
            bool fromHelperSync = legionPerformanceMode?.IsUpdatingUI == true;
            Logger.Info($"Legion Performance mode selection changed (fromHelperSync={fromHelperSync})");

            // Sync TDP Mode dropdown in Performance tab
            // Skip during initial sync - ApplyProfileTDPToHelper will set the correct value
            if (TDPModeComboBox != null && LegionPerformanceModeComboBox != null && !isInitialSync)
            {
                if (TDPModeComboBox.SelectedIndex != LegionPerformanceModeComboBox.SelectedIndex)
                {
                    // Set isApplyingHelperUpdate during sync so TDPModeComboBox_SelectionChanged
                    // returns early (skips profile save and value sends).
                    if (fromHelperSync)
                        isApplyingHelperUpdate = true;
                    try
                    {
                        TDPModeComboBox.SelectedIndex = LegionPerformanceModeComboBox.SelectedIndex;
                    }
                    finally
                    {
                        if (fromHelperSync)
                            isApplyingHelperUpdate = false;
                    }
                }
            }

            // Update TDP slider enabled state based on mode
            UpdateTDPSliderEnabledState();
        }

        /// <summary>
        /// Handles TDP Mode ComboBox selection in Performance tab (Legion devices only)
        /// </summary>
        private int lastTDPModeIndex = 1; // Track last index to avoid redundant updates (init to XAML default: Balanced)
        private double savedCustomTDP = 15; // Saved custom TDP value when switching away from Custom mode
        private void TDPModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TDPModeComboBox == null) return;

            int selectedIndex = TDPModeComboBox.SelectedIndex;
            if (selectedIndex < 0) return;

            Logger.Debug($"TDPModeComboBox_SelectionChanged: selectedIndex={selectedIndex}, lastTDPModeIndex={lastTDPModeIndex}, isApplyingHelperUpdate={isApplyingHelperUpdate}, isLoadingProfile={isLoadingProfile}");

            // Block only during active pipe operations (sync/message handling) or profile loading.
            // Use isApplyingHelperUpdate instead of isInitialSync so user clicks work during the
            // init window (20-50s). Programmatic changes from pipe sync are blocked by isApplyingHelperUpdate,
            // and post-sync ComboBox updates are blocked by the equality check (lastTDPModeIndex set first).
            if (isApplyingHelperUpdate || isLoadingProfile)
            {
                lastTDPModeIndex = selectedIndex;
                return;
            }

            // Skip if this is the same index as last time (avoid redundant processing)
            if (selectedIndex == lastTDPModeIndex)
            {
                Logger.Debug($"TDPModeComboBox_SelectionChanged skipped: selectedIndex={selectedIndex} == lastTDPModeIndex={lastTDPModeIndex}");
                return;
            }

            // Check if switching away from Custom mode (to save slider value)
            // Use lastTDPModeIndex to check the PREVIOUS selection, not the new one
            bool wasCustomMode = lastTDPModeIndex >= 0 && IsCustomTdpModeIndex(lastTDPModeIndex);
            if (wasCustomMode && TDPSlider != null)
            {
                savedCustomTDP = TDPSlider.Value;
                Logger.Info($"Saved custom TDP value: {savedCustomTDP}W before switching to preset mode");
            }

            lastTDPModeIndex = selectedIndex;

            // Use the new preset system to get TDP and Legion mode values
            bool isCustomMode = IsCustomTdpModeSelected();
            int presetTdpValue = GetCurrentPresetTdpValue();
            int legionModeValue = GetCurrentPresetLegionMode();
            bool? presetTdpBoost = GetCurrentPresetTdpBoost();

            Logger.Info($"TDP Mode selection changed to index {selectedIndex}: isCustom={isCustomMode}, presetTDP={presetTdpValue}W, legionMode={legionModeValue}, boost={presetTdpBoost}");

            bool isLegion = legionGoDetected?.Value == true;

            if (isLegion)
            {
                // Legion device: use hardware presets via WMI
                // For built-in presets with LegionModeValue (1/2/3), use hardware mode
                // For custom presets (LegionModeValue == 255), use Custom mode + software TDP

                // Sync with Legion Performance Mode ComboBox if using default mode (not custom presets)
                if (!useCustomTDPPresets && LegionPerformanceModeComboBox != null && LegionPerformanceModeComboBox.SelectedIndex != selectedIndex)
                {
                    LegionPerformanceModeComboBox.SelectedIndex = selectedIndex;
                }

                // Send Legion mode to helper - force send even if cached value matches
                if (legionPerformanceMode != null)
                {
                    if (legionPerformanceMode.Value == legionModeValue)
                    {
                        legionPerformanceMode.ForceSetValue(legionModeValue);
                    }
                    else
                    {
                        legionPerformanceMode.SetValue(legionModeValue);
                    }
                }

                // For custom presets without hardware mode (255), also apply TDP via software
                if (legionModeValue == 255 && !isCustomMode && presetTdpValue > 0)
                {
                    // Custom preset on Legion: apply TDP value via software
                    if (TDPSlider != null)
                    {
                        TDPSlider.Value = presetTdpValue;
                    }
                    if (tdp != null)
                    {
                        tdp.SetValue(presetTdpValue);
                        Logger.Info($"Legion device: Applied custom preset TDP {presetTdpValue}W via software");
                    }
                }
                // For actual Custom mode on Legion, restore saved custom TDP value
                else if (isCustomMode)
                {
                    if (TDPSlider != null)
                    {
                        TDPSlider.Value = savedCustomTDP;
                        Logger.Info($"Legion device: Restored custom TDP value to slider: {savedCustomTDP}W");
                    }
                }
            }
            else
            {
                // Generic device: apply TDP value directly based on preset
                if (isCustomMode)
                {
                    // Custom mode: restore saved custom TDP value to slider
                    if (TDPSlider != null)
                    {
                        TDPSlider.Value = savedCustomTDP;
                        Logger.Info($"Restored custom TDP value to slider: {savedCustomTDP}W");
                    }
                }
                else
                {
                    // Preset mode: set TDP directly and update slider to show the value
                    int targetTDP = presetTdpValue > 0 ? presetTdpValue : 15; // Default to 15W if something goes wrong
                    if (TDPSlider != null)
                    {
                        TDPSlider.Value = targetTDP;
                    }
                    if (tdp != null)
                    {
                        tdp.SetValue(targetTDP);
                        Logger.Info($"Generic device: Applied TDP preset {targetTDP}W (mode index {selectedIndex})");
                    }
                }
            }

            // Update TDP slider enabled state based on mode
            UpdateTDPSliderEnabledState();

            // Apply TDP Boost setting from preset (only for custom presets with TdpBoostEnabled set)
            // This should use software TDP control, so only apply when not using Lenovo hardware modes
            if (presetTdpBoost.HasValue && !isCustomMode)
            {
                bool shouldEnableTdpBoost = presetTdpBoost.Value;
                // Only apply if we're using software TDP control (not Lenovo hardware modes)
                // For Legion devices using hardware modes (legionModeValue != 255), TDP Boost is controlled by hardware
                if (legionModeValue == 255 || !(legionGoDetected?.Value == true))
                {
                    if (TDPBoostToggle != null)
                    {
                        TDPBoostToggle.IsOn = shouldEnableTdpBoost;
                    }
                    if (tdpBoostEnabled != null && tdpBoostEnabled.Value != shouldEnableTdpBoost)
                    {
                        tdpBoostEnabled.SetValue(shouldEnableTdpBoost);
                        Logger.Info($"Applied preset TDP Boost: {shouldEnableTdpBoost}");
                    }
                }
            }

            // Save profile when TDP Mode changes (if not during initialization or helper update)
            // Allow save if user-initiated from Quick Tab tile (bypasses isApplyingHelperUpdate)
            // Don't save when Default Game Profile is active (to avoid contaminating user's profile)
            if (!isInitialSync && !isLoadingProfile && SaveTDP && (!isApplyingHelperUpdate || isUserInitiatedTDPModeChange) && defaultGameProfileEnabled?.Value != true)
            {
                // Don't save to game profile if per-game profile is disabled.
                // During game close, helper sends global mode via pipe → ComboBox changes → handler fires.
                // At this point, perGameProfile.Value is already false (pipe message processed) but
                // currentProfileName still points to the game profile (SwitchProfile hasn't run yet).
                // Without this check, the global mode gets saved to the game profile, corrupting it.
                bool isGameProfile = currentProfileName?.StartsWith("Game_") == true;
                if (isGameProfile && perGameProfile?.Value != true)
                {
                    Logger.Warn($"TDP Mode save skipped: per-game profile is disabled but currentProfileName is still '{currentProfileName}' (game closing)");
                }
                else
                {
                    Logger.Info($"Saving TDP Mode change to profile: {currentProfileName}");
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
            }
            else
            {
                Logger.Warn($"TDP Mode save skipped: isInitialSync={isInitialSync}, isApplyingHelperUpdate={isApplyingHelperUpdate}, isLoadingProfile={isLoadingProfile}, SaveTDP={SaveTDP}, isUserInitiatedTDPModeChange={isUserInitiatedTDPModeChange}, defaultGameProfile={defaultGameProfileEnabled?.Value}");
            }
        }

        /// <summary>
        /// Updates TDP slider enabled state based on TDP Mode
        /// For all devices: slider disabled in preset modes (Quiet/Balanced/Performance), enabled in Custom mode
        /// Also updates XY focus bindings to skip disabled TDP slider
        /// </summary>
        private void UpdateTDPSliderEnabledState()
        {
            if (TDPSlider == null) return;

            // If Default Game Profile is active, keep TDP controls disabled
            if (defaultGameProfileEnabled?.Value == true)
            {
                Logger.Debug("TDP slider state update skipped - Default Game Profile is active");
                return;
            }

            // Check if in Custom mode (uses preset system for proper detection)
            bool isCustomMode = IsCustomTdpModeSelected();
            bool isLegion = legionGoDetected?.Value == true;

            // Check if the mode change came from helper pipe sync (not user interaction).
            // When true, widget's profile may have stale values - use helper's property values instead.
            bool isHelperModeSync = legionPerformanceMode?.IsUpdatingUI == true;

            // TDP slider, TDP Boost, and AutoTDP should only be enabled in Custom mode
            // Note: TDP slider also requires tdp property to be ready (IsEnabled is set elsewhere too)
            if (!isCustomMode)
            {
                TDPSlider.IsEnabled = false;

                // Update display to show preset name and TDP value
                int modeIndex = TDPModeComboBox?.SelectedIndex ?? 1;
                string modeName = "Balanced";
                int presetTdp = GetCurrentPresetTdpValue();

                if (useCustomTDPPresets && tdpPresets != null && modeIndex >= 0 && modeIndex < tdpPresets.Count)
                {
                    modeName = tdpPresets[modeIndex].Name;
                }
                else
                {
                    string[] defaultModeNames = { "Quiet", "Balanced", "Performance" };
                    modeName = (modeIndex >= 0 && modeIndex < defaultModeNames.Length) ? defaultModeNames[modeIndex] : "Balanced";
                }

                if (TDPValueText != null)
                {
                    // Show TDP value for custom presets, mode name for defaults
                    TDPValueText.Text = (useCustomTDPPresets && presetTdp > 0) ? $"{presetTdp}W" : $"{modeName} mode";
                }
                if (CurrentTDPValueText != null)
                {
                    CurrentTDPValueText.Text = (useCustomTDPPresets && presetTdp > 0) ? $"{modeName} ({presetTdp}W)" : $"{modeName} mode";
                }

                // Set flag to prevent toggle handlers from saving forced-off state to LocalSettings
                isUpdatingTDPMode = true;
                bool wasAutoTDPOn = AutoTDPToggle?.IsOn == true;
                bool wasStickyTDPOn = StickyTDPToggle?.IsOn == true;
                try
                {
                    // Also disable TDP Boost and AutoTDP controls in preset modes
                    if (TDPBoostToggle != null)
                    {
                        TDPBoostToggle.IsEnabled = false;
                        TDPBoostToggle.IsOn = false; // Turn off when switching to preset mode (binding handles slider visibility)
                    }
                    if (AutoTDPToggle != null)
                    {
                        AutoTDPToggle.IsEnabled = false;
                        AutoTDPToggle.IsOn = false; // Turn off when switching to preset mode
                    }
                    if (AutoTDPTargetFPSSlider != null) AutoTDPTargetFPSSlider.IsEnabled = false;
                    if (AutoTDPMinSlider != null) AutoTDPMinSlider.IsEnabled = false;
                    if (AutoTDPMaxSlider != null) AutoTDPMaxSlider.IsEnabled = false;
                    if (StickyTDPToggle != null)
                    {
                        StickyTDPToggle.IsEnabled = false;
                        StickyTDPToggle.IsOn = false; // Turn off when switching to preset mode
                    }
                    if (StickyTDPIntervalSlider != null) StickyTDPIntervalSlider.IsEnabled = false;
                }
                finally
                {
                    isUpdatingTDPMode = false;
                }

                // Explicitly notify helper to disable AutoTDP/StickyTDP since toggle handlers were blocked.
                // Skip when:
                // - isLoadingProfile: helper manages state during profile switches, ComboBox may show wrong mode
                // - isHelperModeSync: mode change came from helper pipe sync (not user).
                //   During game close, helper sends global mode → widget shouldn't send stale values back.
                //   During game open, helper sends game mode → widget's profile may have stale AutoTDP.
                if (wasAutoTDPOn && !isLoadingProfile && !isHelperModeSync)
                {
                    autoTDPEnabled?.SetValue(false);
                    Logger.Info("AutoTDP disabled due to TDP mode change away from Custom");
                }
                if (wasStickyTDPOn)
                {
                    StopStickyTDPTimer();
                    Logger.Info("Sticky TDP disabled due to TDP mode change away from Custom");
                }

                // Update XY focus to skip disabled controls
                // TDPModeComboBox -> OSPowerModeComboBox (skip all TDP controls)
                if (TDPModeComboBox != null && OSPowerModeComboBox != null)
                {
                    TDPModeComboBox.XYFocusDown = OSPowerModeComboBox;
                    OSPowerModeComboBox.XYFocusUp = TDPModeComboBox;
                }

                Logger.Debug($"TDP slider disabled - using {modeName} mode");
            }
            else
            {
                // In Custom mode, enable if tdp property is ready
                TDPSlider.IsEnabled = tdp != null;

                // Re-enable TDP Boost, AutoTDP, and Sticky TDP controls in Custom mode
                if (TDPBoostToggle != null) TDPBoostToggle.IsEnabled = true;
                if (AutoTDPToggle != null) AutoTDPToggle.IsEnabled = true;
                if (AutoTDPTargetFPSSlider != null) AutoTDPTargetFPSSlider.IsEnabled = true;
                if (AutoTDPMinSlider != null) AutoTDPMinSlider.IsEnabled = true;
                if (AutoTDPMaxSlider != null) AutoTDPMaxSlider.IsEnabled = true;
                if (StickyTDPToggle != null) StickyTDPToggle.IsEnabled = true;
                if (StickyTDPIntervalSlider != null) StickyTDPIntervalSlider.IsEnabled = true;

                // Restore toggle states when switching back to Custom mode.
                // When SaveAutoTDP/SaveTDP is enabled, use the CURRENT PROFILE's values (source of truth)
                // instead of LocalSettings. LocalSettings stores a global value that can be stale
                // (e.g., AutoTDP=true from a per-game profile bleeds into global via deferred UI updates).
                // Only fall back to LocalSettings when the profile system doesn't manage the setting.
                isUpdatingTDPMode = true;
                try
                {
                    var settings = ApplicationData.Current.LocalSettings;

                    // Skip sending to helper when:
                    // - isLoadingProfile: profile is being loaded, helper manages state
                    // - isHelperModeSync: mode change came from helper pipe sync, widget's profile
                    //   may have stale values (e.g., AutoTDP=false saved during game close)
                    bool canSendToHelper = !isLoadingProfile && !isHelperModeSync;

                    if (TDPBoostToggle != null)
                    {
                        if (SaveTDP)
                        {
                            var profile = GetProfile(currentProfileName);
                            // When helper is syncing mode, use the helper's property value.
                            bool tdpBoostState = isHelperModeSync && this.tdpBoostEnabled != null
                                ? this.tdpBoostEnabled.Value
                                : profile.TDPBoostEnabled;
                            TDPBoostToggle.IsOn = tdpBoostState;
                            if (canSendToHelper)
                                this.tdpBoostEnabled?.SetValue(tdpBoostState);
                            Logger.Debug($"Restored TDP Boost from {(isHelperModeSync ? "helper" : "profile")} '{currentProfileName}': {tdpBoostState}");
                        }
                        else if (settings.Values.TryGetValue("TDPBoostEnabled", out object tdpBoostVal) && tdpBoostVal is bool tdpBoostEnabledVal)
                        {
                            TDPBoostToggle.IsOn = tdpBoostEnabledVal;
                            if (canSendToHelper)
                                this.tdpBoostEnabled?.SetValue(tdpBoostEnabledVal);
                            Logger.Debug($"Restored TDP Boost from LocalSettings: {tdpBoostEnabledVal}");
                        }
                    }

                    if (AutoTDPToggle != null)
                    {
                        isLoadingAutoTDPSettings = true;
                        try
                        {
                            if (SaveAutoTDP)
                            {
                                var profile = GetProfile(currentProfileName);
                                // When helper is syncing mode, use the helper's property value.
                                // The widget's profile may be stale (e.g., AutoTDP=false saved during
                                // game close). The helper's autoTDPEnabled.Value is the source of truth.
                                bool autoTDPState = isHelperModeSync && autoTDPEnabled != null
                                    ? autoTDPEnabled.Value
                                    : profile.AutoTDPEnabled;
                                AutoTDPToggle.IsOn = autoTDPState;
                                if (canSendToHelper)
                                    autoTDPEnabled?.SetValue(autoTDPState);
                                Logger.Debug($"Restored AutoTDP from {(isHelperModeSync ? "helper" : "profile")} '{currentProfileName}': {autoTDPState}");
                            }
                            else if (settings.Values.TryGetValue("AutoTDPEnabled", out object autoTdpVal) && autoTdpVal is bool autoTdpEnabled)
                            {
                                AutoTDPToggle.IsOn = autoTdpEnabled;
                                if (canSendToHelper)
                                    autoTDPEnabled?.SetValue(autoTdpEnabled);
                                Logger.Debug($"Restored AutoTDP from LocalSettings: {autoTdpEnabled}");
                            }
                        }
                        finally
                        {
                            isLoadingAutoTDPSettings = false;
                        }
                    }

                    if (StickyTDPToggle != null && settings.Values.TryGetValue("StickyTDPEnabled", out object stickyVal) && stickyVal is bool stickyEnabled)
                    {
                        StickyTDPToggle.IsOn = stickyEnabled;
                        if (stickyEnabled)
                        {
                            targetTDPLimit = TDPSlider.Value;
                            StartStickyTDPTimer();
                            Logger.Debug($"Restored Sticky TDP enabled - monitoring TDP: {targetTDPLimit}W");
                        }
                        else
                        {
                            StopStickyTDPTimer();
                        }
                        Logger.Debug($"Restored Sticky TDP toggle state from LocalSettings: {stickyEnabled}");
                    }
                }
                finally
                {
                    isUpdatingTDPMode = false;
                }

                Logger.Debug($"TDP slider, TDP Boost, AutoTDP, and Sticky TDP enabled in Custom mode: {TDPSlider.IsEnabled}");

                // Update display to show wattage in Custom mode
                UpdateTDPDisplayText();

                // CRITICAL FIX: Sync TDPProperty.Value with the slider's current visual value
                // When TDP sync is skipped (preset modes), TDPProperty.Value stays at initial value (4).
                // Profile loads set TDPSlider.Value but not TDPProperty.Value.
                // Without this sync, Slider_ValueChanged comparison (newValue != Value) fails
                // because the property's Value doesn't match what the user sees on screen.
                if (tdp != null)
                {
                    int currentSliderValue = (int)TDPSlider.Value;
                    tdp.StopDebounceTimer(); // Cancel any pending debounce
                    tdp.SetValueSilent(currentSliderValue); // Update internal Value without sending

                    // Also send current value to helper to ensure hardware matches UI
                    tdp.ForceSetValue(currentSliderValue);
                    Logger.Info($"Custom mode enabled - synced TDP property to slider value: {currentSliderValue}W");
                }

                // Restore normal XY focus chain in Custom mode
                // TDPModeComboBox -> TDPSlider -> TDPExtrasExpandToggle -> [expanded: TDPBoost/AutoTDP/Sticky] -> OSPowerModeComboBox
                if (TDPModeComboBox != null && OSPowerModeComboBox != null)
                {
                    TDPModeComboBox.XYFocusDown = TDPSlider;
                    TDPSlider.XYFocusUp = TDPModeComboBox;
                    TDPSlider.XYFocusDown = TDPExtrasExpandToggle;
                    TDPExtrasExpandToggle.XYFocusUp = TDPSlider;

                    // Internal chain when TDP Extras is expanded
                    TDPBoostToggle.XYFocusUp = TDPExtrasExpandToggle;
                    TDPBoostToggle.XYFocusDown = AutoTDPToggle;
                    AutoTDPToggle.XYFocusUp = TDPBoostToggle;
                    AutoTDPToggle.XYFocusDown = StickyTDPToggle;
                    StickyTDPToggle.XYFocusUp = AutoTDPToggle;
                    StickyTDPToggle.XYFocusDown = OSPowerModeComboBox;

                    // TDPExtrasExpandToggle.XYFocusDown depends on expanded state
                    bool isTDPExtrasOpen = TDPExtrasContent?.Visibility == Visibility.Visible;
                    TDPExtrasExpandToggle.XYFocusDown = isTDPExtrasOpen ? (DependencyObject)TDPBoostToggle : OSPowerModeComboBox;
                    OSPowerModeComboBox.XYFocusUp = isTDPExtrasOpen ? (DependencyObject)StickyTDPToggle : TDPExtrasExpandToggle;
                    Logger.Debug($"XY focus restored for Custom mode (TDP Extras expanded: {isTDPExtrasOpen})");
                }
            }
        }

        /// <summary>
        /// Handles Custom TDP slider changes and updates the value labels
        /// Note: The actual value sync is handled by WidgetSliderProperty's built-in debounce
        /// </summary>
        private void LegionCustomTDPSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            try
            {
                // Update value labels immediately for visual feedback
                if (LegionCustomTDPSlowSlider != null && LegionCustomTDPSlowValue != null)
                {
                    LegionCustomTDPSlowValue.Text = $"{(int)LegionCustomTDPSlowSlider.Value}W";
                }
                if (LegionCustomTDPFastSlider != null && LegionCustomTDPFastValue != null)
                {
                    LegionCustomTDPFastValue.Text = $"{(int)LegionCustomTDPFastSlider.Value}W";
                }
                if (LegionCustomTDPPeakSlider != null && LegionCustomTDPPeakValue != null)
                {
                    LegionCustomTDPPeakValue.Text = $"{(int)LegionCustomTDPPeakSlider.Value}W";
                }
                // The WidgetSliderProperty handles debouncing and sending to helper
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionCustomTDPSlider_ValueChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the TDP display text when slider value changes (Custom mode only)
        /// </summary>
        private void TDPSlider_ValueChanged_UpdateDisplay(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // Only update display if in Custom mode and slider is enabled
            // The slider is disabled in preset modes, so this prevents overwriting mode names
            if (IsCustomTdpModeSelected() && TDPSlider?.IsEnabled == true)
            {
                UpdateTDPDisplayText();
            }
        }

        /// <summary>
        /// Updates TDP display text to show current wattage (for Custom mode)
        /// </summary>
        private void UpdateTDPDisplayText()
        {
            if (TDPSlider == null) return;

            int tdpValue = (int)TDPSlider.Value;
            if (TDPValueText != null)
            {
                TDPValueText.Text = $"{tdpValue}W";
            }
            // Note: CurrentTDPValueText is updated by the helper with actual hardware limits
            // via CurrentTDPProperty. Don't set it here - slider values would overwrite
            // the detailed hardware readout (e.g., "S:21W F:21W L:21W").
        }

    }
}
