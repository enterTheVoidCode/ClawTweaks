using NLog;
using Shared.Constants;
using Shared.Data;
using Shared.IPC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Input.Preview.Injection;
using XboxGamingBarHelper.AMD;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.LosslessScaling;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Systems;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.DefaultGameProfiles;
using XboxGamingBarHelper.Labs;
using Shared.Enums;

namespace XboxGamingBarHelper
{
    internal partial class Program
    {

        private static void LegionControllerSetting_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug("Skipping LegionControllerSetting_PropertyChanged - already applying profile");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug("Skipping LegionControllerSetting_PropertyChanged - in profile switch cooldown");
                return;
            }

            // Per-setting routing: buttons/gamepad-mapping/nintendo/vibration/lighting consult the
            // widget's save-flags. Gyro, stick deadzones, triggers, joystick-as-mouse, and
            // LegionControllerProfileEnabled stay per-game (CurrentProfile) — no flag exists yet.
            var profileName = profileManager.CurrentProfile.GameId.Name;
            bool saveButtonsToProfile = ProfileSaveFlagsState.ButtonMappings;

            // Button mappings
            if (sender == legionManager?.LegionButtonY1)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionButtonY1",
                    cur => cur.LegionButtonY1 = legionManager.LegionButtonY1.Value,
                    glo => glo.LegionButtonY1 = legionManager.LegionButtonY1.Value);
            }
            else if (sender == legionManager?.LegionButtonY2)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionButtonY2",
                    cur => cur.LegionButtonY2 = legionManager.LegionButtonY2.Value,
                    glo => glo.LegionButtonY2 = legionManager.LegionButtonY2.Value);
            }
            else if (sender == legionManager?.LegionButtonY3)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionButtonY3",
                    cur => cur.LegionButtonY3 = legionManager.LegionButtonY3.Value,
                    glo => glo.LegionButtonY3 = legionManager.LegionButtonY3.Value);
            }
            else if (sender == legionManager?.LegionButtonM1)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionButtonM1",
                    cur => cur.LegionButtonM1 = legionManager.LegionButtonM1.Value,
                    glo => glo.LegionButtonM1 = legionManager.LegionButtonM1.Value);
            }
            else if (sender == legionManager?.LegionButtonM2)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionButtonM2",
                    cur => cur.LegionButtonM2 = legionManager.LegionButtonM2.Value,
                    glo => glo.LegionButtonM2 = legionManager.LegionButtonM2.Value);
            }
            else if (sender == legionManager?.LegionButtonM3)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionButtonM3",
                    cur => cur.LegionButtonM3 = legionManager.LegionButtonM3.Value,
                    glo => glo.LegionButtonM3 = legionManager.LegionButtonM3.Value);
            }
            else if (sender == legionManager?.LegionButtonDesktop)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionButtonDesktop",
                    cur => cur.LegionButtonDesktop = legionManager.LegionButtonDesktop.Value,
                    glo => glo.LegionButtonDesktop = legionManager.LegionButtonDesktop.Value);
            }
            else if (sender == legionManager?.LegionButtonPage)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionButtonPage",
                    cur => cur.LegionButtonPage = legionManager.LegionButtonPage.Value,
                    glo => glo.LegionButtonPage = legionManager.LegionButtonPage.Value);
            }
            // Gyro settings
            else if (sender == legionManager?.LegionGyroActivationButton)
            {
                Logger.Info($"Saving LegionGyroButton to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroButton = legionManager.LegionGyroActivationButton.Value;
            }
            else if (sender == legionManager?.LegionGyroTarget)
            {
                Logger.Info($"Saving LegionGyroTarget to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroTarget = legionManager.LegionGyroTarget.Value;
            }
            else if (sender == legionManager?.LegionGyroSensitivityX)
            {
                Logger.Info($"Saving LegionGyroSensitivityX to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroSensitivityX = legionManager.LegionGyroSensitivityX.Value;
            }
            else if (sender == legionManager?.LegionGyroSensitivityY)
            {
                Logger.Info($"Saving LegionGyroSensitivityY to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroSensitivityY = legionManager.LegionGyroSensitivityY.Value;
            }
            else if (sender == legionManager?.LegionGyroInvertX)
            {
                Logger.Info($"Saving LegionGyroInvertX to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroInvertX = legionManager.LegionGyroInvertX.Value;
            }
            else if (sender == legionManager?.LegionGyroInvertY)
            {
                Logger.Info($"Saving LegionGyroInvertY to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroInvertY = legionManager.LegionGyroInvertY.Value;
            }
            else if (sender == legionManager?.LegionGyroMappingType)
            {
                Logger.Info($"Saving LegionGyroMappingType to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroMappingType = legionManager.LegionGyroMappingType.Value;
            }
            else if (sender == legionManager?.LegionGyroActivationMode)
            {
                Logger.Info($"Saving LegionGyroActivationMode to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroActivationMode = legionManager.LegionGyroActivationMode.Value;
            }
            else if (sender == legionManager?.LegionGyroDeadzone)
            {
                Logger.Info($"Saving LegionGyroDeadzone to profile {profileName}");
                profileManager.CurrentProfile.LegionGyroDeadzone = legionManager.LegionGyroDeadzone.Value;
            }
            // Stick deadzones
            else if (sender == legionManager?.LegionLeftStickDeadzone)
            {
                Logger.Info($"Saving LegionLeftStickDeadzone to profile {profileName}");
                profileManager.CurrentProfile.LegionLeftStickDeadzone = legionManager.LegionLeftStickDeadzone.Value;
            }
            else if (sender == legionManager?.LegionRightStickDeadzone)
            {
                Logger.Info($"Saving LegionRightStickDeadzone to profile {profileName}");
                profileManager.CurrentProfile.LegionRightStickDeadzone = legionManager.LegionRightStickDeadzone.Value;
            }
            // Trigger travel
            else if (sender == legionManager?.LegionLeftTriggerStart)
            {
                Logger.Info($"Saving LegionLeftTriggerStart to profile {profileName}");
                profileManager.CurrentProfile.LegionLeftTriggerStart = legionManager.LegionLeftTriggerStart.Value;
            }
            else if (sender == legionManager?.LegionLeftTriggerEnd)
            {
                Logger.Info($"Saving LegionLeftTriggerEnd to profile {profileName}");
                profileManager.CurrentProfile.LegionLeftTriggerEnd = legionManager.LegionLeftTriggerEnd.Value;
            }
            else if (sender == legionManager?.LegionRightTriggerStart)
            {
                Logger.Info($"Saving LegionRightTriggerStart to profile {profileName}");
                profileManager.CurrentProfile.LegionRightTriggerStart = legionManager.LegionRightTriggerStart.Value;
            }
            else if (sender == legionManager?.LegionRightTriggerEnd)
            {
                Logger.Info($"Saving LegionRightTriggerEnd to profile {profileName}");
                profileManager.CurrentProfile.LegionRightTriggerEnd = legionManager.LegionRightTriggerEnd.Value;
            }
            else if (sender == legionManager?.LegionHairTriggers)
            {
                Logger.Info($"Saving LegionHairTriggers to profile {profileName}");
                profileManager.CurrentProfile.LegionHairTriggers = legionManager.LegionHairTriggers.Value;
            }
            // Joystick as mouse
            else if (sender == legionManager?.LegionJoystickAsMouseMode)
            {
                Logger.Info($"Saving LegionJoystickAsMouseMode to profile {profileName}");
                profileManager.CurrentProfile.LegionJoystickAsMouseMode = legionManager.LegionJoystickAsMouseMode.Value;
            }
            else if (sender == legionManager?.LegionJoystickMouseSens)
            {
                Logger.Info($"Saving LegionJoystickMouseSens to profile {profileName}");
                profileManager.CurrentProfile.LegionJoystickMouseSens = legionManager.LegionJoystickMouseSens.Value;
            }
            // Gamepad mapping (covered by the ButtonMappings flag alongside the Y/M/Desktop/Page buttons)
            else if (sender == legionManager?.LegionGamepadMapping)
            {
                RouteProfileSave(saveButtonsToProfile, "LegionGamepadMapping",
                    cur => cur.LegionGamepadMapping = legionManager.LegionGamepadMapping.Value,
                    glo => glo.LegionGamepadMapping = legionManager.LegionGamepadMapping.Value);
            }
            // Nintendo layout — device-wide by default so a per-game toggle doesn't sit on top of
            // a stale True in GlobalProfile that then reappears after every reboot.
            else if (sender == legionManager?.LegionNintendoLayout)
            {
                RouteProfileSave(ProfileSaveFlagsState.NintendoLayout, "LegionNintendoLayout",
                    cur => cur.LegionNintendoLayout = legionManager.LegionNintendoLayout.Value,
                    glo => glo.LegionNintendoLayout = legionManager.LegionNintendoLayout.Value);
            }
            else if (sender == legionManager?.LegionVibration)
            {
                RouteProfileSave(ProfileSaveFlagsState.Vibration, "LegionVibration",
                    cur => cur.LegionVibration = legionManager.LegionVibration.Value,
                    glo => glo.LegionVibration = legionManager.LegionVibration.Value);
            }
            else if (sender == legionManager?.LegionVibrationMode)
            {
                RouteProfileSave(ProfileSaveFlagsState.Vibration, "LegionVibrationMode",
                    cur => cur.LegionVibrationMode = legionManager.LegionVibrationMode.Value,
                    glo => glo.LegionVibrationMode = legionManager.LegionVibrationMode.Value);
            }
            else if (sender == legionManager?.LegionControllerProfileEnabled)
            {
                Logger.Info($"Saving LegionControllerProfileEnabled to profile {profileName}");
                profileManager.CurrentProfile.LegionControllerProfileEnabled = legionManager.LegionControllerProfileEnabled.Value;
            }
            // Performance mode (for per-game TDP mode switching)
            else if (sender == legionManager?.LegionPerformanceMode)
            {
                Logger.Info($"Saving LegionPerformanceMode to profile {profileName}");
                profileManager.CurrentProfile.LegionPerformanceMode = legionManager.LegionPerformanceMode.Value;
                // Also save directly to GlobalProfile to ensure it's always in sync
                // This fixes an issue where the restore reads from GlobalProfile but save goes to CurrentProfile
                if (profileManager.CurrentProfile.IsGlobalProfile)
                {
                    profileManager.GlobalProfile.LegionPerformanceMode = legionManager.LegionPerformanceMode.Value;
                    Logger.Debug($"Also saved LegionPerformanceMode to GlobalProfile directly: {legionManager.LegionPerformanceMode.Value}");
                }
            }
            // Lighting settings — share a single Lighting flag
            else if (sender == legionManager?.LegionLightMode)
            {
                RouteProfileSave(ProfileSaveFlagsState.Lighting, "LegionLightMode",
                    cur => cur.LegionLightMode = legionManager.LegionLightMode.Value,
                    glo => glo.LegionLightMode = legionManager.LegionLightMode.Value);
            }
            else if (sender == legionManager?.LegionLightColor)
            {
                RouteProfileSave(ProfileSaveFlagsState.Lighting, "LegionLightColor",
                    cur => cur.LegionLightColor = legionManager.LegionLightColor.Value,
                    glo => glo.LegionLightColor = legionManager.LegionLightColor.Value);
            }
            else if (sender == legionManager?.LegionLightBrightness)
            {
                RouteProfileSave(ProfileSaveFlagsState.Lighting, "LegionLightBrightness",
                    cur => cur.LegionLightBrightness = legionManager.LegionLightBrightness.Value,
                    glo => glo.LegionLightBrightness = legionManager.LegionLightBrightness.Value);
            }
            else if (sender == legionManager?.LegionLightSpeed)
            {
                RouteProfileSave(ProfileSaveFlagsState.Lighting, "LegionLightSpeed",
                    cur => cur.LegionLightSpeed = legionManager.LegionLightSpeed.Value,
                    glo => glo.LegionLightSpeed = legionManager.LegionLightSpeed.Value);
            }
            else if (sender == legionManager?.LegionPowerLight)
            {
                RouteProfileSave(ProfileSaveFlagsState.Lighting, "LegionPowerLight",
                    cur => cur.LegionPowerLight = legionManager.LegionPowerLight.Value,
                    glo => glo.LegionPowerLight = legionManager.LegionPowerLight.Value);
            }
        }

    }
}
