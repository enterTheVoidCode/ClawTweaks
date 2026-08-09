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
            // Sliders save on the COMMIT boundary, not per step - see GamingWidget.SliderCommit.cs.
            WireSliderProfileCommit(TDPSlider);
            CPUBoostToggle.Toggled += SettingChanged;
            WireSliderProfileCommit(CPUEPPSlider);
            MinCPUStateComboBox.SelectionChanged += SettingChanged;
            MaxCPUStateComboBox.SelectionChanged += SettingChanged;
            FPSLimitToggle.Toggled += FPSLimitToggle_Toggled;
            FPSLimitSlider.ValueChanged += FPSLimitSlider_ValueChanged;
            // ...and the same commit boundary for its profile save (the handler above only marks it).
            FPSLimitSlider.FocusDisengaged += SliderProfile_FocusDisengaged;
            FPSLimitSlider.PointerCaptureLost += SliderProfile_PointerCaptureLost;
            FPSLimitSlider.LostFocus += SliderProfile_LostFocus;

            // PL2/Overboost, same deal: TDPBoostFPPTSlider_ValueChanged marks it, these commit it.
            // Both instances — the card slider and the one in the settings panel — mirror each other's
            // value, so either can be the one the user was holding.
            foreach (var pl2 in new[] { TDPBoostFPPTSliderCard, TDPBoostFPPTSlider })
            {
                if (pl2 == null) continue;
                pl2.FocusDisengaged += SliderProfile_FocusDisengaged;
                pl2.PointerCaptureLost += SliderProfile_PointerCaptureLost;
                pl2.LostFocus += SliderProfile_LostFocus;
            }

            // Graphics settings (HDR and Resolution for profile feature)
            HDRToggle.Toggled += SettingChanged;
            ResolutionComboBox.SelectionChanged += SettingChanged;

            // Intel Display (IGCL) sliders — persist into the performance & display profile so
            // their values appear in the global / per-game cards. (Skipped during helper sync.)
            WireSliderProfileCommit(DisplaySaturationSlider);
            WireSliderProfileCommit(DisplayHueSlider);
            WireSliderProfileCommit(DisplayContrastSlider);
            WireSliderProfileCommit(DisplayBrightnessSlider);
            WireSliderProfileCommit(DisplayGammaSlider);
            WireSliderProfileCommit(DisplaySharpnessSlider);

            // Intel gaming (IGCL) combos — persist into the widget profile copy (helper is authoritative
            // for apply; the CpuIntComboProperty ignores this until the first helper sync).
            if (IntelLowLatencyComboBox != null) IntelLowLatencyComboBox.SelectionChanged += SettingChanged;
            if (IntelFrameGenerationComboBox != null) IntelFrameGenerationComboBox.SelectionChanged += SettingChanged;
            if (IntelVrrComboBox != null) IntelVrrComboBox.SelectionChanged += SettingChanged;
            if (IntelVrrModeComboBox != null) IntelVrrModeComboBox.SelectionChanged += SettingChanged;
            if (IntelScalingModeComboBox != null) IntelScalingModeComboBox.SelectionChanged += SettingChanged;
            if (IntelScalingMethodComboBox != null) IntelScalingMethodComboBox.SelectionChanged += SettingChanged;
            if (IntelFrameSyncComboBox != null) IntelFrameSyncComboBox.SelectionChanged += SettingChanged;

            // AMD settings
            AMDFluidMotionFrameToggle.Toggled += SettingChanged;
            AMDRadeonSuperResolutionToggle.Toggled += AMDRadeonSuperResolutionToggle_Toggled;
            WireSliderProfileCommit(AMDRadeonSuperResolutionSharpnessSlider);
            AMDImageSharpeningToggle.Toggled += AMDImageSharpeningToggle_Toggled;
            WireSliderProfileCommit(AMDImageSharpeningSlider);
            AMDRadeonAntiLagToggle.Toggled += AMDRadeonAntiLagToggle_Toggled;
            AMDRadeonBoostToggle.Toggled += AMDRadeonBoostToggle_Toggled;
            WireSliderProfileCommit(AMDRadeonBoostResolutionSlider);
            AMDRadeonChillToggle.Toggled += AMDRadeonChillToggle_Toggled;
            WireSliderProfileCommit(AMDRadeonChillMinFPSSlider);
            WireSliderProfileCommit(AMDRadeonChillMaxFPSSlider);

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
            if (LegionGyroAntiDeadzoneSlider != null)
                LegionGyroAntiDeadzoneSlider.ValueChanged += ControllerSettingChanged;
            if (LegionGyroBoostButtonComboBox != null)
                LegionGyroBoostButtonComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroBoostFactorSlider != null)
                LegionGyroBoostFactorSlider.ValueChanged += ControllerSettingChanged;
            // Smoothing is stored per engine mode (LocalSettings), not in the controller profile — its own
            // handler persists + relabels; the legionGyroSmoothing property pushes the value to the helper.
            if (LegionGyroSmoothingSlider != null)
                LegionGyroSmoothingSlider.ValueChanged += LegionGyroSmoothingSlider_ValueChanged;

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
            // Don't save during profile loading, switching, initial sync, when helper is updating values,
            // when any property is syncing from helper pipe, or when Default Game Profile is active
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync
                || WidgetSliderProperty.HelperSyncCount > 0)
            {
                Logger.Debug($"Skipping auto-save during profile operation (loading={isLoadingProfile}, switching={isSwitchingProfile}, helperUpdate={isApplyingHelperUpdate}, initialSync={isInitialSync})");
                return;
            }

            // Auto-save to current profile
            SaveWidgetUiStateToProfile(currentProfileName);
        }

    }
}
