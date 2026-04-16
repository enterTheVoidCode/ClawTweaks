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
        /// Controls visibility and enabled state for the handheld-agnostic Controller Emulation card.
        /// </summary>
        private void SetControllerEmulationAvailability(bool available)
        {
            controllerEmulationSupported = available;

            if (ControllerEmulationCard != null)
            {
                ControllerEmulationCard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ControllerEmulationEnabledToggle != null)
            {
                ControllerEmulationEnabledToggle.IsEnabled = available;
            }

            UpdateControllerEmulationControlState();
            UpdateControllerEmulationStatusText();
            Logger.Info($"Controller emulation availability set to: {available}");
            UpdateControllerEmulationMouseSettingsVisibility();
            RefreshLegionEnhancedRemapUi();
            UpdateSystemControllerEmulationNavigation();

            if (available)
            {
                RequestControllerEmulationDriverStatus();
            }
        }

        private void UpdateControllerEmulationControlState()
        {
            bool enabled = controllerEmulationSupported &&
                           ControllerEmulationEnabledToggle != null &&
                           ControllerEmulationEnabledToggle.IsOn;

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
        }

        private void UpdateControllerEmulationStatusText()
        {
            if (ControllerEmulationStatusText != null)
            {
                bool enabled = controllerEmulationSupported &&
                               ControllerEmulationEnabledToggle != null &&
                               ControllerEmulationEnabledToggle.IsOn;

                if (!controllerEmulationSupported)
                {
                    ControllerEmulationStatusText.Text = "Controller emulation is not available on this handheld.";
                    ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
                }
                else if (!enabled)
                {
                    ControllerEmulationStatusText.Text = "Controller emulation is disabled.";
                    ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 170, 90));
                }
                else
                {
                    ControllerEmulationStatusText.Text = "Controller emulation is enabled.";
                    ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 200, 120));
                }
            }
        }

        private void ControllerEmulationEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateControllerEmulationControlState();
            UpdateControllerEmulationStatusText();
            UpdateControllerEmulationMouseSettingsVisibility();
            UpdateSystemControllerEmulationNavigation();
        }

        private void ControllerEmulationImprovedInputToggle_Toggled(object sender, RoutedEventArgs e)
        {
            RefreshLegionEnhancedRemapUi();
        }

        private async void ControllerEmulationImprovedInput_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Dispatcher != null)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    RefreshLegionEnhancedRemapUi();
                });
            }
            else
            {
                RefreshLegionEnhancedRemapUi();
            }
        }

        private void UpdateControllerEmulationMouseSettingsVisibility()
        {
            if (ControllerEmulationMouseSettingsPanel == null)
            {
                return;
            }

            bool available = ControllerEmulationCard != null &&
                             ControllerEmulationCard.Visibility == Visibility.Visible &&
                             ControllerEmulationEnabledToggle != null &&
                             ControllerEmulationEnabledToggle.IsOn &&
                             ControllerEmulationModeComboBox != null &&
                             ControllerEmulationModeComboBox.IsEnabled;

            bool isMouseMode = ControllerEmulationModeComboBox != null &&
                               ControllerEmulationModeComboBox.SelectedIndex == 0;

            if (ControllerEmulationMouseSettingsPanel != null)
            {
                ControllerEmulationMouseSettingsPanel.Visibility = (available && isMouseMode)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

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
