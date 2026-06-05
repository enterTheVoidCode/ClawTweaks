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

        // ── Performance-tab nav spine: "always-focusable" helpers ───────────────
        //
        // UWP IsEnabled=false makes a control completely invisible to every focus
        // mechanism — XYFocusUp, TryMoveFocus, and even programmatic Focus() all
        // silently fail on a disabled control. The only way to let the gamepad D-Pad
        // navigate THROUGH controls that are logically inactive (no game running /
        // RTSS not available) is to keep them IsEnabled=true at all times and use
        // Opacity to show the inactive appearance (0.45 = same as UWP's default
        // disabled opacity). An _available flag plus an A-button intercept in
        // PreviewKeyDown prevents the user from accidentally activating them.
        //
        // Spine (top→bottom, MSI Claw):
        //   PerGameProfileToggle → FPSLimitToggle → [FPS mode/slider when expanded]
        //   → TDPSlider → TDPBoostToggle → TDPBoostFPPTSliderCard
        //   → OSPowerModeComboBox → CPUBoostToggle → PerformanceOverlayComboBox → (loop)
        // ────────────────────────────────────────────────────────────────────────────────

        // --- Availability flags for always-enabled nav-spine controls ----------------

        /// <summary>true while a game is running and a per-game profile can be used.</summary>
        private bool _perGameProfileAvailable = false;

        /// <summary>true while RTSS or Intel IGCL is available for FPS limiting.</summary>
        private bool _fpsLimitNavAvailable = false;

        /// <summary>
        /// Update the per-game profile toggle's interactive availability without touching
        /// IsEnabled. Keeps the control focusable so the gamepad spine navigation works.
        /// </summary>
        internal void SetPerGameProfileAvailable(bool available)
        {
            _perGameProfileAvailable = available;
            if (PerGameProfileToggle != null)
                PerGameProfileToggle.Opacity = available ? 1.0 : 0.45;
        }

        /// <summary>
        /// Update the FPS-limit toggle's interactive availability without touching IsEnabled.
        /// Sub-controls (slider, mode radios) still use IsEnabled=false when unavailable —
        /// they are not part of the main nav spine so do not need to stay focusable.
        /// </summary>
        internal void SetFpsLimitNavAvailable(bool available)
        {
            _fpsLimitNavAvailable = available;
            if (FPSLimitToggle != null)
                FPSLimitToggle.Opacity = available ? 1.0 : 0.45;
        }

        private void PerfNav_FocusUp(Windows.UI.Xaml.Controls.Control target)
        {
            target?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
        }

        private void PerGameProfileToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                PerformanceNavItem?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                FPSLimitToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void FPSLimitToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // D-pad Up is now owned by GamingWidget_PreviewKeyDown (tunneling) — do not handle here
            // to avoid double-navigation conflicts. PreviewKeyDown calls TryMoveFocus(Up) first and
            // falls back to FocusActiveTab(); it always sets e.Handled so this branch never fires.
            if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                // Only navigate into FPS sub-controls when the FPS limit feature is available
                // (RTSS / Intel IGCL present). When unavailable, sub-controls have IsEnabled=false
                // so Focus() on them silently fails — skip directly to TDPSlider instead.
                Windows.UI.Xaml.Controls.Control target = null;
                if (_fpsLimitNavAvailable && FPSModeRTSSRadio?.IsEnabled == true) target = FPSModeRTSSRadio;
                else if (_fpsLimitNavAvailable && FPSLimitSlider?.IsEnabled == true) target = FPSLimitSlider;
                else target = TDPSlider;
                target?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void FPSModeRTSSRadio_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                FPSLimitToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                FPSLimitSlider?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void FPSLimitSlider_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                FPSModeRTSSRadio?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                TDPSlider?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void TDPSlider_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                // D-pad Up is now owned by GamingWidget_PreviewKeyDown (tunneling) and will have
                // already fired and set e.Handled before this bubbling handler runs.
                // This branch is kept as a safety fallback (e.g. keyboard Up key, which is not
                // intercepted by PreviewKeyDown) and correctly checks IsEnabled before focusing
                // to avoid the old bug where cast-based ?? always resolved to a non-null but
                // disabled FPSLimitSlider, leaving focus stuck on the TDP slider.
                Windows.UI.Xaml.Controls.Control target = null;
                if (FPSLimitSlider?.IsEnabled == true)           target = FPSLimitSlider;
                else if (FPSModeRTSSRadio?.IsEnabled == true)    target = FPSModeRTSSRadio;
                else if (FPSLimitToggle?.IsEnabled == true)      target = FPSLimitToggle;
                else if (PerGameProfileToggle?.IsEnabled == true) target = PerGameProfileToggle;
                else                                             target = PerformanceNavItem;
                target?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                TDPBoostToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void TDPBoostToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                TDPSlider?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                TDPBoostFPPTSliderCard?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void TDPBoostFPPTSliderCard_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                TDPBoostToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                OSPowerModeComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void OSPowerModeComboBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (OSPowerModeComboBox?.IsDropDownOpen == true) return;
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                TDPBoostFPPTSliderCard?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                CPUBoostToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void CPUBoostToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                OSPowerModeComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                PerformanceOverlayComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        /// <summary>
        /// UWP ComboBox consumes D-Pad Up/Down for item selection, bypassing XYFocusUp/Down.
        /// This handler intercepts Up/Down and manually moves focus to the correct neighbour.
        /// </summary>
        private void PerformanceOverlayComboBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (PerformanceOverlayComboBox?.IsDropDownOpen == true) return; // let the open dropdown handle keys

            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                var target = CPUBoostToggle ?? (Windows.UI.Xaml.Controls.Control)OSPowerModeComboBox;
                target?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                // On MSI Claw the fan card sits below the overlay card — continue into it.
                if (MsiFanCard != null && MsiFanCard.Visibility == Windows.UI.Xaml.Visibility.Visible && MsiFanEnableToggle != null)
                {
                    MsiFanEnableToggle.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                }
                else
                {
                    // Loop back to top of Performance tab
                    var target = PerGameProfileToggle ?? (Windows.UI.Xaml.Controls.Control)FPSLimitToggle;
                    target?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                }
                e.Handled = true;
            }
        }

        private void PerformanceOverlayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PerformanceOverlayComboBox != null && PerformanceOverlaySlider != null)
            {
                // Sync the hidden slider value with the selected combobox item
                int index = PerformanceOverlayComboBox.SelectedIndex;
                if (index >= 0)
                {
                    if (osdProvider == 1) // AMD
                    {
                        // For AMD: index 0 = Off, index 1-3 maps to AMD levels
                        if (index == 0 && amdOverlayLevel > 0)
                        {
                            // Turn off AMD overlay
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 0;
                            SaveAMDOverlayLevel();
                            Logger.Info("AMD Overlay toggled OFF via ComboBox");
                        }
                        else if (index > 0 && amdOverlayLevel == 0)
                        {
                            // Turn on AMD overlay (starts at level 1)
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 1;
                            SaveAMDOverlayLevel();
                            Logger.Info("AMD Overlay toggled ON via ComboBox");
                        }
                        // Note: We can't set specific AMD levels directly, only cycle
                        UpdateQuickSettingsTileStates();
                    }
                    else // RTSS
                    {
                        PerformanceOverlaySlider.Value = index;
                    }
                    // Save the setting (but not during initial load)
                    if (!isLoadingPerformanceOverlaySetting)
                    {
                        SavePerformanceOverlaySetting();

                        // Also update current profile's OverlayLevel if SaveOverlayLevel is enabled
                        // This ensures the profile stays in sync with the user's selection
                        if (SaveOverlayLevel && !string.IsNullOrEmpty(currentProfileName))
                        {
                            var profile = GetProfile(currentProfileName);
                            if (profile != null)
                            {
                                profile.OverlayLevel = index;
                                SaveProfileToStorage(currentProfileName, profile);
                                Logger.Debug($"Updated profile '{currentProfileName}' OverlayLevel to {index}");
                            }
                        }
                    }
                }
            }
        }

        private void LoadPerformanceOverlaySetting()
        {
            try
            {
                if (PerformanceOverlayComboBox == null) return;
                isLoadingPerformanceOverlaySetting = true;
                var settings = ApplicationData.Current.LocalSettings;

                int level;
                if (settings.Values.TryGetValue("PerformanceOverlayLevel", out object val) && val is int savedLevel
                    && savedLevel >= 0 && savedLevel < PerformanceOverlayComboBox.Items.Count)
                {
                    level = savedLevel;
                    Logger.Debug($"Loaded PerformanceOverlayLevel: {level}");
                }
                else
                {
                    // No saved value (fresh install / factory reset) — default to H. Detailed (index 3).
                    // The helper persists OSD level independently and defaults to the same value,
                    // so this keeps the UI in sync with what's actually active in the overlay.
                    level = 3;
                    settings.Values["PerformanceOverlayLevel"] = level;
                    Logger.Info($"No saved PerformanceOverlayLevel — defaulting to H. Detailed (level {level})");
                }

                PerformanceOverlayComboBox.SelectedIndex = level;
                // Set the osd property directly to avoid debounce delay
                osd?.SetValue(level);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading PerformanceOverlay setting: {ex.Message}");
            }
            finally
            {
                isLoadingPerformanceOverlaySetting = false;
            }
        }

        private void SavePerformanceOverlaySetting()
        {
            try
            {
                if (PerformanceOverlayComboBox == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                int level = PerformanceOverlayComboBox.SelectedIndex;
                settings.Values["PerformanceOverlayLevel"] = level;
                Logger.Debug($"Saved PerformanceOverlayLevel: {level}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving PerformanceOverlay setting: {ex.Message}");
            }
        }

        private void SaveAMDOverlayLevel()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["AMD_OverlayLevel"] = amdOverlayLevel;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving AMD overlay level: {ex.Message}");
            }
        }

        private void PerformanceOverlaySlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (PerformanceOverlaySlider != null && PerformanceOverlayComboBox != null)
            {
                // Sync the ComboBox selection when slider value changes
                // (e.g., from property loading or helper updates)
                int newIndex = (int)Math.Round(e.NewValue);

                if (PerformanceOverlayComboBox.SelectedIndex != newIndex)
                {
                    PerformanceOverlayComboBox.SelectedIndex = newIndex;
                }
            }
        }

    }
}
