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
            var powerModeVisibility = SaveOSPowerMode ? Visibility.Visible : Visibility.Collapsed;
            var amdVisibility = SaveAMDFeatures ? Visibility.Visible : Visibility.Collapsed;
            // HDR is gone from the profiles (user, 2026-08-02): no Claw model has an HDR panel,
            // so the rows described a capability the device does not have. The live HDR control is a
            // separate thing and untouched.
            var resolutionVisibility = SaveResolution ? Visibility.Visible : Visibility.Collapsed;

            // Plan §5.3: group A/B values come from the HELPER's profile store, not from the widget's
            // copy. Resolved once per refresh — GetByName parses at most once per received snapshot,
            // but resolving per row would still be needless work.
            var snapGlobal = profileSnapshot?.GetGlobal();
            var snapGame = HasValidGame(currentGameName) ? profileSnapshot?.GetByName(currentGameName) : null;
            bool onBattery = Data.ProfileSnapshotProperty.IsOnBattery;

            // Plan §6: until the first snapshot arrives, rows fed by it stay HIDDEN. They must not show
            // 0 W / "Off" in the startup gap — a visible wrong value is worse than a missing row, and it
            // is the most likely new bug report of this rebuild.
            bool snapReady = profileSnapshot?.HasSnapshot == true;
            var snapTdpModeVis = (snapReady && snapGlobal != null) ? tdpModeVisibility : Visibility.Collapsed;
            var snapFpsVis = (snapReady && snapGlobal != null) ? fpsLimitVisibility : Visibility.Collapsed;

            // LegionPerformanceMode is nullable in the helper's profile (never captured). 2 = Balanced,
            // the same fallback the widget's copy defaulted to.
            const int LegionModeFallback = 2;

            // The global fan for the state on screen. It comes from the helper rather than the snapshot
            // because the global curve is the one fan value kept in the helper's own LocalSettings.
            string globalFanNow = onBattery ? _globalFanCurveBattery : _globalFanCurvePlugged;

            // Multi-column rendering of the simple global profile (matches the saved cards).
            if (GlobalProfilePairs != null)
            {
                GlobalProfilePairs.Children.Clear();
                GlobalProfilePairs.Children.Add(RenderPairsGrid(
                    BuildProfileCardPairs(globalProfile, snapGlobal, onBattery, globalFanNow)));
            }

            // Un-hide whatever the previous collapse pass folded away, BEFORE this refresh assigns row
            // visibilities — see RestoreCollapsedSplitRows for why the order is not interchangeable.
            RestoreCollapsedSplitRows(GlobalProfileACDC);
            RestoreCollapsedSplitRows(GameProfileACDC);

            // The split view of the same card: the rows the XAML grid does not declare (Intel channels,
            // frame generation, VRR, fan) appended for both power states at once.
            RenderSplitExtraRows(GlobalProfileACDC,
                BuildSplitExtraPairs(snapGlobal, onBattery: false, globalFanCurve: _globalFanCurvePlugged),
                BuildSplitExtraPairs(snapGlobal, onBattery: true,  globalFanCurve: _globalFanCurveBattery));

            // Update Global profile display (simple mode)
            GlobalProfileTDPModeLabel.Visibility = snapTdpModeVis;
            GlobalProfileTDPModeText.Visibility = snapTdpModeVis;
            GlobalProfileTDPModeText.Text = GetProfileTDPModeName(
                globalProfile.TDPModeIndex,                                   // group C: widget's combo index
                snapGlobal?.LegionPerformanceMode ?? LegionModeFallback);     // group A: helper's store

            var snapTdpVis = (snapReady && snapGlobal != null) ? tdpVisibility : Visibility.Collapsed;
            var snapCpuBoostVis = (snapReady && snapGlobal != null) ? cpuBoostVisibility : Visibility.Collapsed;

            GlobalProfileTDPLabel.Visibility = snapTdpVis;
            GlobalProfileTDPText.Visibility = snapTdpVis;
            // Show the effective TDP: for preset modes, read the watt value from the preset definition
            // so the card always matches what's active. The stored value now comes from the HELPER's
            // profile (plan §5.3) — the "possibly-stale stored value" the old comment warned about was
            // the widget's own copy, which is exactly what this rebuild removes.
            if (snapGlobal != null)
            {
                double displayTDP = snapGlobal.EffectiveTDP(onBattery);
                if (!IsCustomTdpModeSelected() && TDPModeComboBox != null)
                {
                    int presetWatts = GetCurrentPresetTdpValue();
                    if (presetWatts > 0)
                        displayTDP = presetWatts;
                }
                GlobalProfileTDPText.Text = $"{displayTDP}W";
            }

            GlobalProfileCPUBoostLabel.Visibility = snapCpuBoostVis;
            GlobalProfileCPUBoostText.Visibility = snapCpuBoostVis;
            if (snapGlobal != null)
                GlobalProfileCPUBoostText.Text = snapGlobal.EffectiveCPUBoost(onBattery) ? "On" : "Off";

            // Repurposed: CPU EPP slot → FPS Mode — read from saved profile, not live state
            GlobalProfileCPUEPPLabel.Text = "FPS Mode";
            GlobalProfileCPUEPPLabel.Visibility = fpsLimitVisibility;
            GlobalProfileCPUEPPText.Visibility = snapFpsVis;
            GlobalProfileCPUEPPText.Text = GetFpsCapModeLabel(snapGlobal, onBattery);

            // Repurposed: CPU State slot → TDP Overboost
            GlobalProfileCPUStateLabel.Text = "TDP Overboost";
            GlobalProfileCPUStateLabel.Visibility = snapTdpVis;
            GlobalProfileCPUStateText.Visibility = snapTdpVis;
            GlobalProfileCPUStateText.Text = (snapGlobal?.EffectiveTDPBoostEnabled(onBattery) == true) ? "On" : "Off";

            // PL2 value — shown as sub-row when Overboost is on
            var pl2Visibility = (snapGlobal?.EffectiveTDPBoostEnabled(onBattery) == true && snapTdpVis == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
            if (GlobalProfilePL2Label != null) GlobalProfilePL2Label.Visibility = pl2Visibility;
            if (GlobalProfilePL2Text  != null)
            {
                GlobalProfilePL2Text.Visibility = pl2Visibility;
                if (snapGlobal != null) GlobalProfilePL2Text.Text = $"{snapGlobal.EffectiveTDPBoostFPPTWatts(onBattery)}W";
            }

            GlobalProfileFPSLimitLabel.Visibility = snapFpsVis;
            GlobalProfileFPSLimitText.Visibility = snapFpsVis;
            GlobalProfileFPSLimitText.Text = GetFpsValueLabel(snapGlobal, onBattery);


            // Helper-owned since 35ee315 — the widget copy is frozen and always read "Balanced".
            var snapGlobalPowerVis = (snapReady && snapGlobal != null) ? powerModeVisibility : Visibility.Collapsed;
            GlobalProfilePowerModeLabel.Visibility = snapGlobalPowerVis;
            GlobalProfilePowerModeText.Visibility = snapGlobalPowerVis;
            if (snapGlobal != null)
            {
                int? globalPowerMode = snapGlobal.EffectiveOSPowerMode(onBattery);
                GlobalProfilePowerModeText.Text = globalPowerMode.HasValue
                    ? GetPowerModeShortName(globalPowerMode.Value) : "-";
            }

            GlobalProfileAMDLabel.Visibility = amdVisibility;
            GlobalProfileAMDText.Visibility = amdVisibility;
            var globalAmdFeatures = GetAMDFeaturesShortString(globalProfile);
            GlobalProfileAMDText.Text = string.IsNullOrEmpty(globalAmdFeatures) ? "Off" : globalAmdFeatures;


            // §5.5: the display mode comes from the helper's store like every other applied value.
            var snapResolutionVis = (snapReady && snapGlobal != null) ? resolutionVisibility : Visibility.Collapsed;
            GlobalProfileResolutionLabel.Visibility = snapResolutionVis;
            GlobalProfileResolutionText.Visibility = snapResolutionVis;
            GlobalProfileResolutionText.Text = ResolutionCardLabel(snapGlobal?.Resolution);


            // CPU advanced (ToothNClaw port) summary row — only shown when something is set.
            string globalCpuAdv = BuildCpuAdvancedSummary(snapGlobal);
            var globalCpuAdvVis = string.IsNullOrEmpty(globalCpuAdv) ? Visibility.Collapsed : Visibility.Visible;
            GlobalProfileCpuAdvLabel.Visibility = globalCpuAdvVis;
            GlobalProfileCpuAdvText.Visibility = globalCpuAdvVis;
            GlobalProfileCpuAdvText.Text = globalCpuAdv ?? "";

            // Intel Display (IGCL) summary row — part of the Performance & Display profile.
            string globalDisp = BuildDisplaySummary(snapGlobal);
            var globalDispVis = string.IsNullOrEmpty(globalDisp) ? Visibility.Collapsed : Visibility.Visible;
            GlobalProfileDisplayLabel.Visibility = globalDispVis;
            GlobalProfileDisplayText.Visibility = globalDispVis;
            GlobalProfileDisplayText.Text = globalDisp ?? "";

            // Update AC/DC profile display
            ACDCProfileTDPModeLabel.Visibility = tdpModeVisibility;
            ACProfileTDPModeText.Visibility = tdpModeVisibility;
            DCProfileTDPModeText.Visibility = tdpModeVisibility;
            // Plan §5.3 + §7.1: the AC and DC columns are two VIEWS of one helper profile now, resolved
            // through GameProfile.Effective*(onBattery), instead of two separate widget containers.
            // TDP Mode itself has no DC override, so both columns read the same value — as before.
            ACProfileTDPModeText.Text = GetProfileTDPModeName(acProfile.TDPModeIndex,
                snapGlobal?.LegionPerformanceMode ?? LegionModeFallback);
            DCProfileTDPModeText.Text = GetProfileTDPModeName(dcProfile.TDPModeIndex,
                snapGlobal?.LegionPerformanceMode ?? LegionModeFallback);

            ACDCProfileTDPLabel.Visibility = tdpVisibility;
            ACProfileTDPText.Visibility = tdpVisibility;
            DCProfileTDPText.Visibility = tdpVisibility;
            // One helper profile, two power-source views (plan §7.1).
            if (snapGlobal != null)
            {
                ACProfileTDPText.Text = $"{snapGlobal.EffectiveTDP(false)}W";
                DCProfileTDPText.Text = $"{snapGlobal.EffectiveTDP(true)}W";
            }

            ACDCProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
            ACProfileCPUBoostText.Visibility = cpuBoostVisibility;
            DCProfileCPUBoostText.Visibility = cpuBoostVisibility;
            if (snapGlobal != null)
            {
                ACProfileCPUBoostText.Text = snapGlobal.EffectiveCPUBoost(false) ? "On" : "Off";
                DCProfileCPUBoostText.Text = snapGlobal.EffectiveCPUBoost(true) ? "On" : "Off";
            }

            // Repurposed: CPU EPP slot → FPS Mode. Per side: the mode has its own override now, so
            // "Intel plugged in / RTSS on battery" is a real configuration and must render as one.
            ACDCProfileCPUEPPLabel.Text = "FPS Mode";
            ACDCProfileCPUEPPLabel.Visibility = fpsLimitVisibility;
            ACProfileCPUEPPText.Visibility = fpsLimitVisibility;
            DCProfileCPUEPPText.Visibility = fpsLimitVisibility;
            ACProfileCPUEPPText.Text = GetFpsCapModeLabel(snapGlobal, onBattery: false);
            DCProfileCPUEPPText.Text = GetFpsCapModeLabel(snapGlobal, onBattery: true);

            // Repurposed: CPU State slot → TDP Overboost
            ACDCProfileCPUStateLabel.Text = "TDP Overboost";
            ACDCProfileCPUStateLabel.Visibility = tdpVisibility;
            ACProfileCPUStateText.Visibility = tdpVisibility;
            DCProfileCPUStateText.Visibility = tdpVisibility;
            // Overboost DOES have a per-power-state override since 2026-08-02, so the two columns can
            // differ. Both used to print the raw field, which after the direction change is the
            // UNPLUGGED value — a global profile with Overboost on while plugged in therefore showed
            // "Off / Off" (reported 2026-08-02).
            bool acGlobalBoost = snapGlobal?.EffectiveTDPBoostEnabled(false) == true;
            bool dcGlobalBoost = snapGlobal?.EffectiveTDPBoostEnabled(true) == true;
            ACProfileCPUStateText.Text = acGlobalBoost ? "On" : "Off";
            DCProfileCPUStateText.Text = dcGlobalBoost ? "On" : "Off";

            // PL2 sub-row: shown when EITHER side has Overboost on, so the column that has it off still
            // lines up. That side prints "-" rather than a watt figure it does not use.
            var acdcPl2Vis = ((acGlobalBoost || dcGlobalBoost) && tdpVisibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
            ACDCProfilePL2Label.Visibility = acdcPl2Vis;
            ACProfilePL2Text.Visibility = acdcPl2Vis;
            DCProfilePL2Text.Visibility = acdcPl2Vis;
            ACProfilePL2Text.Text = acGlobalBoost ? $"{snapGlobal?.EffectiveTDPBoostFPPTWatts(false)}W" : "-";
            DCProfilePL2Text.Text = dcGlobalBoost ? $"{snapGlobal?.EffectiveTDPBoostFPPTWatts(true)}W" : "-";

            // Gyro — one value for both power states (the gyro lives in the controller profile, which
            // has no power-state split). Present so the two cards carry the same rows.
            string globalGyroText = ((legionGyroTarget?.Value ?? 0) != 0) ? "On" : "Off";
            ACDCProfileGyroLabel.Visibility = Visibility.Visible;
            ACProfileGyroText.Visibility = Visibility.Visible;
            DCProfileGyroText.Visibility = Visibility.Visible;
            ACProfileGyroText.Text = globalGyroText;
            DCProfileGyroText.Text = globalGyroText;

            ACDCProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
            ACProfileFPSLimitText.Visibility = fpsLimitVisibility;
            DCProfileFPSLimitText.Visibility = fpsLimitVisibility;
            ACProfileFPSLimitText.Text = GetFpsValueLabel(snapGlobal, onBattery: false);
            DCProfileFPSLimitText.Text = GetFpsValueLabel(snapGlobal, onBattery: true);


            // Power mode from the HELPER's profile, per power source. It read the two widget containers
            // before, which is why these columns said "Balanced" no matter what the OS was set to —
            // the same defect already fixed for the single cards. Nullable: "profile configures no
            // mode" prints "-" rather than inventing Balanced.
            var snapPowerVis = (snapReady && snapGlobal != null) ? powerModeVisibility : Visibility.Collapsed;
            ACDCProfilePowerModeLabel.Visibility = snapPowerVis;
            ACProfilePowerModeText.Visibility = snapPowerVis;
            DCProfilePowerModeText.Visibility = snapPowerVis;
            if (snapGlobal != null)
            {
                int? acPowerMode = snapGlobal.EffectiveOSPowerMode(onBattery: false);
                int? dcPowerMode = snapGlobal.EffectiveOSPowerMode(onBattery: true);
                ACProfilePowerModeText.Text = acPowerMode.HasValue ? GetPowerModeShortName(acPowerMode.Value) : "-";
                DCProfilePowerModeText.Text = dcPowerMode.HasValue ? GetPowerModeShortName(dcPowerMode.Value) : "-";
            }

            ACDCProfileAMDLabel.Visibility = amdVisibility;
            ACProfileAMDText.Visibility = amdVisibility;
            DCProfileAMDText.Visibility = amdVisibility;
            var acAmdFeatures = GetAMDFeaturesShortString(acProfile);
            var dcAmdFeatures = GetAMDFeaturesShortString(dcProfile);
            ACProfileAMDText.Text = string.IsNullOrEmpty(acAmdFeatures) ? "Off" : acAmdFeatures;
            DCProfileAMDText.Text = string.IsNullOrEmpty(dcAmdFeatures) ? "Off" : dcAmdFeatures;


            ACDCProfileResolutionLabel.Visibility = snapResolutionVis;
            ACProfileResolutionText.Visibility = snapResolutionVis;
            DCProfileResolutionText.Visibility = snapResolutionVis;
            // No DC override for the display mode (user decision 2026-08-01) — both columns show the
            // one stored value, same as TDP Overboost above.
            string globalResolutionText = ResolutionCardLabel(snapGlobal?.Resolution);
            ACProfileResolutionText.Text = globalResolutionText;
            DCProfileResolutionText.Text = globalResolutionText;


            // Update game profile display (if game is running)
            if (HasValidGame(currentGameName))
            {
                if (GetPerGamePowerSourceProfileEnabled(currentGameName))
                {
                    // Show AC/DC game profiles - TDP Mode (Legion only)
                    GameACDCProfileTDPModeLabel.Visibility = tdpModeVisibility;
                    GameACProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameDCProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameACProfileTDPModeText.Text = GetProfileTDPModeName(gameACProfile.TDPModeIndex,
                        snapGame?.LegionPerformanceMode ?? LegionModeFallback);
                    GameDCProfileTDPModeText.Text = GetProfileTDPModeName(gameDCProfile.TDPModeIndex,
                        snapGame?.LegionPerformanceMode ?? LegionModeFallback);

                    // TDP
                    GameACDCProfileTDPLabel.Visibility = tdpVisibility;
                    GameACProfileTDPText.Visibility = tdpVisibility;
                    GameDCProfileTDPText.Visibility = tdpVisibility;
                    if (snapGame != null)
                    {
                        GameACProfileTDPText.Text = $"{snapGame.EffectiveTDP(false)}W";
                        GameDCProfileTDPText.Text = $"{snapGame.EffectiveTDP(true)}W";
                    }

                    // CPU Boost
                    GameACDCProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
                    GameACProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    GameDCProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    if (snapGame != null)
                    {
                        GameACProfileCPUBoostText.Text = snapGame.EffectiveCPUBoost(false) ? "On" : "Off";
                        GameDCProfileCPUBoostText.Text = snapGame.EffectiveCPUBoost(true) ? "On" : "Off";
                    }

                    // The CPU EPP and CPU State slots used to be filled from the widget copies here.
                    // Those writes were dead: both slots are REPURPOSED further down (EPP → Gyro,
                    // State → FPS Mode) and rewritten, text and visibility, before anything renders.
                    // Removed with plan §5.4 rather than migrated — there is nothing to migrate.

                    // FPS Limit
                    GameACDCProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
                    GameACProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameDCProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameACProfileFPSLimitText.Text = GetFpsValueLabel(snapGame, onBattery: false);
                    GameDCProfileFPSLimitText.Text = GetFpsValueLabel(snapGame, onBattery: true);


                    // Power Mode — helper-owned, per power source, nullable (see the global card).
                    var snapGamePowerVis = (snapReady && snapGame != null) ? powerModeVisibility : Visibility.Collapsed;
                    GameACDCProfilePowerModeLabel.Visibility = snapGamePowerVis;
                    GameACProfilePowerModeText.Visibility = snapGamePowerVis;
                    GameDCProfilePowerModeText.Visibility = snapGamePowerVis;
                    if (snapGame != null)
                    {
                        int? gameACPowerMode = snapGame.EffectiveOSPowerMode(onBattery: false);
                        int? gameDCPowerMode = snapGame.EffectiveOSPowerMode(onBattery: true);
                        GameACProfilePowerModeText.Text = gameACPowerMode.HasValue ? GetPowerModeShortName(gameACPowerMode.Value) : "-";
                        GameDCProfilePowerModeText.Text = gameDCPowerMode.HasValue ? GetPowerModeShortName(gameDCPowerMode.Value) : "-";
                    }

                    // AMD Features
                    GameACDCProfileAMDLabel.Visibility = amdVisibility;
                    GameACProfileAMDText.Visibility = amdVisibility;
                    GameDCProfileAMDText.Visibility = amdVisibility;
                    var gameACAmdFeatures = GetAMDFeaturesShortString(gameACProfile);
                    var gameDCAmdFeatures = GetAMDFeaturesShortString(gameDCProfile);
                    GameACProfileAMDText.Text = string.IsNullOrEmpty(gameACAmdFeatures) ? "Off" : gameACAmdFeatures;
                    GameDCProfileAMDText.Text = string.IsNullOrEmpty(gameDCAmdFeatures) ? "Off" : gameDCAmdFeatures;


                    // Resolution — helper-owned (§5.5), no DC override, so both columns show one value
                    var snapGameResolutionVis = (snapReady && snapGame != null) ? resolutionVisibility : Visibility.Collapsed;
                    string gameResolutionText = ResolutionCardLabel(snapGame?.Resolution);
                    GameACDCProfileResolutionLabel.Visibility = snapGameResolutionVis;
                    GameACProfileResolutionText.Visibility = snapGameResolutionVis;
                    GameDCProfileResolutionText.Visibility = snapGameResolutionVis;
                    GameACProfileResolutionText.Text = gameResolutionText;
                    GameDCProfileResolutionText.Text = gameResolutionText;

                    // TDP Overboost + PL2 — mirrors the global card's rows exactly.
                    bool acGameBoost = snapGame?.EffectiveTDPBoostEnabled(false) == true;
                    bool dcGameBoost = snapGame?.EffectiveTDPBoostEnabled(true) == true;
                    GameACDCProfileOverboostLabel.Visibility = tdpVisibility;
                    GameACProfileOverboostText.Visibility = tdpVisibility;
                    GameDCProfileOverboostText.Visibility = tdpVisibility;
                    GameACProfileOverboostText.Text = acGameBoost ? "On" : "Off";
                    GameDCProfileOverboostText.Text = dcGameBoost ? "On" : "Off";

                    var gameAcdcPl2Vis = ((acGameBoost || dcGameBoost) && tdpVisibility == Visibility.Visible)
                        ? Visibility.Visible : Visibility.Collapsed;
                    GameACDCProfilePL2Label.Visibility = gameAcdcPl2Vis;
                    GameACProfilePL2Text.Visibility = gameAcdcPl2Vis;
                    GameDCProfilePL2Text.Visibility = gameAcdcPl2Vis;
                    GameACProfilePL2Text.Text = acGameBoost ? $"{snapGame?.EffectiveTDPBoostFPPTWatts(false)}W" : "-";
                    GameDCProfilePL2Text.Text = dcGameBoost ? $"{snapGame?.EffectiveTDPBoostFPPTWatts(true)}W" : "-";

                    // MSI Claw: repurpose CPUEPPLabel/Text slot → Gyro On/Off (same value for AC/DC)
                    bool gyroOnACDC = (legionGyroTarget?.Value ?? 0) != 0;
                    string gyroText = gyroOnACDC ? "On" : "Off";
                    GameACDCProfileCPUEPPLabel.Text = "Gyro";
                    GameACDCProfileCPUEPPLabel.Visibility = Visibility.Visible;
                    GameACProfileCPUEPPText.Text = gyroText;
                    GameACProfileCPUEPPText.Visibility = Visibility.Visible;
                    GameDCProfileCPUEPPText.Text = gyroText;
                    GameDCProfileCPUEPPText.Visibility = Visibility.Visible;

                    // MSI Claw: repurpose CPUStateLabel/Text slot → FPS Limiter Mode.
                    // From the helper's profile: FPSLimitEnabled does not exist there, its encoding is
                    // a cap > 0 — which is exactly what GetFpsValueLabel already resolves per side.
                    // Always shown, and resolved PER SIDE. Two things were wrong before: the row was
                    // hidden entirely when no cap was active, which left a gap the global card did not
                    // have, and the mode came from the raw FpsCapMode — which since 2026-08-02 is the
                    // unplugged value, so a profile capping with Intel plugged in and RTSS on battery
                    // printed the same word twice.
                    GameACDCProfileCPUStateLabel.Text = "FPS Mode";
                    GameACDCProfileCPUStateLabel.Visibility = Visibility.Visible;
                    GameACProfileCPUStateText.Visibility = Visibility.Visible;
                    GameDCProfileCPUStateText.Visibility = Visibility.Visible;
                    GameACProfileCPUStateText.Text = (snapGame?.EffectiveFpsCapMode(false) == 1) ? "Intel" : "RTSS";
                    GameDCProfileCPUStateText.Text = (snapGame?.EffectiveFpsCapMode(true) == 1) ? "Intel" : "RTSS";

                    // The rows this grid does not declare in XAML — Intel channels, frame generation, VRR
                    // and the fan. Without them the split view of a running game was missing what the saved
                    // card for the same game showed, which is exactly the comparison being made while
                    // tweaking (user, 2026-08-04). The fan is per-power-state here, so the two columns can
                    // genuinely differ; globalFanCurve stays null because a game card must show the game's
                    // own override or nothing.
                    RenderSplitExtraRows(GameProfileACDC,
                        BuildSplitExtraPairs(snapGame, onBattery: false, globalFanCurve: null),
                        BuildSplitExtraPairs(snapGame, onBattery: true,  globalFanCurve: null));
                }
                else
                {
                    // Multi-column rendering of the Now Playing profile (matches the saved cards).
                    if (GameProfilePairs != null)
                    {
                        GameProfilePairs.Children.Clear();
                        GameProfilePairs.Children.Add(RenderPairsGrid(
                            BuildProfileCardPairs(gameProfile, snapGame, onBattery)));
                    }

                    // Show single game profile - TDP Mode (Legion only)
                    GameProfileTDPModeLabel.Visibility = tdpModeVisibility;
                    GameProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameProfileTDPModeText.Text = GetProfileTDPModeName(gameProfile.TDPModeIndex,
                        snapGame?.LegionPerformanceMode ?? LegionModeFallback);

                    // TDP — from the helper's profile for THIS game (plan §5.3). Hidden while no
                    // snapshot has arrived, rather than showing 0 W (plan §6).
                    var snapGameTdpVis = (snapReady && snapGame != null) ? tdpVisibility : Visibility.Collapsed;
                    GameProfileTDPLabel.Visibility = snapGameTdpVis;
                    GameProfileTDPText.Visibility = snapGameTdpVis;
                    if (snapGame != null) GameProfileTDPText.Text = $"{snapGame.EffectiveTDP(onBattery)}W";

                    // TDP Overboost (saved with TDP)
                    GameProfileTDPBoostLabel.Text = "TDP Overboost";
                    GameProfileTDPBoostLabel.Visibility = snapGameTdpVis;
                    GameProfileTDPBoostText.Visibility = snapGameTdpVis;
                    GameProfileTDPBoostText.Text = (snapGame?.EffectiveTDPBoostEnabled(onBattery) == true) ? "On" : "Off";

                    // PL2 value — sub-row when Overboost is on
                    var gamePl2Visibility = (snapGame?.EffectiveTDPBoostEnabled(onBattery) == true && snapGameTdpVis == Visibility.Visible)
                        ? Visibility.Visible : Visibility.Collapsed;
                    if (GameProfilePL2Label != null) GameProfilePL2Label.Visibility = gamePl2Visibility;
                    if (GameProfilePL2Text  != null)
                    {
                        GameProfilePL2Text.Visibility = gamePl2Visibility;
                        if (snapGame != null) GameProfilePL2Text.Text = $"{snapGame.EffectiveTDPBoostFPPTWatts(onBattery)}W";
                    }

                    // CPU Boost
                    var snapGameBoostVis = (snapReady && snapGame != null) ? cpuBoostVisibility : Visibility.Collapsed;
                    GameProfileCPUBoostLabel.Visibility = snapGameBoostVis;
                    GameProfileCPUBoostText.Visibility = snapGameBoostVis;
                    if (snapGame != null)
                        GameProfileCPUBoostText.Text = snapGame.EffectiveCPUBoost(onBattery) ? "On" : "Off";

                    // CPU EPP and CPU State slots are repurposed below (Gyro / FPS Mode) and rewritten
                    // there, text and visibility. The assignments that stood here read the widget copy
                    // and never reached the screen — removed with plan §5.4, same as in the AC/DC branch.

                    // FPS Limit — use GetFpsValueLabel so Intel tier is shown correctly
                    GameProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
                    GameProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameProfileFPSLimitText.Text = GetFpsValueLabel(snapGame, onBattery);


                    // Power Mode — helper-owned and nullable, like every other card.
                    var snapGamePowerModeVis = (snapReady && snapGame != null) ? powerModeVisibility : Visibility.Collapsed;
                    GameProfilePowerModeLabel.Visibility = snapGamePowerModeVis;
                    GameProfilePowerModeText.Visibility = snapGamePowerModeVis;
                    if (snapGame != null)
                    {
                        int? gamePowerMode = snapGame.EffectiveOSPowerMode(onBattery);
                        GameProfilePowerModeText.Text = gamePowerMode.HasValue
                            ? GetPowerModeShortName(gamePowerMode.Value) : "-";
                    }

                    // AMD Features
                    GameProfileAMDLabel.Visibility = amdVisibility;
                    GameProfileAMDText.Visibility = amdVisibility;
                    var gameAmdFeatures = GetAMDFeaturesShortString(gameProfile);
                    GameProfileAMDText.Text = string.IsNullOrEmpty(gameAmdFeatures) ? "Off" : gameAmdFeatures;


                    // Resolution — helper-owned (§5.5)
                    var snapGameResVis = (snapReady && snapGame != null) ? resolutionVisibility : Visibility.Collapsed;
                    GameProfileResolutionLabel.Visibility = snapGameResVis;
                    GameProfileResolutionText.Visibility = snapGameResVis;
                    GameProfileResolutionText.Text = ResolutionCardLabel(snapGame?.Resolution);

                    // Sticky TDP

                    // CPU advanced (ToothNClaw port) summary row — only shown when set.
                    string gameCpuAdv = BuildCpuAdvancedSummary(snapGame);
                    var gameCpuAdvVis = string.IsNullOrEmpty(gameCpuAdv) ? Visibility.Collapsed : Visibility.Visible;
                    GameProfileCpuAdvLabel.Visibility = gameCpuAdvVis;
                    GameProfileCpuAdvText.Visibility = gameCpuAdvVis;
                    GameProfileCpuAdvText.Text = gameCpuAdv ?? "";

                    // Intel Display (IGCL) summary row
                    string gameDisp = BuildDisplaySummary(snapGame);
                    var gameDispVis = string.IsNullOrEmpty(gameDisp) ? Visibility.Collapsed : Visibility.Visible;
                    GameProfileDisplayLabel.Visibility = gameDispVis;
                    GameProfileDisplayText.Visibility = gameDispVis;
                    GameProfileDisplayText.Text = gameDisp ?? "";

                    // MSI Claw: repurpose CPUEPPLabel/Text slot → Gyro On/Off
                    bool gyroOn = (legionGyroTarget?.Value ?? 0) != 0;
                    GameProfileCPUEPPLabel.Text = "Gyro";
                    GameProfileCPUEPPLabel.Visibility = Visibility.Visible;
                    GameProfileCPUEPPText.Text = gyroOn ? "On" : "Off";
                    GameProfileCPUEPPText.Visibility = Visibility.Visible;

                    // MSI Claw: repurpose CPUStateLabel/Text slot → FPS Limiter Mode (only when a cap is
                    // active). Both facts now come from THIS game's helper profile: whether a cap exists
                    // (the helper encodes that as cap > 0, which GetFpsValueLabel resolves) and which
                    // limiter it uses. The mode used to be read from the LIVE fpsCapMode property, so a
                    // card describing a saved profile could name whatever limiter was last active.
                    if (GetFpsValueLabel(snapGame, onBattery) != "Off")
                    {
                        GameProfileCPUStateLabel.Text = "FPS Mode";
                        GameProfileCPUStateLabel.Visibility = Visibility.Visible;
                        GameProfileCPUStateText.Text = (snapGame?.FpsCapMode == 1) ? "Intel" : "RTSS";
                        GameProfileCPUStateText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        GameProfileCPUStateLabel.Visibility = Visibility.Collapsed;
                        GameProfileCPUStateText.Visibility = Visibility.Collapsed;
                    }

                }
            }

            // Last, once every row above has its final value and visibility: fold the rows both power
            // states agree on out of the tables and into the compact pairs grids beneath them. Running
            // this earlier would read values that are still being written.
            CollapseIdenticalSplitRows(GlobalProfileACDC, GlobalProfileACDCShared);
            CollapseIdenticalSplitRows(GameProfileACDC, GameProfileACDCShared);

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

        /// <summary>
        /// Short form of a "WxH" resolution for the narrow profile cards and the notification:
        /// 1920x1200 → "1200p", 1280x720 → "720p". The stored value keeps its full "WxH" form —
        /// this is display only, see the note on CuratedResolutions about why the value must not
        /// carry a label.
        /// </summary>
        /// <summary>
        /// The "Res" row of a profile card: the profile's stored resolution, or — when it stores none —
        /// the one currently in effect.
        ///
        /// Both cards used to print the word "Native" for an empty field, which is literally correct
        /// (nothing stored means the display keeps whatever it has) but told the user nothing, and it
        /// made the two cards look inconsistent: a per-game profile that captured its resolution showed
        /// "1920x1200" while the global one showed "Native" (reported 2026-08-02). The live value is the
        /// honest answer to the question the card actually asks — what resolution applies in this state.
        ///
        /// "Native" survives only for the case where neither is known, e.g. before the helper has pushed
        /// the current mode.
        /// </summary>
        private string ResolutionCardLabel(string stored)
        {
            if (!string.IsNullOrEmpty(stored)) return stored;
            string live = resolution?.Value;
            return string.IsNullOrEmpty(live) ? "Native" : live;
        }

        private static string GetResolutionShortLabel(string resolution)
        {
            if (string.IsNullOrEmpty(resolution)) return null;
            var parts = resolution.Split('x');
            return (parts.Length == 2 && int.TryParse(parts[1], out int height))
                ? $"{height}p"
                : resolution;
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
                // Intel mode — the value IS the fps. The switch over 1/2/3 that used to sit here
                // returned "Off" for every real cap; MigrateIntelFps still maps legacy stored tiers.
                int fps = MigrateIntelFps(intelFpsTier?.Value ?? 0);
                return fps > 0 ? $"{fps}" : "Off";
            }
            // RTSS mode — use toggle + slider
            return FPSLimitToggle?.IsOn == true
                ? $"{(int)(FPSLimitSlider?.Value ?? 0)}"
                : "Off";
        }

        /// <summary>
        /// Returns a short label for the active FPS cap mode (RTSS / Intel + fps).
        /// Reads the global fpsCapMode and intelFpsTier widget properties (live state).
        /// </summary>
        private string GetFpsCapModeLabel()
        {
            if (fpsCapMode?.Value == 1)
            {
                int fps = MigrateIntelFps(intelFpsTier?.Value ?? 0);
                return fps > 0 ? $"Intel {fps}" : "Intel";
            }
            return "RTSS";
        }

        /// <summary>
        /// Returns the FPS value label for a saved PerformanceProfile.
        /// Mode-aware: shows the Intel tier fps when Intel mode is saved,
        /// not the stale RTSS slider value.
        /// </summary>
        /// <summary>
        /// The effective FPS cap of a HELPER profile (plan §5.3), for the given power source.
        ///
        /// TWO encoding differences to the widget's old copy, both verified against real profile files —
        /// do not "simplify" this back into the old switch:
        ///  - IntelFpsTier: the widget stored a TIER INDEX (0=Off,1=P60,2=B40,3=E30), the helper stores
        ///    the actual FPS value (re2.xml holds 60). So the number is printed directly; a tier switch
        ///    would map 60 to "Off".
        ///  - FPSLimitEnabled does not exist in the helper's profile. Its encoding is FPSLimit &gt; 0,
        ///    and only the RTSS limit has a plugged-in override (EffectiveFPSLimit).
        /// </summary>
        private static string GetFpsValueLabel(Shared.Data.GameProfile profile, bool onBattery)
        {
            if (profile == null) return "Off";
            // Mode and Intel cap resolve per power state as well since 2026-08-02 — reading the base
            // values here would have labelled the battery column with the mains limiter.
            if (profile.EffectiveFpsCapMode(onBattery) == 1)
            {
                int tier = profile.EffectiveIntelFpsTier(onBattery);
                return tier > 0 ? $"{tier}" : "Off";
            }

            int rtssLimit = profile.EffectiveFPSLimit(onBattery);
            return rtssLimit > 0 ? $"{rtssLimit}" : "Off";
        }

        /// <summary>
        /// Reads the FPS cap mode from a HELPER profile (plan §5.3), so each card shows the settings
        /// stored for THAT profile rather than the current live state.
        /// Same IntelFpsTier caveat as GetFpsValueLabel: the value is the actual FPS, not a tier index.
        ///
        /// Resolved per power state — both the mode and the Intel cap have an override since
        /// 2026-08-02, so the two columns of a split profile can legitimately differ.
        /// </summary>
        private string GetFpsCapModeLabel(Shared.Data.GameProfile profile, bool onBattery)
        {
            if (profile == null) return "RTSS";
            if (profile.EffectiveFpsCapMode(onBattery) == 1)
            {
                int tier = profile.EffectiveIntelFpsTier(onBattery);
                return tier > 0 ? $"Intel {tier}" : "Intel";
            }
            return "RTSS";
        }

        /// <summary>
        /// Gets the TDP mode display name, accounting for custom presets.
        ///
        /// Takes the two values SEPARATELY on purpose (plan §5.3): they come from different owners and
        /// passing one object would hide that again. <paramref name="tdpModeIndex"/> is group C — the
        /// selection index of the widget's own preset ComboBox, which has no hardware counterpart.
        /// <paramref name="legionPerformanceMode"/> is group A and comes from the helper's profile,
        /// where it is nullable (never captured) — callers pass the fallback they want for that case.
        /// </summary>
        private string GetProfileTDPModeName(int tdpModeIndex, int legionPerformanceMode)
        {
            // If TDPModeIndex is set and we have custom presets, use the preset name
            if (tdpModeIndex >= 0 && useCustomTDPPresets && tdpPresets != null)
            {
                if (tdpModeIndex < tdpPresets.Count)
                {
                    return tdpPresets[tdpModeIndex].Name;
                }
                else if (tdpModeIndex == tdpPresets.Count)
                {
                    return "Slider"; // The actual Slider mode after all presets
                }
            }
            // Fall back to legacy mode name
            return GetLegionModeShortName(legionPerformanceMode);
        }

        /// <summary>
        /// Gets the TDPModeComboBox index from a profile, accounting for custom presets.
        /// Returns the index to use for TDPModeComboBox.SelectedIndex.
        /// </summary>
        private int GetProfileTDPModeIndex(PerformanceProfile profile)
        {
            // ROOT CAUSE of the "widget crashes when a game ends" loop: the value returned here
            // drives TDPModeComboBox.SelectedIndex, so it MUST be valid for the combo's CURRENT
            // items. TDPModeComboBox is rebuilt when custom presets toggle (see TdpCustomPresets),
            // and a profile can hold a TDPModeIndex saved under a *different* preset configuration.
            // This used to validate the saved index against tdpPresets.Count / a hardcoded 5 —
            // NOT against the live combo — so a stale index slipped through and
            // "TDPModeComboBox.SelectedIndex = modeIndex" threw ArgumentException, which (inside a
            // DependencyProperty callback) fail-fasted the whole Game Bar widget. Validate against
            // the actual combo and clamp the result so an out-of-range index can never escape.
            int comboCount = TDPModeComboBox?.Items.Count ?? 0;
            int result;

            // Use the saved index only if it's valid for the combo as it stands right now.
            if (profile.TDPModeIndex >= 0 && (comboCount == 0 || profile.TDPModeIndex < comboCount))
            {
                result = profile.TDPModeIndex;
            }
            else
            {
                // No usable saved index — fall back to the first entry.
                //
                // What stood here was a watt-matching search: take the profile's stored TDP and find
                // the preset with the same wattage, so a user on "Max (30 W)" would not be reset to
                // "Standard (25 W)". That search was the LAST reader of the widget profile copy's TDP,
                // and it is pointless on this device (user, 2026-08-02): every TDP value is set with
                // the slider, the named stages are not a thing here, and the Legion branch below it
                // mapped Quiet/Balanced/Performance — modes the Claw never leaves 255 for.
                result = 0;
            }

            // Final safety clamp: never return an index outside the live combo, regardless of any
            // preset-config desync — this is what guarantees the SelectedIndex set can't throw.
            if (comboCount > 0)
                result = Math.Max(0, Math.Min(result, comboCount - 1));
            return result;
        }

    }
}
