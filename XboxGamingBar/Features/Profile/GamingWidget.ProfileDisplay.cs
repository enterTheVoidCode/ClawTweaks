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
            GlobalProfileTDPText.Text = $"{globalProfile.TDP}W";

            GlobalProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
            GlobalProfileCPUBoostText.Visibility = cpuBoostVisibility;
            GlobalProfileCPUBoostText.Text = globalProfile.CPUBoost ? "On" : "Off";

            GlobalProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
            GlobalProfileCPUEPPText.Visibility = cpuEPPVisibility;
            GlobalProfileCPUEPPText.Text = $"{globalProfile.CPUEPP}";

            GlobalProfileCPUStateLabel.Visibility = cpuStateVisibility;
            GlobalProfileCPUStateText.Visibility = cpuStateVisibility;
            GlobalProfileCPUStateText.Text = $"{globalProfile.MinCPUState}-{globalProfile.MaxCPUState}%";

            GlobalProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
            GlobalProfileFPSLimitText.Visibility = fpsLimitVisibility;
            GlobalProfileFPSLimitText.Text = globalProfile.FPSLimitEnabled ? $"{globalProfile.FPSLimitValue}" : "Off";

            GlobalProfileAutoTDPLabel.Visibility = autoTDPVisibility;
            GlobalProfileAutoTDPText.Visibility = autoTDPVisibility;
            GlobalProfileAutoTDPText.Text = globalProfile.AutoTDPEnabled ? $"{globalProfile.AutoTDPTargetFPS}fps" : "Off";

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

            ACDCProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
            ACProfileCPUEPPText.Visibility = cpuEPPVisibility;
            DCProfileCPUEPPText.Visibility = cpuEPPVisibility;
            ACProfileCPUEPPText.Text = $"{acProfile.CPUEPP}";
            DCProfileCPUEPPText.Text = $"{dcProfile.CPUEPP}";

            ACDCProfileCPUStateLabel.Visibility = cpuStateVisibility;
            ACProfileCPUStateText.Visibility = cpuStateVisibility;
            DCProfileCPUStateText.Visibility = cpuStateVisibility;
            ACProfileCPUStateText.Text = $"{acProfile.MinCPUState}-{acProfile.MaxCPUState}%";
            DCProfileCPUStateText.Text = $"{dcProfile.MinCPUState}-{dcProfile.MaxCPUState}%";

            ACDCProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
            ACProfileFPSLimitText.Visibility = fpsLimitVisibility;
            DCProfileFPSLimitText.Visibility = fpsLimitVisibility;
            ACProfileFPSLimitText.Text = acProfile.FPSLimitEnabled ? $"{acProfile.FPSLimitValue}" : "Off";
            DCProfileFPSLimitText.Text = dcProfile.FPSLimitEnabled ? $"{dcProfile.FPSLimitValue}" : "Off";

            ACDCProfileAutoTDPLabel.Visibility = autoTDPVisibility;
            ACProfileAutoTDPText.Visibility = autoTDPVisibility;
            DCProfileAutoTDPText.Visibility = autoTDPVisibility;
            ACProfileAutoTDPText.Text = acProfile.AutoTDPEnabled ? $"{acProfile.AutoTDPTargetFPS}fps" : "Off";
            DCProfileAutoTDPText.Text = dcProfile.AutoTDPEnabled ? $"{dcProfile.AutoTDPTargetFPS}fps" : "Off";

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
                    GameACProfileFPSLimitText.Text = gameACProfile.FPSLimitEnabled ? $"{gameACProfile.FPSLimitValue}" : "Off";
                    GameDCProfileFPSLimitText.Text = gameDCProfile.FPSLimitEnabled ? $"{gameDCProfile.FPSLimitValue}" : "Off";

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

                    // TDP Boost (saved with TDP)
                    GameProfileTDPBoostLabel.Visibility = tdpVisibility;
                    GameProfileTDPBoostText.Visibility = tdpVisibility;
                    GameProfileTDPBoostText.Text = gameProfile.TDPBoostEnabled ? "On" : "Off";

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

                    // FPS Limit
                    GameProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
                    GameProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameProfileFPSLimitText.Text = gameProfile.FPSLimitEnabled ? $"{gameProfile.FPSLimitValue}" : "Off";

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
                case 255: return "Custom";
                default: return "Balanced";
            }
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
                    return "Custom"; // The actual Custom mode after all presets
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
                // Validate the index is still valid with current preset configuration
                int maxIndex = useCustomTDPPresets && tdpPresets != null ? tdpPresets.Count : 3;
                if (profile.TDPModeIndex <= maxIndex)
                {
                    return profile.TDPModeIndex;
                }
            }
            // Fall back to legacy: convert LegionPerformanceMode to index
            int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom
            int index = Array.IndexOf(modeValues, profile.LegionPerformanceMode);
            return index >= 0 ? index : 1; // Default to Balanced if not found
        }

    }
}
