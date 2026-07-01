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
        /// Sets GPD tab visibility based on device detection.
        /// </summary>
        private void SetGPDTabVisibility(bool visible)
        {
            if (GPDNavItem != null)
            {
                GPDNavItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"GPD tab visibility set to: {visible}");
            }

            // Update connection status text
            if (GPDConnectionStatusText != null)
            {
                GPDConnectionStatusText.Text = visible ? "Connected" : "Detecting...";
                GPDConnectionStatusText.Foreground = new SolidColorBrush(visible ?
                    Windows.UI.Color.FromArgb(255, 76, 175, 80) :  // Green
                    Windows.UI.Color.FromArgb(255, 136, 136, 136)); // Gray
            }

            UpdateSystemControllerEmulationNavigation();
        }

        /// <summary>
        /// Sets the GPD device name text from the helper.
        /// </summary>
        private void SetGPDDeviceName(string name)
        {
            if (GPDDeviceNameText != null && !string.IsNullOrEmpty(name))
            {
                GPDDeviceNameText.Text = name;
                Logger.Info($"GPD device name set to: {name}");
            }
        }

        /// <summary>
        /// Sets visibility of fan control section based on device capability.
        /// Fan control uses EC commands, independent of HID controller connection.
        /// </summary>
        private void SetGPDFanControlVisibility(bool supported)
        {
            if (GPDFanControlSection != null)
            {
                GPDFanControlSection.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"GPD fan control section visibility set to: {supported}");
            }

            // Restore fan curve enabled state from LocalSettings
            if (supported)
            {
                try
                {
                    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    if (settings.Values.TryGetValue("GPDFanCurveEnabled", out object saved) && saved is bool enabled && enabled)
                    {
                        if (GPDFanCurveToggle != null)
                        {
                            GPDFanCurveToggle.Toggled -= GPDFanCurveToggle_Toggled;
                            GPDFanCurveToggle.IsOn = true;
                            GPDFanCurveToggle.Toggled += GPDFanCurveToggle_Toggled;
                        }
                        if (GPDManualFanContent != null)
                            GPDManualFanContent.Visibility = Visibility.Collapsed;
                        if (GPDFanCurveContent != null)
                            GPDFanCurveContent.Visibility = Visibility.Visible;

                        // Send enabled state to helper
                        gpdFanCurveEnabled?.SetEnabled(true);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Sets visibility of button remapping section based on HID controller connection.
        /// Button remapping requires HID connection to the Win 5 controller.
        /// </summary>
        private void SetGPDButtonRemapVisibility(bool connected)
        {
            if (GPDButtonRemapSection != null)
            {
                GPDButtonRemapSection.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"GPD button remap section visibility set to: {connected}");
            }

            if (GPDApplyMappingsButton != null)
            {
                GPDApplyMappingsButton.IsEnabled = connected;
            }

            UpdateSystemControllerEmulationNavigation();
        }

        /// <summary>
        /// Controls visibility of the Controller Emulation card based on hardware support
        /// AND game state. The card is hidden while a game is running — changing the virtual
        /// controller during gameplay is not supported and wastes screen space.
        /// </summary>
        /// <summary>
        /// Single source of truth for ControllerEmulationEnabledToggle.IsEnabled.
        /// The toggle is interactive only when ALL prerequisites are met:
        ///   1. Hardware / helper reports emulation as supported.
        ///   2. Steam Xbox driver not active.
        ///   3. MSI Center M not intercepting the controller.
        ///   4. Onboarding complete (all required tools installed).
        /// Exception to (4): if emulation is already running (toggle ON, restored at startup),
        /// the user can always turn it off — they should never be stuck in an active-emulation state.
        /// </summary>
        internal void UpdateControllerEmulationToggleEnabled()
        {
            if (ControllerEmulationEnabledToggle == null) return;

            if (!controllerEmulationSupported
                || _steamXboxDriverDetected
                || msiCenterActive?.Value == true)
            {
                ControllerEmulationEnabledToggle.IsEnabled = false;
                UpdateControllerEmulationStatusText();
                return;
            }

            bool onboardingOk = OnbAllToolsInstalled || GetPersistedOnboardingComplete();
            bool alreadyOn = ControllerEmulationEnabledToggle.IsOn;
            bool allowed = onboardingOk || alreadyOn;
            ControllerEmulationEnabledToggle.IsEnabled = allowed;
            UpdateControllerEmulationStatusText();
            Logger.Debug($"[VCtrl] toggle enabled={allowed} (onboarding={onboardingOk}, alreadyOn={alreadyOn}, steam={_steamXboxDriverDetected}, msi={msiCenterActive?.Value})");
        }

        private void SetControllerEmulationAvailability(bool available)
        {
            controllerEmulationSupported = available;
            // Card visibility: only show when supported AND no game running
            UpdateControllerEmulationCardVisibility();

            // For non-Legion devices (MSI Claw): show the Controller tab when emulation is available.
            // Legion devices already have the tab shown via SetLegionTabVisibility(true).
            if (LegionNavItem != null && (legionGoDetected == null || legionGoDetected.Value != true))
            {
                LegionNavItem.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Controller tab visibility set to: {available} (non-Legion, controllerEmulationAvailable)");
            }

            UpdateControllerEmulationToggleEnabled();

            UpdateControllerEmulationControlState();
            Logger.Info($"Controller emulation availability set to: {available}");
            UpdateControllerEmulationMouseSettingsVisibility();
            RefreshLegionEnhancedRemapUi();
            UpdateSystemControllerEmulationNavigation();

            if (available)
            {
                RequestControllerEmulationDriverStatus();
            }

            // Quick tab tile visibility is gated on availability — refresh the grid so
            // the Controller tile appears/disappears when the helper reports support.
            RefreshQuickSettingsForControllerEmulation();
        }

        /// <summary>
        /// Updates Controller Emulation card visibility: visible only when the device
        /// supports emulation AND no game is currently running.
        /// </summary>
        internal void UpdateControllerEmulationCardVisibility()
        {
            if (ControllerEmulationCard == null) return;
            bool gameRunning = HasValidGame(currentGameName);
            bool show = controllerEmulationSupported && !gameRunning;
            ControllerEmulationCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            // The master-toggle card and the Mouse Settings card follow the same gate — both must be
            // completely hidden in-game (little screen space) and while emulation is unsupported.
            if (VirtualControllerMasterCard != null)
                VirtualControllerMasterCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (MouseSettingsCard != null)
                MouseSettingsCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Logger.Debug($"[VCtrl] Card visibility: supported={controllerEmulationSupported} gameRunning={gameRunning} → {(show ? "Visible" : "Collapsed")}");

            // Drive the expand-chevron discovery glow: pulses only while the card is
            // visible (no game) and still collapsed; stays off in-game / after discovery.
            UpdateControllerEmulationExpandHint();
        }

        private void RefreshQuickSettingsForControllerEmulation()
        {
            if (!quickSettingsInitialized) return;
            try
            {
                RebuildQuickSettingsTiles();
                BuildSortableGrid();
                UpdateQuickSettingsTileStates();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error refreshing Quick Settings for Controller Emulation: {ex.Message}");
            }
        }

        /// <summary>
        /// True when the virtual controller is actually providing input: the master toggle is ON AND
        /// the running game is not using the HW-controller exception (which app-side stops the virtual
        /// pad for the native HW controller while leaving the master toggle ON). Every controller
        /// feature — remaps, gyro, vibration, deadzones, front buttons — is read-only when this is false.
        /// </summary>
        private bool IsVirtualControllerActive()
        {
            bool hwExceptionActive = HasValidGame(currentGameName) && HwControllerExceptionToggle?.IsOn == true;
            return controllerEmulationSupported
                && ControllerEmulationEnabledToggle?.IsOn == true
                && !hwExceptionActive;
        }

        /// <summary>
        /// Force-collapses an expandable controller section and disables its expand chevron when the
        /// virtual controller is inactive, so the settings can't be opened or edited. Re-enables the
        /// chevron (without auto-opening) when active.
        /// </summary>
        private void GateControllerSection(bool active,
            Windows.UI.Xaml.Controls.Primitives.ToggleButton toggle,
            Windows.UI.Xaml.UIElement content,
            Windows.UI.Xaml.Controls.FontIcon icon,
            ref bool expandedFlag)
        {
            if (toggle != null) toggle.IsEnabled = active;
            if (!active)
            {
                expandedFlag = false;
                if (content != null) content.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                if (icon != null) icon.Glyph = "\uE70D"; // collapsed chevron
                if (toggle != null) toggle.IsChecked = false;
            }
        }

        private void UpdateControllerEmulationControlState()
        {
            bool enabled = IsVirtualControllerActive();

            // Expandable controller sections (Virtual Controller body incl. front buttons, Gyro,
            // Button Remapping, Vibration & Deadzone): collapse + lock when the virtual controller is
            // inactive — master off, or the HW-controller exception is active in-game — so every
            // controller setting is read-only and the menus can't be expanded.
            GateControllerSection(enabled, ControllerEmulationExpandButton, ControllerEmulationContent, ControllerEmulationExpandIcon, ref isControllerEmulationExpanded);
            GateControllerSection(enabled, GyroSettingsExpandToggle, GyroSettingsContent, GyroSettingsExpandIcon, ref isGyroSettingsExpanded);
            GateControllerSection(enabled, ButtonRemappingExpandToggle, ButtonRemappingContent, ButtonRemappingExpandIcon, ref isButtonRemappingExpanded);
            GateControllerSection(enabled, TouchpadVibrationExpandToggle, TouchpadVibrationContent, TouchpadVibrationExpandIcon, ref isTouchpadVibrationExpanded);
            // "Vibration & Deadzone" card (ControllerFeedback*) is the one actually shown on the MSI Claw
            // (TouchpadVibration* is hidden on devices without a HID touchpad). Gate it the same way.
            GateControllerSection(enabled, ControllerFeedbackExpandToggle, ControllerFeedbackContent, ControllerFeedbackExpandIcon, ref _controllerFeedbackExpanded);
            // Mouse Settings card (split out of the old Virtual Controller card) — gate it the same way.
            GateControllerSection(enabled, MouseSettingsExpandToggle, MouseSettingsContent, MouseSettingsExpandIcon, ref _mouseSettingsExpanded);

            if (ControllerEmulationHideStockControllerToggle != null)
            {
                ControllerEmulationHideStockControllerToggle.IsEnabled = enabled;
            }

            if (ControllerEmulationImprovedInputToggle != null)
            {
                ControllerEmulationImprovedInputToggle.IsEnabled = enabled;
            }

            if (ControllerEmulationHideTargetComboBox != null)
            {
                ControllerEmulationHideTargetComboBox.IsEnabled = enabled;
            }

            if (ControllerEmulationGyroSourceComboBox != null)
            {
                ControllerEmulationGyroSourceComboBox.IsEnabled = enabled;
            }

            if (CalibrateGyroGrid != null)
            {
                bool isControllerSource = ControllerEmulationGyroSourceComboBox != null &&
                                          ControllerEmulationGyroSourceComboBox.SelectedIndex > 0;
                CalibrateGyroGrid.Visibility = isControllerSource
                    ? Windows.UI.Xaml.Visibility.Visible
                    : Windows.UI.Xaml.Visibility.Collapsed;
                if (CalibrateGyroButton != null)
                {
                    CalibrateGyroButton.IsEnabled = enabled && isControllerSource && App.IsConnected;
                }
            }

            if (ControllerEmulationModeComboBox != null)
            {
                ControllerEmulationModeComboBox.IsEnabled = enabled;
            }

            bool isVirtualGamepadMode = ControllerEmulationModeComboBox != null &&
                                        ControllerEmulationModeComboBox.SelectedIndex > 0;
            if (ControllerEmulationRumbleProfileComboBox != null)
            {
                ControllerEmulationRumbleProfileComboBox.IsEnabled = enabled && isVirtualGamepadMode;
            }

            bool isAlwaysOnActivation = ControllerEmulationGyroActivationModeComboBox == null ||
                                        ControllerEmulationGyroActivationModeComboBox.SelectedIndex <= 0;
            if (ControllerEmulationGyroActivationModeComboBox != null)
            {
                ControllerEmulationGyroActivationModeComboBox.IsEnabled = enabled;
            }

            if (ControllerEmulationGyroActivationButtonComboBox != null)
            {
                ControllerEmulationGyroActivationButtonComboBox.IsEnabled = enabled && !isAlwaysOnActivation;
            }

            bool isMouseMode = ControllerEmulationModeComboBox != null &&
                               ControllerEmulationModeComboBox.SelectedIndex == 0;
            bool isDs4MotionMode = ControllerEmulationModeComboBox != null &&
                                   ControllerEmulationModeComboBox.SelectedIndex == 2;
            bool isDs4Mode = ControllerEmulationModeComboBox != null &&
                             (ControllerEmulationModeComboBox.SelectedIndex == 2 || ControllerEmulationModeComboBox.SelectedIndex == 3);
            bool isStickMode = ControllerEmulationModeComboBox != null &&
                               (ControllerEmulationModeComboBox.SelectedIndex == 1 || ControllerEmulationModeComboBox.SelectedIndex == 3);

            bool mouseControlsEnabled = enabled && isMouseMode;
            if (ControllerEmulationMouseSensitivitySlider != null)
                ControllerEmulationMouseSensitivitySlider.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseThresholdSlider != null)
                ControllerEmulationMouseThresholdSlider.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseLeftClickButtonComboBox != null)
                ControllerEmulationMouseLeftClickButtonComboBox.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseRightClickButtonComboBox != null)
                ControllerEmulationMouseRightClickButtonComboBox.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseCursorStickComboBox != null)
                ControllerEmulationMouseCursorStickComboBox.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseScrollStickComboBox != null)
                ControllerEmulationMouseScrollStickComboBox.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseAxisComboBox != null)
                ControllerEmulationMouseAxisComboBox.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseInvertXToggle != null)
                ControllerEmulationMouseInvertXToggle.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseInvertYToggle != null)
                ControllerEmulationMouseInvertYToggle.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseGainXSlider != null)
                ControllerEmulationMouseGainXSlider.IsEnabled = mouseControlsEnabled;
            if (ControllerEmulationMouseGainYSlider != null)
                ControllerEmulationMouseGainYSlider.IsEnabled = mouseControlsEnabled;

            // Features group - enable/disable controls based on mode
            if (ControllerEmulationPs4TouchpadToggle != null)
                ControllerEmulationPs4TouchpadToggle.IsEnabled = enabled && isDs4Mode;
            if (ControllerEmulationLedForwardingToggle != null)
                ControllerEmulationLedForwardingToggle.IsEnabled = enabled && isDs4Mode;
            if (ControllerEmulationRumbleProfileComboBox != null)
                ControllerEmulationRumbleProfileComboBox.IsEnabled = enabled && isVirtualGamepadMode;
            if (ControllerEmulationVirtualAbxyLayoutComboBox != null)
                ControllerEmulationVirtualAbxyLayoutComboBox.IsEnabled = enabled && isVirtualGamepadMode;
            if (ControllerEmulationStickOnlyJoystickToggle != null)
                ControllerEmulationStickOnlyJoystickToggle.IsEnabled = enabled && isStickMode;

            // DS4 Orientation - in Gyro Activation group, only for DS4 modes
            if (ControllerEmulationDs4OrientationComboBox != null)
                ControllerEmulationDs4OrientationComboBox.IsEnabled = enabled && isDs4Mode;
            if (ControllerEmulationDs4OrientationRow != null)
                ControllerEmulationDs4OrientationRow.Visibility = isDs4Mode ? Visibility.Visible : Visibility.Collapsed;

            // Touchpad/LED rows - show only for DS4 modes
            if (ControllerEmulationPs4TouchpadRow != null)
                ControllerEmulationPs4TouchpadRow.Visibility = isDs4Mode ? Visibility.Visible : Visibility.Collapsed;
            if (ControllerEmulationLedForwardingRow != null)
                ControllerEmulationLedForwardingRow.Visibility = isDs4Mode ? Visibility.Visible : Visibility.Collapsed;

            // Joystick Output group - visible only for stick modes
            if (JoystickOutputGroupHeader != null)
                JoystickOutputGroupHeader.Visibility = isStickMode ? Visibility.Visible : Visibility.Collapsed;
            if (!isStickMode && JoystickOutputContent != null)
                JoystickOutputContent.Visibility = Visibility.Collapsed;

            // Enable/disable all stick v2 controls
            bool stickControlsEnabled = enabled && isStickMode;
            if (ControllerEmulationStickSelectComboBox != null)
                ControllerEmulationStickSelectComboBox.IsEnabled = stickControlsEnabled;
            if (StickConversionComboBox != null)
                StickConversionComboBox.IsEnabled = stickControlsEnabled;
            if (StickOrientationV2ComboBox != null)
                StickOrientationV2ComboBox.IsEnabled = stickControlsEnabled;
            if (ControllerEmulationStickInvertXToggle != null)
                ControllerEmulationStickInvertXToggle.IsEnabled = stickControlsEnabled;
            if (ControllerEmulationStickInvertYToggle != null)
                ControllerEmulationStickInvertYToggle.IsEnabled = stickControlsEnabled;
            if (StickSensitivityV2Slider != null)
                StickSensitivityV2Slider.IsEnabled = stickControlsEnabled;
            if (StickMinGyroSpeedSlider != null)
                StickMinGyroSpeedSlider.IsEnabled = stickControlsEnabled;
            if (StickMaxGyroSpeedSlider != null)
                StickMaxGyroSpeedSlider.IsEnabled = stickControlsEnabled;
            if (StickMinOutputSlider != null)
                StickMinOutputSlider.IsEnabled = stickControlsEnabled;
            if (StickMaxOutputSlider != null)
                StickMaxOutputSlider.IsEnabled = stickControlsEnabled;
            if (StickPowerCurveSlider != null)
                StickPowerCurveSlider.IsEnabled = stickControlsEnabled;
            if (StickDeadzoneSlider != null)
                StickDeadzoneSlider.IsEnabled = stickControlsEnabled;
            if (StickPrecisionSpeedSlider != null)
                StickPrecisionSpeedSlider.IsEnabled = stickControlsEnabled;
            if (StickOutputMixSlider != null)
                StickOutputMixSlider.IsEnabled = stickControlsEnabled;

            // Keep Legion remap advanced controls aligned with current emulation toggles
            // even when startup/property sync order suppresses Toggle events.
            RefreshLegionEnhancedRemapUi();
        }

        private void RefreshLegionEnhancedRemapUi()
        {
            foreach (string buttonName in LegionRemapButtonNames)
            {
                UpdateButtonGamepadComboControls(buttonName);
            }

            // Also refresh the generic "Re-Map Specific Buttons" swap summary (the deletable tags
            // above the 3-dropdown swap row). ApplyControllerProfile updates it on profile load,
            // but if the Controls tab UI wasn't realized yet at that moment (e.g. a game started
            // while the user was on another tab), the summary stayed empty. Refresh it here when
            // the tab becomes active so the active profile's swaps are visible (and deletable).
            UpdateGamepadMappingSummary();
        }

        private void UpdateControllerEmulationStatusText()
        {
            if (ControllerEmulationStatusText == null) return;

            bool isOn = ControllerEmulationEnabledToggle?.IsOn == true;

            if (!controllerEmulationSupported)
            {
                ControllerEmulationStatusText.Text = "Controller emulation is not available on this handheld.";
                ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
            }
            else if (_steamXboxDriverDetected)
            {
                ControllerEmulationStatusText.Text = "Blocked by Steam Xbox driver — disable it in Steam Settings → Controller.";
                ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 80, 80));
            }
            else if (msiCenterActive?.Value == true)
            {
                ControllerEmulationStatusText.Text = "Blocked by MSI Center M — turn it off first.";
                ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 80, 80));
            }
            else if (!OnbAllToolsInstalled && !GetPersistedOnboardingComplete() && !isOn)
            {
                ControllerEmulationStatusText.Text = "Complete the setup (Setup tab) before enabling controller emulation.";
                ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 170, 90));
            }
            else if (!isOn)
            {
                ControllerEmulationStatusText.Text = "Enable controller emulation to use button remapping, gyro, Mouse Mode and more.";
                ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
            }
            else
            {
                ControllerEmulationStatusText.Text = "Turn the virtual controller and mouse runtime on or off.";
                ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
            }
        }

        private void ControllerEmulationEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateControllerEmulationControlState();
            // HW Controller Exception card depends on emulation being on (and a game running).
            UpdateHwControllerExceptionVisibility();
            // Re-evaluate IsEnabled: if user turned emulation off, "alreadyOn" gate no longer applies.
            UpdateControllerEmulationToggleEnabled();
            UpdateControllerEmulationMouseSettingsVisibility();
            UpdateSystemControllerEmulationNavigation();
            // Per-game controller profiles only work with emulation running — keep the
            // "create per-game profile" toggle's enabled state in sync if emulation is
            // flipped while already in-game.
            RefreshPerGameControllerToggleEnabled();
            // Keep the Quick tab Controller tile in sync with the System-tab toggle
            // and any helper-driven changes (e.g. ControllerEmulationAvailable arrives
            // late and needs to flip the tile from "N/A" to its actual state).
            UpdateQuickSettingsTileStates();
        }

        private void ControllerEmulationImprovedInputToggle_Toggled(object sender, RoutedEventArgs e)
        {
            RefreshLegionEnhancedRemapUi();
        }

        private async void ControllerEmulationImprovedInput_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // async void: any exception here escapes to the UWP runtime as 0xc000027b
            // and terminates the process. Wrap both the await and the lambda body.
            try
            {
                if (Dispatcher != null)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { RefreshLegionEnhancedRemapUi(); }
                        catch (Exception ex) { Logger.Warn($"RefreshLegionEnhancedRemapUi threw: {ex.GetType().Name}: {ex.Message}"); }
                    });
                }
                else
                {
                    RefreshLegionEnhancedRemapUi();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ControllerEmulationImprovedInput_PropertyChanged outer: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void UpdateControllerEmulationMouseSettingsVisibility()
        {
            if (ControllerEmulationMouseSettingsPanel == null)
            {
                return;
            }

            // Mouse settings now live in their own "Mouse Settings" card whose visibility is owned by
            // the MouseSettingsContent expander + the in-game/inactive gate — no longer tied to the
            // (hidden legacy) gyro-simulation "Mouse" mode. The panel itself stays visible inside the
            // expander; we only keep the XYFocus wiring below valid.
            bool isMouseMode = ControllerEmulationModeComboBox != null &&
                               ControllerEmulationModeComboBox.SelectedIndex == 0;

            DependencyObject firstModeDetailControl = AutoHibernateToggle;
            if (isMouseMode && ControllerEmulationMouseSensitivitySlider != null)
            {
                firstModeDetailControl = ControllerEmulationMouseSensitivitySlider;
            }
            else if (GyroActivationExpandToggle != null)
            {
                firstModeDetailControl = GyroActivationExpandToggle;
            }

            if (ControllerEmulationModeComboBox != null)
            {
                ControllerEmulationModeComboBox.XYFocusDown = firstModeDetailControl;
                if (firstModeDetailControl is Control firstControl &&
                    !ReferenceEquals(firstModeDetailControl, AutoHibernateToggle))
                {
                    firstControl.XYFocusUp = ControllerEmulationModeComboBox;
                }
            }

            if (AutoHibernateToggle != null)
            {
                if (isMouseMode && ControllerEmulationMouseGainYSlider != null)
                {
                    AutoHibernateToggle.XYFocusUp = ControllerEmulationMouseGainYSlider;
                }
                else if (JoystickOutputExpandToggle != null)
                {
                    AutoHibernateToggle.XYFocusUp = JoystickOutputExpandToggle;
                }
                else if (FeaturesExpandToggle != null)
                {
                    AutoHibernateToggle.XYFocusUp = FeaturesExpandToggle;
                }
                else if (GyroActivationExpandToggle != null)
                {
                    AutoHibernateToggle.XYFocusUp = GyroActivationExpandToggle;
                }
                else
                {
                    AutoHibernateToggle.XYFocusUp = ControllerEmulationModeComboBox;
                }
            }
        }

        private void ControllerEmulationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateControllerEmulationControlState();
            UpdateControllerEmulationMouseSettingsVisibility();
            UpdateSystemControllerEmulationNavigation();
        }

        private void ControllerEmulationGyroSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateControllerEmulationControlState();
        }

        private void ControllerEmulationGyroActivationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateControllerEmulationControlState();
            UpdateControllerEmulationMouseSettingsVisibility();
            UpdateSystemControllerEmulationNavigation();
        }

        /// <summary>
        /// Keeps System tab D-pad/keyboard navigation valid when Controller Emulation card visibility/enabled state changes.
        /// </summary>
        private void UpdateSystemControllerEmulationNavigation()
        {
            if (HotkeysExpandButton == null || AutoHibernateToggle == null)
            {
                return;
            }

            bool emulationCardVisible =
                ControllerEmulationCard != null &&
                ControllerEmulationCard.Visibility == Visibility.Visible &&
                ControllerEmulationExpandButton != null;

            bool emulationCardExpanded =
                emulationCardVisible &&
                isControllerEmulationExpanded &&
                ControllerEmulationContent != null &&
                ControllerEmulationContent.Visibility == Visibility.Visible;

            bool emulationCardActive =
                emulationCardExpanded &&
                ControllerEmulationEnabledToggle != null &&
                ControllerEmulationEnabledToggle.IsEnabled;

            bool emulationModeControlsActive =
                emulationCardExpanded &&
                ControllerEmulationGyroSourceComboBox != null &&
                ControllerEmulationGyroSourceComboBox.IsEnabled &&
                ControllerEmulationModeComboBox != null &&
                ControllerEmulationModeComboBox.IsEnabled;
            bool isMouseMode = ControllerEmulationModeComboBox != null &&
                               ControllerEmulationModeComboBox.SelectedIndex == 0;
            bool isStickMode = ControllerEmulationModeComboBox != null &&
                               (ControllerEmulationModeComboBox.SelectedIndex == 1 || ControllerEmulationModeComboBox.SelectedIndex == 3);

            if (emulationCardVisible)
            {
                HotkeysExpandButton.XYFocusDown = ControllerEmulationExpandButton;
                ControllerEmulationExpandButton.XYFocusUp = HotkeysExpandButton;

                if (!emulationCardExpanded)
                {
                    ControllerEmulationExpandButton.XYFocusDown = AutoHibernateToggle;
                    AutoHibernateToggle.XYFocusUp = ControllerEmulationExpandButton;
                    return;
                }

                if (ControllerEmulationEnabledToggle != null)
                {
                    ControllerEmulationExpandButton.XYFocusDown = ControllerEmulationEnabledToggle;
                    ControllerEmulationEnabledToggle.XYFocusUp = ControllerEmulationExpandButton;
                }
                else
                {
                    ControllerEmulationExpandButton.XYFocusDown = AutoHibernateToggle;
                    AutoHibernateToggle.XYFocusUp = ControllerEmulationExpandButton;
                    return;
                }

                if (!emulationCardActive)
                {
                    AutoHibernateToggle.XYFocusUp = ControllerEmulationEnabledToggle;
                    return;
                }

                if (!emulationModeControlsActive)
                {
                    AutoHibernateToggle.XYFocusUp = ControllerEmulationEnabledToggle;
                    return;
                }

                if (isMouseMode && ControllerEmulationMouseGainYSlider != null && ControllerEmulationMouseGainYSlider.IsEnabled)
                {
                    AutoHibernateToggle.XYFocusUp = ControllerEmulationMouseGainYSlider;
                }
                else if (isStickMode && JoystickOutputExpandToggle != null)
                {
                    AutoHibernateToggle.XYFocusUp = JoystickOutputExpandToggle;
                }
                else if (FeaturesExpandToggle != null)
                {
                    AutoHibernateToggle.XYFocusUp = FeaturesExpandToggle;
                }
                else if (GyroActivationExpandToggle != null)
                {
                    AutoHibernateToggle.XYFocusUp = GyroActivationExpandToggle;
                }
                else
                {
                    AutoHibernateToggle.XYFocusUp = ControllerEmulationModeComboBox;
                }
            }
            else
            {
                HotkeysExpandButton.XYFocusDown = AutoHibernateToggle;
                AutoHibernateToggle.XYFocusUp = HotkeysExpandButton;
            }
        }

        /// <summary>
        /// Updates the fan RPM display.
        /// </summary>
        private void UpdateGPDFanRPM(int rpm)
        {
            if (GPDFanRPMText != null)
            {
                GPDFanRPMText.Text = rpm > 0 ? $"{rpm} RPM" : "-- RPM";
            }
        }

        /// <summary>
        /// Updates the fan mode UI.
        /// </summary>
        private void UpdateGPDFanMode(int mode)
        {
            bool isManual = mode == 1;

            if (GPDFanModeToggle != null)
            {
                // Temporarily remove handler to avoid triggering property update
                GPDFanModeToggle.Toggled -= GPDFanModeToggle_Toggled;
                GPDFanModeToggle.IsOn = isManual;
                GPDFanModeToggle.Toggled += GPDFanModeToggle_Toggled;
            }

            if (GPDFanModeText != null)
            {
                GPDFanModeText.Text = isManual ? "Manual" : "Auto";
            }

            if (GPDFanSpeedSection != null)
            {
                GPDFanSpeedSection.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
            }
        }

    }
}
