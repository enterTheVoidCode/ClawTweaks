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
        private void UpdateProfileDisplay()
        {
            // Guard against calls during XAML initialization when controls aren't ready
            if (GlobalProfileTDPModeLabel == null) return;

            // Determine visibility based on save settings
            var tdpModeVisibility = (legionGoDetected?.Value == true && SaveTDP) ? Visibility.Visible : Visibility.Collapsed;
            var tdpVisibility = SaveTDP ? Visibility.Visible : Visibility.Collapsed;
            var cpuBoostVisibility = SaveCPUBoost ? Visibility.Visible : Visibility.Collapsed;
            var cpuEPPVisibility = SaveCPUEPP ? Visibility.Visible : Visibility.Collapsed;
            var cpuStateVisibility = SaveCPUState ? Visibility.Visible : Visibility.Collapsed;
            var fpsLimitVisibility = SaveFPSLimit ? Visibility.Visible : Visibility.Collapsed;
            var autoTDPVisibility = SaveAutoTDP ? Visibility.Visible : Visibility.Collapsed;
            var powerModeVisibility = SaveOSPowerMode ? Visibility.Visible : Visibility.Collapsed;
            var amdVisibility = SaveAMDFeatures ? Visibility.Visible : Visibility.Collapsed;
            var hdrVisibility = SaveHDR ? Visibility.Visible : Visibility.Collapsed;
            var resolutionVisibility = SaveResolution ? Visibility.Visible : Visibility.Collapsed;
            var stickyTDPVisibility = SaveStickyTDP ? Visibility.Visible : Visibility.Collapsed;

            // Update Global profile display (simple mode)
            GlobalProfileTDPModeLabel.Visibility = tdpModeVisibility;
            GlobalProfileTDPModeText.Visibility = tdpModeVisibility;
            GlobalProfileTDPModeText.Text = GetProfileTDPModeName(globalProfile);

            GlobalProfileTDPLabel.Visibility = tdpVisibility;
            GlobalProfileTDPText.Visibility = tdpVisibility;
            // Show the effective TDP: for preset modes, read the watt value from the preset definition
            // (not the possibly-stale stored globalProfile.TDP) so the card always matches what's active.
            {
                double displayTDP = globalProfile.TDP;
                if (!IsCustomTdpModeSelected() && TDPModeComboBox != null)
                {
                    int presetWatts = GetCurrentPresetTdpValue();
                    if (presetWatts > 0)
                        displayTDP = presetWatts;
                }
                GlobalProfileTDPText.Text = $"{displayTDP}W";
            }

            GlobalProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
            GlobalProfileCPUBoostText.Visibility = cpuBoostVisibility;
            GlobalProfileCPUBoostText.Text = globalProfile.CPUBoost ? "On" : "Off";

            // Repurposed: CPU EPP slot → FPS Mode — read from saved profile, not live state
            GlobalProfileCPUEPPLabel.Text = "FPS Mode";
            GlobalProfileCPUEPPLabel.Visibility = fpsLimitVisibility;
            GlobalProfileCPUEPPText.Visibility = fpsLimitVisibility;
            GlobalProfileCPUEPPText.Text = GetFpsCapModeLabel(globalProfile);

            // Repurposed: CPU State slot → TDP Overboost
            GlobalProfileCPUStateLabel.Text = "TDP Overboost";
            GlobalProfileCPUStateLabel.Visibility = tdpVisibility;
            GlobalProfileCPUStateText.Visibility = tdpVisibility;
            GlobalProfileCPUStateText.Text = globalProfile.TDPBoostEnabled ? "On" : "Off";

            // PL2 value — shown as sub-row when Overboost is on
            var pl2Visibility = (globalProfile.TDPBoostEnabled && tdpVisibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
            if (GlobalProfilePL2Label != null) GlobalProfilePL2Label.Visibility = pl2Visibility;
            if (GlobalProfilePL2Text  != null)
            {
                GlobalProfilePL2Text.Visibility = pl2Visibility;
                GlobalProfilePL2Text.Text = $"{globalProfile.TDPBoostFPPTWatts}W";
            }

            GlobalProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
            GlobalProfileFPSLimitText.Visibility = fpsLimitVisibility;
            GlobalProfileFPSLimitText.Text = GetFpsValueLabel(globalProfile);

            // AutoTDP hidden (not relevant for MSI Claw)
            GlobalProfileAutoTDPLabel.Visibility = Visibility.Collapsed;
            GlobalProfileAutoTDPText.Visibility = Visibility.Collapsed;

            GlobalProfilePowerModeLabel.Visibility = powerModeVisibility;
            GlobalProfilePowerModeText.Visibility = powerModeVisibility;
            GlobalProfilePowerModeText.Text = GetPowerModeShortName(globalProfile.OSPowerMode);

            GlobalProfileAMDLabel.Visibility = amdVisibility;
            GlobalProfileAMDText.Visibility = amdVisibility;
            var globalAmdFeatures = GetAMDFeaturesShortString(globalProfile);
            GlobalProfileAMDText.Text = string.IsNullOrEmpty(globalAmdFeatures) ? "Off" : globalAmdFeatures;

            GlobalProfileHDRLabel.Visibility = hdrVisibility;
            GlobalProfileHDRText.Visibility = hdrVisibility;
            GlobalProfileHDRText.Text = globalProfile.HDREnabled ? "On" : "Off";

            GlobalProfileResolutionLabel.Visibility = resolutionVisibility;
            GlobalProfileResolutionText.Visibility = resolutionVisibility;
            GlobalProfileResolutionText.Text = string.IsNullOrEmpty(globalProfile.Resolution) ? "Native" : globalProfile.Resolution;

            GlobalProfileStickyTDPLabel.Visibility = stickyTDPVisibility;
            GlobalProfileStickyTDPText.Visibility = stickyTDPVisibility;
            GlobalProfileStickyTDPText.Text = globalProfile.StickyTDPEnabled ? "On" : "Off";

            // Update AC/DC profile display
            ACDCProfileTDPModeLabel.Visibility = tdpModeVisibility;
            ACProfileTDPModeText.Visibility = tdpModeVisibility;
            DCProfileTDPModeText.Visibility = tdpModeVisibility;
            ACProfileTDPModeText.Text = GetProfileTDPModeName(acProfile);
            DCProfileTDPModeText.Text = GetProfileTDPModeName(dcProfile);

            ACDCProfileTDPLabel.Visibility = tdpVisibility;
            ACProfileTDPText.Visibility = tdpVisibility;
            DCProfileTDPText.Visibility = tdpVisibility;
            ACProfileTDPText.Text = $"{acProfile.TDP}W";
            DCProfileTDPText.Text = $"{dcProfile.TDP}W";

            ACDCProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
            ACProfileCPUBoostText.Visibility = cpuBoostVisibility;
            DCProfileCPUBoostText.Visibility = cpuBoostVisibility;
            ACProfileCPUBoostText.Text = acProfile.CPUBoost ? "On" : "Off";
            DCProfileCPUBoostText.Text = dcProfile.CPUBoost ? "On" : "Off";

            // Repurposed: CPU EPP slot → FPS Mode (same value for AC/DC, it's a global setting)
            string acDcFpsMode = GetFpsCapModeLabel();
            ACDCProfileCPUEPPLabel.Text = "FPS Mode";
            ACDCProfileCPUEPPLabel.Visibility = fpsLimitVisibility;
            ACProfileCPUEPPText.Visibility = fpsLimitVisibility;
            DCProfileCPUEPPText.Visibility = fpsLimitVisibility;
            ACProfileCPUEPPText.Text = acDcFpsMode;
            DCProfileCPUEPPText.Text = acDcFpsMode;

            // Repurposed: CPU State slot → TDP Overboost
            ACDCProfileCPUStateLabel.Text = "TDP Overboost";
            ACDCProfileCPUStateLabel.Visibility = tdpVisibility;
            ACProfileCPUStateText.Visibility = tdpVisibility;
            DCProfileCPUStateText.Visibility = tdpVisibility;
            ACProfileCPUStateText.Text = acProfile.TDPBoostEnabled ? "On" : "Off";
            DCProfileCPUStateText.Text = dcProfile.TDPBoostEnabled ? "On" : "Off";

            ACDCProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
            ACProfileFPSLimitText.Visibility = fpsLimitVisibility;
            DCProfileFPSLimitText.Visibility = fpsLimitVisibility;
            ACProfileFPSLimitText.Text = GetFpsValueLabel(acProfile);
            DCProfileFPSLimitText.Text = GetFpsValueLabel(dcProfile);

            // AutoTDP hidden (not relevant for MSI Claw)
            ACDCProfileAutoTDPLabel.Visibility = Visibility.Collapsed;
            ACProfileAutoTDPText.Visibility = Visibility.Collapsed;
            DCProfileAutoTDPText.Visibility = Visibility.Collapsed;

            ACDCProfilePowerModeLabel.Visibility = powerModeVisibility;
            ACProfilePowerModeText.Visibility = powerModeVisibility;
            DCProfilePowerModeText.Visibility = powerModeVisibility;
            ACProfilePowerModeText.Text = GetPowerModeShortName(acProfile.OSPowerMode);
            DCProfilePowerModeText.Text = GetPowerModeShortName(dcProfile.OSPowerMode);

            ACDCProfileAMDLabel.Visibility = amdVisibility;
            ACProfileAMDText.Visibility = amdVisibility;
            DCProfileAMDText.Visibility = amdVisibility;
            var acAmdFeatures = GetAMDFeaturesShortString(acProfile);
            var dcAmdFeatures = GetAMDFeaturesShortString(dcProfile);
            ACProfileAMDText.Text = string.IsNullOrEmpty(acAmdFeatures) ? "Off" : acAmdFeatures;
            DCProfileAMDText.Text = string.IsNullOrEmpty(dcAmdFeatures) ? "Off" : dcAmdFeatures;

            ACDCProfileHDRLabel.Visibility = hdrVisibility;
            ACProfileHDRText.Visibility = hdrVisibility;
            DCProfileHDRText.Visibility = hdrVisibility;
            ACProfileHDRText.Text = acProfile.HDREnabled ? "On" : "Off";
            DCProfileHDRText.Text = dcProfile.HDREnabled ? "On" : "Off";

            ACDCProfileResolutionLabel.Visibility = resolutionVisibility;
            ACProfileResolutionText.Visibility = resolutionVisibility;
            DCProfileResolutionText.Visibility = resolutionVisibility;
            ACProfileResolutionText.Text = string.IsNullOrEmpty(acProfile.Resolution) ? "Native" : acProfile.Resolution;
            DCProfileResolutionText.Text = string.IsNullOrEmpty(dcProfile.Resolution) ? "Native" : dcProfile.Resolution;

            ACDCProfileStickyTDPLabel.Visibility = stickyTDPVisibility;
            ACProfileStickyTDPText.Visibility = stickyTDPVisibility;
            DCProfileStickyTDPText.Visibility = stickyTDPVisibility;
            ACProfileStickyTDPText.Text = acProfile.StickyTDPEnabled ? "On" : "Off";
            DCProfileStickyTDPText.Text = dcProfile.StickyTDPEnabled ? "On" : "Off";

            // Update game profile display (if game is running)
            if (HasValidGame(currentGameName))
            {
                if (GetPerGamePowerSourceProfileEnabled(currentGameName))
                {
                    // Show AC/DC game profiles - TDP Mode (Legion only)
                    GameACDCProfileTDPModeLabel.Visibility = tdpModeVisibility;
                    GameACProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameDCProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameACProfileTDPModeText.Text = GetProfileTDPModeName(gameACProfile);
                    GameDCProfileTDPModeText.Text = GetProfileTDPModeName(gameDCProfile);

                    // TDP
                    GameACDCProfileTDPLabel.Visibility = tdpVisibility;
                    GameACProfileTDPText.Visibility = tdpVisibility;
                    GameDCProfileTDPText.Visibility = tdpVisibility;
                    GameACProfileTDPText.Text = $"{gameACProfile.TDP}W";
                    GameDCProfileTDPText.Text = $"{gameDCProfile.TDP}W";

                    // CPU Boost
                    GameACDCProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
                    GameACProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    GameDCProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    GameACProfileCPUBoostText.Text = gameACProfile.CPUBoost ? "On" : "Off";
                    GameDCProfileCPUBoostText.Text = gameDCProfile.CPUBoost ? "On" : "Off";

                    // CPU EPP
                    GameACDCProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
                    GameACProfileCPUEPPText.Visibility = cpuEPPVisibility;
                    GameDCProfileCPUEPPText.Visibility = cpuEPPVisibility;
                    GameACProfileCPUEPPText.Text = $"{gameACProfile.CPUEPP}";
                    GameDCProfileCPUEPPText.Text = $"{gameDCProfile.CPUEPP}";

                    // CPU State
                    GameACDCProfileCPUStateLabel.Visibility = cpuStateVisibility;
                    GameACProfileCPUStateText.Visibility = cpuStateVisibility;
                    GameDCProfileCPUStateText.Visibility = cpuStateVisibility;
                    GameACProfileCPUStateText.Text = $"{gameACProfile.MinCPUState}-{gameACProfile.MaxCPUState}%";
                    GameDCProfileCPUStateText.Text = $"{gameDCProfile.MinCPUState}-{gameDCProfile.MaxCPUState}%";

                    // FPS Limit
                    GameACDCProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
                    GameACProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameDCProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameACProfileFPSLimitText.Text = GetFpsValueLabel(gameACProfile);
                    GameDCProfileFPSLimitText.Text = GetFpsValueLabel(gameDCProfile);

                    // AutoTDP
                    GameACDCProfileAutoTDPLabel.Visibility = autoTDPVisibility;
                    GameACProfileAutoTDPText.Visibility = autoTDPVisibility;
                    GameDCProfileAutoTDPText.Visibility = autoTDPVisibility;
                    GameACProfileAutoTDPText.Text = gameACProfile.AutoTDPEnabled ? $"{gameACProfile.AutoTDPTargetFPS}fps" : "Off";
                    GameDCProfileAutoTDPText.Text = gameDCProfile.AutoTDPEnabled ? $"{gameDCProfile.AutoTDPTargetFPS}fps" : "Off";

                    // Power Mode
                    GameACDCProfilePowerModeLabel.Visibility = powerModeVisibility;
                    GameACProfilePowerModeText.Visibility = powerModeVisibility;
                    GameDCProfilePowerModeText.Visibility = powerModeVisibility;
                    GameACProfilePowerModeText.Text = GetPowerModeShortName(gameACProfile.OSPowerMode);
                    GameDCProfilePowerModeText.Text = GetPowerModeShortName(gameDCProfile.OSPowerMode);

                    // AMD Features
                    GameACDCProfileAMDLabel.Visibility = amdVisibility;
                    GameACProfileAMDText.Visibility = amdVisibility;
                    GameDCProfileAMDText.Visibility = amdVisibility;
                    var gameACAmdFeatures = GetAMDFeaturesShortString(gameACProfile);
                    var gameDCAmdFeatures = GetAMDFeaturesShortString(gameDCProfile);
                    GameACProfileAMDText.Text = string.IsNullOrEmpty(gameACAmdFeatures) ? "Off" : gameACAmdFeatures;
                    GameDCProfileAMDText.Text = string.IsNullOrEmpty(gameDCAmdFeatures) ? "Off" : gameDCAmdFeatures;

                    // HDR
                    GameACDCProfileHDRLabel.Visibility = hdrVisibility;
                    GameACProfileHDRText.Visibility = hdrVisibility;
                    GameDCProfileHDRText.Visibility = hdrVisibility;
                    GameACProfileHDRText.Text = gameACProfile.HDREnabled ? "On" : "Off";
                    GameDCProfileHDRText.Text = gameDCProfile.HDREnabled ? "On" : "Off";

                    // Resolution
                    GameACDCProfileResolutionLabel.Visibility = resolutionVisibility;
                    GameACProfileResolutionText.Visibility = resolutionVisibility;
                    GameDCProfileResolutionText.Visibility = resolutionVisibility;
                    GameACProfileResolutionText.Text = string.IsNullOrEmpty(gameACProfile.Resolution) ? "Native" : gameACProfile.Resolution;
                    GameDCProfileResolutionText.Text = string.IsNullOrEmpty(gameDCProfile.Resolution) ? "Native" : gameDCProfile.Resolution;

                    // Sticky TDP
                    GameACDCProfileStickyTDPLabel.Visibility = stickyTDPVisibility;
                    GameACProfileStickyTDPText.Visibility = stickyTDPVisibility;
                    GameDCProfileStickyTDPText.Visibility = stickyTDPVisibility;
                    GameACProfileStickyTDPText.Text = gameACProfile.StickyTDPEnabled ? "On" : "Off";
                    GameDCProfileStickyTDPText.Text = gameDCProfile.StickyTDPEnabled ? "On" : "Off";

                    // MSI Claw: repurpose CPUEPPLabel/Text slot → Gyro On/Off (same value for AC/DC)
                    bool gyroOnACDC = (legionGyroTarget?.Value ?? 0) != 0;
                    string gyroText = gyroOnACDC ? "On" : "Off";
                    GameACDCProfileCPUEPPLabel.Text = "Gyro";
                    GameACDCProfileCPUEPPLabel.Visibility = Visibility.Visible;
                    GameACProfileCPUEPPText.Text = gyroText;
                    GameACProfileCPUEPPText.Visibility = Visibility.Visible;
                    GameDCProfileCPUEPPText.Text = gyroText;
                    GameDCProfileCPUEPPText.Visibility = Visibility.Visible;

                    // MSI Claw: repurpose CPUStateLabel/Text slot → FPS Limiter Mode
                    bool acFpsOn = gameACProfile.FPSLimitEnabled
                                || (gameACProfile.FpsCapMode == 1 && gameACProfile.IntelFpsTier > 0);
                    bool dcFpsOn = gameDCProfile.FPSLimitEnabled
                                || (gameDCProfile.FpsCapMode == 1 && gameDCProfile.IntelFpsTier > 0);
                    if (acFpsOn || dcFpsOn)
                    {
                        // Use stored profile value, not live fpsCapMode
                        string acModeText = (gameACProfile.FpsCapMode == 1) ? "Intel" : "RTSS";
                        string dcModeText = (gameDCProfile.FpsCapMode == 1) ? "Intel" : "RTSS";
                        GameACDCProfileCPUStateLabel.Text = "FPS Mode";
                        GameACDCProfileCPUStateLabel.Visibility = Visibility.Visible;
                        GameACProfileCPUStateText.Text = acModeText;
                        GameACProfileCPUStateText.Visibility = Visibility.Visible;
                        GameDCProfileCPUStateText.Text = dcModeText;
                        GameDCProfileCPUStateText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        GameACDCProfileCPUStateLabel.Visibility = Visibility.Collapsed;
                        GameACProfileCPUStateText.Visibility = Visibility.Collapsed;
                        GameDCProfileCPUStateText.Visibility = Visibility.Collapsed;
                    }

                    // Always hide AutoTDP row in game profile card (not relevant for MSI Claw)
                    GameACDCProfileAutoTDPLabel.Visibility = Visibility.Collapsed;
                    GameACProfileAutoTDPText.Visibility = Visibility.Collapsed;
                    GameDCProfileAutoTDPText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Show single game profile - TDP Mode (Legion only)
                    GameProfileTDPModeLabel.Visibility = tdpModeVisibility;
                    GameProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameProfileTDPModeText.Text = GetProfileTDPModeName(gameProfile);

                    // TDP
                    GameProfileTDPLabel.Visibility = tdpVisibility;
                    GameProfileTDPText.Visibility = tdpVisibility;
                    GameProfileTDPText.Text = $"{gameProfile.TDP}W";

                    // TDP Overboost (saved with TDP)
                    GameProfileTDPBoostLabel.Text = "TDP Overboost";
                    GameProfileTDPBoostLabel.Visibility = tdpVisibility;
                    GameProfileTDPBoostText.Visibility = tdpVisibility;
                    GameProfileTDPBoostText.Text = gameProfile.TDPBoostEnabled ? "On" : "Off";

                    // PL2 value — sub-row when Overboost is on
                    var gamePl2Visibility = (gameProfile.TDPBoostEnabled && tdpVisibility == Visibility.Visible)
                        ? Visibility.Visible : Visibility.Collapsed;
                    if (GameProfilePL2Label != null) GameProfilePL2Label.Visibility = gamePl2Visibility;
                    if (GameProfilePL2Text  != null)
                    {
                        GameProfilePL2Text.Visibility = gamePl2Visibility;
                        GameProfilePL2Text.Text = $"{gameProfile.TDPBoostFPPTWatts}W";
                    }

                    // CPU Boost
                    GameProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
                    GameProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    GameProfileCPUBoostText.Text = gameProfile.CPUBoost ? "On" : "Off";

                    // CPU EPP
                    GameProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
                    GameProfileCPUEPPText.Visibility = cpuEPPVisibility;
                    GameProfileCPUEPPText.Text = $"{gameProfile.CPUEPP}";

                    // CPU State
                    GameProfileCPUStateLabel.Visibility = cpuStateVisibility;
                    GameProfileCPUStateText.Visibility = cpuStateVisibility;
                    GameProfileCPUStateText.Text = $"{gameProfile.MinCPUState}-{gameProfile.MaxCPUState}%";

                    // FPS Limit — use GetFpsValueLabel so Intel tier is shown correctly
                    GameProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
                    GameProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameProfileFPSLimitText.Text = GetFpsValueLabel(gameProfile);

                    // AutoTDP
                    GameProfileAutoTDPLabel.Visibility = autoTDPVisibility;
                    GameProfileAutoTDPText.Visibility = autoTDPVisibility;
                    GameProfileAutoTDPText.Text = gameProfile.AutoTDPEnabled ? $"{gameProfile.AutoTDPTargetFPS}fps" : "Off";

                    // Power Mode
                    GameProfilePowerModeLabel.Visibility = powerModeVisibility;
                    GameProfilePowerModeText.Visibility = powerModeVisibility;
                    GameProfilePowerModeText.Text = GetPowerModeShortName(gameProfile.OSPowerMode);

                    // AMD Features
                    GameProfileAMDLabel.Visibility = amdVisibility;
                    GameProfileAMDText.Visibility = amdVisibility;
                    var gameAmdFeatures = GetAMDFeaturesShortString(gameProfile);
                    GameProfileAMDText.Text = string.IsNullOrEmpty(gameAmdFeatures) ? "Off" : gameAmdFeatures;

                    // HDR
                    GameProfileHDRLabel.Visibility = hdrVisibility;
                    GameProfileHDRText.Visibility = hdrVisibility;
                    GameProfileHDRText.Text = gameProfile.HDREnabled ? "On" : "Off";

                    // Resolution
                    GameProfileResolutionLabel.Visibility = resolutionVisibility;
                    GameProfileResolutionText.Visibility = resolutionVisibility;
                    GameProfileResolutionText.Text = string.IsNullOrEmpty(gameProfile.Resolution) ? "Native" : gameProfile.Resolution;

                    // Sticky TDP
                    GameProfileStickyTDPLabel.Visibility = stickyTDPVisibility;
                    GameProfileStickyTDPText.Visibility = stickyTDPVisibility;
                    GameProfileStickyTDPText.Text = gameProfile.StickyTDPEnabled ? "On" : "Off";

                    // MSI Claw: repurpose CPUEPPLabel/Text slot → Gyro On/Off
                    bool gyroOn = (legionGyroTarget?.Value ?? 0) != 0;
                    GameProfileCPUEPPLabel.Text = "Gyro";
                    GameProfileCPUEPPLabel.Visibility = Visibility.Visible;
                    GameProfileCPUEPPText.Text = gyroOn ? "On" : "Off";
                    GameProfileCPUEPPText.Visibility = Visibility.Visible;

                    // MSI Claw: repurpose CPUStateLabel/Text slot → FPS Limiter Mode (only when FPS limit is active)
                    if (gameProfile.FPSLimitEnabled)
                    {
                        GameProfileCPUStateLabel.Text = "FPS Mode";
                        GameProfileCPUStateLabel.Visibility = Visibility.Visible;
                        GameProfileCPUStateText.Text = (fpsCapMode?.Value == 1) ? "Intel" : "RTSS";
                        GameProfileCPUStateText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        GameProfileCPUStateLabel.Visibility = Visibility.Collapsed;
                        GameProfileCPUStateText.Visibility = Visibility.Collapsed;
                    }

                    // Always hide AutoTDP row in game profile card (not relevant for MSI Claw)
                    GameProfileAutoTDPLabel.Visibility = Visibility.Collapsed;
                    GameProfileAutoTDPText.Visibility = Visibility.Collapsed;
                }
            }

            // Update all saved game profiles display
            UpdateAllGameProfilesDisplay();
        }

        private static string GetPowerModeShortName(int mode)
        {
            switch (mode)
            {
                case 0: return "Efficiency";
                case 1: return "Balanced";
                case 2: return "Performance";
                default: return "Balanced";
            }
        }

        private static string GetLegionModeShortName(int mode)
        {
            switch (mode)
            {
                case 1: return "Quiet";
                case 2: return "Balanced";
                case 3: return "Performance";
                case 255: return "Slider";
                default: return "Balanced";
            }
        }

        /// <summary>
        /// Returns the active FPS value label for the global profile card.
        /// Reads live state (fpsCapMode / intelFpsTier / FPSLimitSlider) so the card
        /// always reflects the currently active limiter, not a stale RTSS slider value.
        /// </summary>
        private string GetActiveFpsValueLabel()
        {
            if (fpsCapMode?.Value == 1)
            {
                // Intel mode — derive fps from tier
                int tier = intelFpsTier?.Value ?? 0;
                switch (tier)
                {
                    case 1: return "60";
                    case 2: return "40";
                    case 3: return "30";
                    default: return "Off";
                }
            }
            // RTSS mode — use toggle + slider
            return FPSLimitToggle?.IsOn == true
                ? $"{(int)(FPSLimitSlider?.Value ?? 0)}"
                : "Off";
        }

        /// <summary>
        /// Returns a short label for the active FPS cap mode (RTSS / Intel tier).
        /// Reads the global fpsCapMode and intelFpsTier widget properties (live state).
        /// </summary>
        private string GetFpsCapModeLabel()
        {
            if (fpsCapMode?.Value == 1)
            {
                int tier = intelFpsTier?.Value ?? 0;
                switch (tier)
                {
                    case 1: return "Intel 60";
                    case 2: return "Intel 40";
                    case 3: return "Intel 30";
                    default: return "Intel";
                }
            }
            return "RTSS";
        }

        /// <summary>
        /// Returns the FPS value label for a saved PerformanceProfile.
        /// Mode-aware: shows the Intel tier fps when Intel mode is saved,
        /// not the stale RTSS slider value.
        /// </summary>
        private static string GetFpsValueLabel(PerformanceProfile profile)
        {
            if (profile == null) return "Off";
            if (profile.FpsCapMode == 1)
            {
                switch (profile.IntelFpsTier)
                {
                    case 1: return "60";
                    case 2: return "40";
                    case 3: return "30";
                    default: return "Off";
                }
            }
            return profile.FPSLimitEnabled ? $"{profile.FPSLimitValue}" : "Off";
        }

        /// <summary>
        /// Reads FPS cap mode from a saved PerformanceProfile — use in profile cards
        /// so each card shows its own stored settings, not the current live state.
        /// </summary>
        private string GetFpsCapModeLabel(PerformanceProfile profile)
        {
            if (profile == null) return "RTSS";
            if (profile.FpsCapMode == 1)
            {
                switch (profile.IntelFpsTier)
                {
                    case 1: return "Intel 60";
                    case 2: return "Intel 40";
                    case 3: return "Intel 30";
                    default: return "Intel";
                }
            }
            return "RTSS";
        }

        /// <summary>
        /// Gets the TDP mode display name from a profile, accounting for custom presets.
        /// </summary>
        private string GetProfileTDPModeName(PerformanceProfile profile)
        {
            // If TDPModeIndex is set and we have custom presets, use the preset name
            if (profile.TDPModeIndex >= 0 && useCustomTDPPresets && tdpPresets != null)
            {
                if (profile.TDPModeIndex < tdpPresets.Count)
                {
                    return tdpPresets[profile.TDPModeIndex].Name;
                }
                else if (profile.TDPModeIndex == tdpPresets.Count)
                {
                    return "Slider"; // The actual Slider mode after all presets
                }
            }
            // Fall back to legacy mode name
            return GetLegionModeShortName(profile.LegionPerformanceMode);
        }

        /// <summary>
        /// Gets the TDPModeComboBox index from a profile, accounting for custom presets.
        /// Returns the index to use for TDPModeComboBox.SelectedIndex.
        /// </summary>
        private int GetProfileTDPModeIndex(PerformanceProfile profile)
        {
            // If TDPModeIndex is set, use it directly (for custom presets)
            if (profile.TDPModeIndex >= 0)
            {
                // Validate the index is still valid with current preset configuration.
                // For custom presets: valid range 0..tdpPresets.Count (Slider = tdpPresets.Count).
                // For default presets: valid range 0..5 (Max=0..SuperBattery=4, Slider=5).
                int maxIndex = useCustomTDPPresets && tdpPresets != null ? tdpPresets.Count : 5;
                if (profile.TDPModeIndex <= maxIndex)
                {
                    return profile.TDPModeIndex;
                }
            }
            // Fall back: no valid TDPModeIndex saved — match by stored TDP watt value so we
            // don't blindly reset to Standard (25 W) when the user had e.g. Max (30 W) active.
            bool isLegionDevice = legionGoDetected?.Value == true;
            if (!isLegionDevice)
            {
                int tdpWatts = (int)Math.Round((double)profile.TDP);
                if (useCustomTDPPresets && tdpPresets != null)
                {
                    for (int i = 0; i < tdpPresets.Count; i++)
                    {
                        if (tdpPresets[i].TdpWatts == tdpWatts)
                            return i;
                    }
                }
                else
                {
                    // Default hardcoded preset watt values: Max(30), Standard(25), Balanced(17), Battery(12), SuperBattery(8)
                    int[] defaultTdpValues = { 30, 25, 17, 12, 8 };
                    for (int i = 0; i < defaultTdpValues.Length; i++)
                    {
                        if (defaultTdpValues[i] == tdpWatts)
                            return i;
                    }
                }
                return 1; // Default to Standard if no watt match found
            }
            // Legion device: convert LegionPerformanceMode to index
            int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom
            int index = Array.IndexOf(modeValues, profile.LegionPerformanceMode);
            return index >= 0 ? index : 1; // Default to Balanced for Legion if not found
        }

    }
}
