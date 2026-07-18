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

        private void SubscribeToSettingsChanges()
        {
            // Performance settings
            TDPSlider.ValueChanged += SettingChanged;
            CPUBoostToggle.Toggled += SettingChanged;
            CPUEPPSlider.ValueChanged += SettingChanged;
            MinCPUStateComboBox.SelectionChanged += SettingChanged;
            MaxCPUStateComboBox.SelectionChanged += SettingChanged;
            FPSLimitToggle.Toggled += FPSLimitToggle_Toggled;
            FPSLimitSlider.ValueChanged += FPSLimitSlider_ValueChanged;

            // Graphics settings (HDR and Resolution for profile feature)
            HDRToggle.Toggled += SettingChanged;
            ResolutionComboBox.SelectionChanged += SettingChanged;

            // Intel Display (IGCL) sliders — persist into the performance & display profile so
            // their values appear in the global / per-game cards. (Skipped during helper sync.)
            if (DisplaySaturationSlider != null) DisplaySaturationSlider.ValueChanged += SettingChanged;
            if (DisplayHueSlider != null) DisplayHueSlider.ValueChanged += SettingChanged;
            if (DisplayContrastSlider != null) DisplayContrastSlider.ValueChanged += SettingChanged;
            if (DisplayBrightnessSlider != null) DisplayBrightnessSlider.ValueChanged += SettingChanged;
            if (DisplayGammaSlider != null) DisplayGammaSlider.ValueChanged += SettingChanged;
            if (DisplaySharpnessSlider != null) DisplaySharpnessSlider.ValueChanged += SettingChanged;

            // Intel gaming (IGCL) combos — persist into the widget profile copy (helper is authoritative
            // for apply; the CpuIntComboProperty ignores this until the first helper sync).
            if (IntelLowLatencyComboBox != null) IntelLowLatencyComboBox.SelectionChanged += SettingChanged;
            if (IntelFrameSyncComboBox != null) IntelFrameSyncComboBox.SelectionChanged += SettingChanged;

            // AMD settings
            AMDFluidMotionFrameToggle.Toggled += SettingChanged;
            AMDRadeonSuperResolutionToggle.Toggled += AMDRadeonSuperResolutionToggle_Toggled;
            AMDRadeonSuperResolutionSharpnessSlider.ValueChanged += SettingChanged;
            AMDImageSharpeningToggle.Toggled += AMDImageSharpeningToggle_Toggled;
            AMDImageSharpeningSlider.ValueChanged += SettingChanged;
            AMDRadeonAntiLagToggle.Toggled += AMDRadeonAntiLagToggle_Toggled;
            AMDRadeonBoostToggle.Toggled += AMDRadeonBoostToggle_Toggled;
            AMDRadeonBoostResolutionSlider.ValueChanged += SettingChanged;
            AMDRadeonChillToggle.Toggled += AMDRadeonChillToggle_Toggled;
            AMDRadeonChillMinFPSSlider.ValueChanged += SettingChanged;
            AMDRadeonChillMaxFPSSlider.ValueChanged += SettingChanged;

            // Legion controller button mapping settings
            InitializeButtonMappingEvents("Y1");
            InitializeButtonMappingEvents("Y2");
            InitializeButtonMappingEvents("Y3");
            InitializeButtonMappingEvents("M1");
            InitializeButtonMappingEvents("M2");
            InitializeButtonMappingEvents("M3");
            InitializeButtonMappingEvents("Desktop");
            InitializeButtonMappingEvents("Page");
            PopulateDesktopButtonActionComboBox();
            InitializeLeftMsiDoubleClick();

            if (LegionNintendoLayoutToggle != null)
                LegionNintendoLayoutToggle.Toggled += LegionNintendoLayout_Toggled;
            if (LegionDesktopControlsToggle != null)
                LegionDesktopControlsToggle.Toggled += LegionDesktopControls_Toggled;
            if (LegionVibrationComboBox != null)
                LegionVibrationComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionVibrationModeComboBox != null)
                LegionVibrationModeComboBox.SelectionChanged += ControllerSettingChanged;
            // Stepless vibration intensity (MSI Claw rumble scaling, per-game profile)
            if (VibrationIntensitySlider != null)
                VibrationIntensitySlider.ValueChanged += ControllerSettingChanged;

            // Gyro settings (per-game profile)
            if (LegionGyroTargetComboBox != null)
                LegionGyroTargetComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroSensitivityXSlider != null)
                LegionGyroSensitivityXSlider.ValueChanged += ControllerSettingChanged;
            if (LegionGyroSensitivityYSlider != null)
                LegionGyroSensitivityYSlider.ValueChanged += ControllerSettingChanged;
            if (LegionGyroInvertXToggle != null)
                LegionGyroInvertXToggle.Toggled += ControllerSettingChanged;
            if (LegionGyroInvertYToggle != null)
                LegionGyroInvertYToggle.Toggled += ControllerSettingChanged;
            if (LegionGyroMappingTypeComboBox != null)
                LegionGyroMappingTypeComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroActivationModeComboBox != null)
                LegionGyroActivationModeComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroActivationButtonComboBox != null)
                LegionGyroActivationButtonComboBox.SelectionChanged += ControllerSettingChanged;

            // Advanced gyro settings (per-game profile)
            if (LegionGyroDeadzoneSlider != null)
                LegionGyroDeadzoneSlider.ValueChanged += ControllerSettingChanged;

            // Stick deadzones (per-game profile)
            if (LegionLeftStickDeadzoneSlider != null)
                LegionLeftStickDeadzoneSlider.ValueChanged += ControllerSettingChanged;
            if (LegionRightStickDeadzoneSlider != null)
                LegionRightStickDeadzoneSlider.ValueChanged += ControllerSettingChanged;

            // Trigger travel (per-game profile)
            if (LegionLeftTriggerStartSlider != null)
                LegionLeftTriggerStartSlider.ValueChanged += ControllerSettingChanged;
            if (LegionLeftTriggerEndSlider != null)
                LegionLeftTriggerEndSlider.ValueChanged += ControllerSettingChanged;
            if (LegionRightTriggerStartSlider != null)
                LegionRightTriggerStartSlider.ValueChanged += ControllerSettingChanged;
            if (LegionRightTriggerEndSlider != null)
                LegionRightTriggerEndSlider.ValueChanged += ControllerSettingChanged;
            if (LegionHairTriggersToggle != null)
                LegionHairTriggersToggle.Toggled += LegionHairTriggers_Toggled;

            // Joystick as mouse (per-game profile)
            if (LegionJoystickAsMouseComboBox != null)
                LegionJoystickAsMouseComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionJoystickMouseSensSlider != null)
                LegionJoystickMouseSensSlider.ValueChanged += ControllerSettingChanged;

            // Lighting settings (per-game profile)
            if (LegionPowerLightToggle != null)
                LegionPowerLightToggle.Toggled += ControllerSettingChanged;
            if (LegionLightModeComboBox != null)
                LegionLightModeComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionColorPicker != null)
                LegionColorPicker.ColorChanged += ControllerSettingChanged;
            if (LegionBrightnessSlider != null)
                LegionBrightnessSlider.ValueChanged += ControllerSettingChanged;
            if (LegionSpeedSlider != null)
                LegionSpeedSlider.ValueChanged += ControllerSettingChanged;

            // Gamepad button remapping (per-game profile)
            if (LegionGamepadButtonSelectorComboBox != null)
                LegionGamepadButtonSelectorComboBox.SelectionChanged += LegionGamepadButtonSelector_SelectionChanged;
            if (LegionGamepadTypeComboBox != null)
                LegionGamepadTypeComboBox.SelectionChanged += LegionGamepadMapping_Changed;
            if (LegionGamepadActionComboBox != null)
                LegionGamepadActionComboBox.SelectionChanged += LegionGamepadMapping_Changed;
            if (LegionGamepadMouseComboBox != null)
                LegionGamepadMouseComboBox.SelectionChanged += LegionGamepadMapping_Changed;
            // LegionGamepadKeyComboBox was replaced by LegionGamepadKeyPickerButton (grouped key
            // picker); its Click is wired in XAML to LegionGamepadKeyPicker_Click.
            if (LegionGamepadResetAllButton != null)
                LegionGamepadResetAllButton.Click += LegionGamepadResetAll_Click;

            if (ControllerEmulationImprovedInputToggle != null)
                ControllerEmulationImprovedInputToggle.Toggled += ControllerEmulationImprovedInputToggle_Toggled;

            foreach (string buttonName in LegionRemapButtonNames)
            {
                UpdateButtonGamepadComboControls(buttonName);
            }

            // Overlay the zone-grouped icon picker on every Xbox-button dropdown (the combos stay
            // in the tree as the state store). Dynamic combo-mode add-combos are attached in
            // EnsureButtonGamepadComboControls as they are created.
            WireGamepadPickers();
        }

        private void SettingChanged(object sender, object e)
        {
            // Update Sticky TDP target if TDP slider changed and Sticky TDP is enabled
            // But ONLY if the change is from the user, not from helper sync/updates
            if (sender == TDPSlider && StickyTDPToggle?.IsOn == true && !isApplyingHelperUpdate)
            {
                targetTDPLimit = TDPSlider.Value;
                Logger.Info($"Sticky TDP target updated to: {targetTDPLimit}W (user change)");
            }

            // Don't save during profile loading, switching, initial sync, when helper is updating values,
            // when any property is syncing from helper pipe, or when Default Game Profile is active
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync
                || WidgetSliderProperty.HelperSyncCount > 0 || defaultGameProfileEnabled?.Value == true)
            {
                Logger.Debug($"Skipping auto-save during profile operation (loading={isLoadingProfile}, switching={isSwitchingProfile}, helperUpdate={isApplyingHelperUpdate}, initialSync={isInitialSync}, defaultGameProfile={defaultGameProfileEnabled?.Value})");
                return;
            }

            // Auto-save to current profile
            SaveCurrentSettingsToProfile(currentProfileName);
        }

    }
}
