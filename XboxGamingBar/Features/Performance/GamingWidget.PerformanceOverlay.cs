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

        // ── Performance tab D-Pad navigation ────────────────────────────────────
        //
        // NAVIGATION SPINE (top→bottom) — FPS limiter restored to position 1 (above TDP):
        //   0 PerGameProfileToggle  (only when game running)
        //   1 FPSStateCycleButton (On/Off)  ←Left→ FPSModeToggle ←Left→ FPSLimitSlider
        //   2 TDPSlider
        //   3 TDPBoostToggle               ←Left→ TDPBoostFPPTSliderCard
        //   4 OSPowerModeComboBox
        //   5 CPUBoostToggle
        //   6 PerformanceOverlayComboBox → (loop)
        //
        // Every spine hop is done EXPLICITLY via .Focus(Keyboard) — the only mechanism that
        // navigates reliably on-device (TryMoveFocus does NOT). For SLIDERS, Up/Down is
        // intercepted in GamingWidget_PreviewKeyDown (tunneling) because sliders swallow the
        // arrow keys before these bubbling KeyDown handlers can fire.
        // ────────────────────────────────────────────────────────────────────────────────

        private void PerGameProfileToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Up: nothing above → nav bar (handled by PreviewKeyDown fallback). Down → FPS On/Off.
            if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                FPSStateCycleButton?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void FPSStateCycleButton_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // On/Off toggle (far right, spine pos 1). Up → Per-Game profile (or nav bar if it is
            // disabled), Down → TDPSlider, Left → Mode toggle.
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                if (PerGameProfileToggle?.IsEnabled == true)
                    PerGameProfileToggle.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else
                    FocusActiveTab(); // escape to nav bar
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                TDPSlider?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.GamepadDPadLeft)
            {
                FPSModeToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void TDPSlider_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Slider swallows arrow keys, so Up/Down are normally handled by PreviewKeyDown.
            // These remain as a fallback (e.g. focus arrived without the slider grabbing keys).
            // Up → FPS On/Off toggle (spine pos 1), Down → TDP Boost toggle.
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                FPSStateCycleButton?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                TDPBoostToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void FPSModeToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Mode toggle (middle). Left → slider; Right/Up/Down → On/Off toggle.
            if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.GamepadDPadLeft)
            {
                FPSLimitSlider?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right || e.Key == Windows.System.VirtualKey.GamepadDPadRight ||
                     e.Key == Windows.System.VirtualKey.Up    || e.Key == Windows.System.VirtualKey.GamepadDPadUp   ||
                     e.Key == Windows.System.VirtualKey.Down  || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                FPSStateCycleButton?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        // Registered with handledEventsToo=true (see WireFpsSliderKeyHandler) so it runs even after the
        // Slider consumed Left/Right for its built-in ±1. Up/Down navigate away; Left/Right change value.
        // Snap mode: jump to the previous/next step (overriding the built-in ±1 which ValueChanged would
        // just re-snap to the same step → the slider appeared stuck on D-pad). Stepless: leave the Slider's
        // built-in ±1 alone (don't double-apply).
        private void FPSLimitSlider_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.GamepadDPadUp:
                    FPSModeToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Down:
                case Windows.System.VirtualKey.GamepadDPadDown:
                    FpsSteplessCheckBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.GamepadDPadLeft:
                    if (FPSLimitSlider?.IsEnabled == true && !FpsSteplessEnabled) { AdjustFpsSlider(-1); e.Handled = true; }
                    break;
                case Windows.System.VirtualKey.Right:
                case Windows.System.VirtualKey.GamepadDPadRight:
                    if (FPSLimitSlider?.IsEnabled == true && !FpsSteplessEnabled) { AdjustFpsSlider(+1); e.Handled = true; }
                    break;
            }
        }

        /// <summary>
        /// Wire FPSLimitSlider_KeyDown with handledEventsToo=true so it fires even after the Slider's
        /// own OnKeyDown consumed Left/Right (otherwise the snap-mode override never runs). Call once
        /// after the control tree is loaded.
        /// </summary>
        private void WireFpsSliderKeyHandler()
        {
            if (FPSLimitSlider == null) return;
            FPSLimitSlider.AddHandler(Windows.UI.Xaml.UIElement.KeyDownEvent,
                new Windows.UI.Xaml.Input.KeyEventHandler(FPSLimitSlider_KeyDown), handledEventsToo: true);
        }

        private void TDPBoostToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Up → TDP slider (spine pos 2), Down → OSPowerMode, Left → PL2 slider.
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                TDPSlider?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                OSPowerModeComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.GamepadDPadLeft)
            {
                if (TDPBoostFPPTSliderCard?.IsEnabled == true)
                {
                    TDPBoostFPPTSliderCard.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                    e.Handled = true;
                }
            }
        }

        private void TDPBoostFPPTSliderCard_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Up/Down: XYFocusUp/Down=TDPBoostToggle in XAML handle it via TryMoveFocus.
            // Left/Right: change slider value naturally (Slider default).
        }

        private void OSPowerModeComboBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (OSPowerModeComboBox?.IsDropDownOpen == true) return;
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                TDPBoostToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                // Below OS Power Mode is the Overlay card (CPU card is now last on the tab).
                PerformanceOverlayComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        // CPU Boost on/off toggle (CPU card, last card on the Performance tab). Up → overlay combo
        // (card above), Down → the "More settings" expander directly beneath the toggle.
        private void CPUBoostToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                PerformanceOverlayComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                CpuSectionExpandButton?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
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
                OSPowerModeComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                // When the Overlay panel is expanded, drop into it (Columns). Otherwise go to the CPU card
                // below. Focus the VISIBLE CPU card header (CpuCardExpandButton) — the Boost Mode combo is
                // inside the collapsed CpuSectionContent, so focusing it failed and the old fallback sent
                // focus back UP to OS Power Mode (the "down goes up" bug).
                if (isOSDCustomizeExpanded && OSDColumnsComboBox != null)
                    OSDColumnsComboBox.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else
                    (CpuCardExpandButton ?? (Windows.UI.Xaml.Controls.Control)PerfSavedProfilesExpandButton)?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
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
