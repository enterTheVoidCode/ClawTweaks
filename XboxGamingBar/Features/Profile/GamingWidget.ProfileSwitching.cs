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

        private void SwitchProfile()
        {
            string targetProfile = GetTargetProfileName();

            if (targetProfile != currentProfileName)
            {
                Logger.Info($"Switching from '{currentProfileName}' to '{targetProfile}' profile");

                // Set flag to prevent auto-saves during transition
                isSwitchingProfile = true;

                try
                {
                    // Save current profile before switching, but SKIP for game-related transitions.
                    // 1. FROM a game profile (game close): helper already pushed global values to the
                    //    widget UI (AutoTDP=false, Mode=Quiet, etc.) BEFORE sending PerGameProfile=false.
                    //    Saving now would capture global values and corrupt the game profile.
                    // 2. TO a game profile (game open): helper sends game values (Mode=Custom, AutoTDP=true)
                    //    BEFORE the profile switch. Saving now would capture game values and corrupt Global.
                    // Individual toggle/slider handlers already save user changes immediately,
                    // so skipping here is safe — the profile is always up-to-date.
                    if (!currentProfileName.StartsWith("Game_") && !targetProfile.StartsWith("Game_"))
                    {
                        SaveWidgetUiStateToProfile(currentProfileName);
                    }

                    // Switch to new profile
                    currentProfileName = targetProfile;

                    // Load settings from new profile (explicit switch - apply HDR/Resolution)
                    LoadProfileSettings(currentProfileName, isExplicitSwitch: true);
                }
                finally
                {
                    // Always clear the flag
                    isSwitchingProfile = false;
                }
            }
        }

        private string GetTargetProfileName()
        {
            bool hasGame = HasValidGame(currentGameName);
            bool perGameEnabled = PerGameProfileToggle?.IsOn ?? false;

            // Only consider DC (battery) when power supply is NotPresent (actually unplugged)
            // Inadequate means charger is connected but can't keep up - still treat as AC
            var powerSupplyStatus = PowerManager.PowerSupplyStatus;
            bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;

            // IMPORTANT: Never create profile names for invalid games
            // If per-game is enabled but no valid game, fall back to global profiles
            if (perGameEnabled && hasGame)
            {
                // Per-game profile - only if we have a VALID game name AND the profile
                // storage container already exists. This prevents switching to ghost profiles
                // for fuzzy-matched launcher names that were never explicitly created by the user
                // or LoadOrCreateGameProfiles(). Without this check, deferred events after
                // SwitchProfile can auto-save to a non-existent profile, creating it accidentally.
                var settings = ApplicationData.Current.LocalSettings;
                string candidateProfile;
                bool perGamePowerSourceSplit = GetPerGamePowerSourceProfileEnabled(currentGameName);
                if (perGamePowerSourceSplit)
                {
                    candidateProfile = isOnAC ? $"Game_{currentGameName}_AC" : $"Game_{currentGameName}_DC";
                }
                else
                {
                    candidateProfile = $"Game_{currentGameName}";
                }

                if (settings.Containers.ContainsKey($"Profile_{candidateProfile}"))
                {
                    Logger.Info($"Using per-game profile for: {currentGameName}");
                    return candidateProfile;
                }

                Logger.Warn($"Per-game toggle is ON but no saved profile exists for '{candidateProfile}', using global profile instead");
                // Fall through to global profile below
            }
            else if (perGameEnabled && !hasGame)
            {
                Logger.Warn($"Per-game toggle is ON but no valid game detected, using global profile instead");
            }

            // Global profiles (used when: no valid game, per-game disabled, or game profile doesn't exist yet)
            if (!GetGlobalPowerSourceProfileEnabled())
            {
                return "Global";
            }
            else
            {
                return isOnAC ? "AC" : "DC";
            }
        }

        // tdpEditedByUserForProfile and MarkTdpEditedByUser are GONE (plan §5.4). They existed only
        // because this method wrote the TDP group: the slider is the helper's display surface, so a
        // blind capture persisted every helper push as if the user had dialled it in, and at a profile
        // transition it wrote the new profile's value into the old profile. Now that nothing here
        // writes TDP, there is nothing left to gate — and leaving the marker in place would be dead
        // protective code that a successor would read as active.

        /// <summary>
        /// Persists the widget's own UI state (group C) for a profile. Named for what it does: this is
        /// NOT where a performance profile is written — the helper owns that store and the widget reads
        /// it through the ProfileSnapshot.
        /// </summary>
        private void SaveWidgetUiStateToProfile(string profileName)
        {
            // Guard against null profile name during XAML initialization
            if (string.IsNullOrEmpty(profileName))
            {
                return;
            }

            // Don't save during helper updates - prevents race conditions
            if (isApplyingHelperUpdate)
            {
                Logger.Debug($"Skipping profile save for {profileName} - isApplyingHelperUpdate is true");
                return;
            }

            // Don't save during initial sync - prevents stale widget values from overwriting
            // the helper's actual hardware state in the profile
            if (isInitialSync)
            {
                Logger.Debug($"Skipping profile save for {profileName} - isInitialSync is true");
                return;
            }

            // Never save to "No game detected" profile (case-insensitive check)
            if (profileName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Warn($"Attempted to save to invalid profile name: {profileName}, skipping");
                return;
            }

            // Don't auto-save to game profiles that haven't been explicitly created.
            // Only LoadOrCreateGameProfiles() should create new game profile storage containers.
            // Without this guard, deferred UI events after SwitchProfile can accidentally create
            // ghost profiles for fuzzy-matched launcher names (e.g., "Game_Hollow Knight: Silksong").
            if (profileName.StartsWith("Game_"))
            {
                var settings2 = ApplicationData.Current.LocalSettings;
                if (!settings2.Containers.ContainsKey($"Profile_{profileName}"))
                {
                    Logger.Warn($"Skipping auto-save to non-existent game profile '{profileName}' (profile must be created via LoadOrCreateGameProfiles first)");
                    return;
                }
            }

            var profile = GetProfile(profileName);

            // Group C only — the widget's UI state. Everything the helper applies to hardware or the OS
            // (TDP, Overboost/PL2, CPU boost/EPP/state, P-/E-core caps, Intel display and gaming, the FPS
            // cap, OS power mode) is owned and persisted by the helper and reaches the widget through the
            // ProfileSnapshot. Writing a second copy here is what produced the two-store divergences this
            // plan exists to remove — see Doku/PLAN_Performance_SingleStore.md §5.4.
            if (SaveTDP && TDPModeComboBox != null)
            {
                int selectedIndex = TDPModeComboBox.SelectedIndex;
                if (selectedIndex >= 0)
                {
                    // The preset ComboBox selection. Genuinely widget state: it has no counterpart in the
                    // helper, which only knows watts.
                    profile.TDPModeIndex = selectedIndex;

                    // Kept deliberately. This looks like a group-A field but it is DERIVED FROM THE
                    // WIDGET'S OWN ComboBox (GetCurrentPresetLegionMode), not captured from a helper
                    // push — it is effectively a second encoding of TDPModeIndex, and the mode-restore
                    // after a game reads it. Nothing else writes it, so it cannot diverge.
                    profile.LegionPerformanceMode = GetCurrentPresetLegionMode();
                }
            }
            // The MSI Claw fan capture that used to sit here is GONE (2026-08-02). It stamped the fan
            // editor's visible state into this profile on every unrelated save, which is how a per-game
            // curve could end up in the global profile and vice versa. The per-game curve is a helper-side
            // GameProfile field now and is written only when the user presses Apply in the fan card —
            // see the note where CaptureMsiFanIntoProfile used to live.
            if (SaveAMDFeatures && AMDFluidMotionFrameToggle != null)
            {
                profile.FluidMotionFrames = AMDFluidMotionFrameToggle.IsOn;
                profile.RadeonSuperResolution = AMDRadeonSuperResolutionToggle.IsOn;
                profile.RadeonSuperResolutionSharpness = AMDRadeonSuperResolutionSharpnessSlider.Value;
                profile.ImageSharpening = AMDImageSharpeningToggle.IsOn;
                profile.ImageSharpeningSharpness = AMDImageSharpeningSlider.Value;
                profile.RadeonAntiLag = AMDRadeonAntiLagToggle.IsOn;
                profile.RadeonBoost = AMDRadeonBoostToggle.IsOn;
                profile.RadeonBoostResolution = AMDRadeonBoostResolutionSlider.Value;
                profile.RadeonChill = AMDRadeonChillToggle.IsOn;
                profile.RadeonChillMinFPS = AMDRadeonChillMinFPSSlider.Value;
                profile.RadeonChillMaxFPS = AMDRadeonChillMaxFPSSlider.Value;
            }
            // HDR is NOT captured any more: no Claw model supports it (user, 2026-08-01), so the toggle
            // wrote a value nothing could ever apply. Same for CPU affinity at the end of this method.
            // Resolution and refresh rate are gone from here too, with §5.5: the helper owns them now.
            // It persists them from its own Resolution/RefreshRate properties when the change is
            // attributed to the user, and applies them per profile — so a widget copy would only be a
            // second truth to drift.
            // Overlay Level
            if (SaveOverlayLevel && PerformanceOverlayComboBox != null)
            {
                profile.OverlayLevel = PerformanceOverlayComboBox.SelectedIndex;
            }
            // Persist to storage
            Logger.Info($"Saving widget UI state for profile {profileName}: TDP mode index={profile.TDPModeIndex}");
            SaveProfileToStorage(profileName, profile);

            // Update profile display
            UpdateProfileDisplay();
        }

        private void LoadProfileSettings(string profileName, bool isExplicitSwitch = false)
        {
            if (isLoadingProfile) return;
            isLoadingProfile = true;
            profileSwitchEpoch++; // Invalidate any deferred PropertyChanged callbacks queued before this switch

            try
            {
                var profile = GetProfile(profileName);

                // Plan §5.4 step 1: the TDP group is neither read from nor pushed out of the widget's
                // profile copy any more. The helper owns it and applies it itself on BOTH switch
                // directions — per-game in PerGameProfile_PropertyChanged and global in
                // RestoreGlobalProfileSettings, each in the order TDPBoostEnabled → PL2 → TDP →
                // CPUBoost/EPP/CPUState (Program.ProfileHandlers.cs:705-720 and :457-467).
                //
                // The UI follows the helper's property pushes (BatchSync when the widget opens,
                // per-property push on an in-session switch) — the same pattern the Intel Display and
                // CPU-advanced blocks below already use, and for the same measured reason: asserting
                // the widget's stored value here overwrote the helper's just-applied one on every
                // reload (a Game Bar background bounce was enough).
                //
                // This also retires the Legion "switch to Custom first, then send the deferred TDP
                // values" ordering dance: with nothing sent from here there is nothing left to order.
                // savedCustomTDP is no longer seeded from the profile either — it is initialized from
                // the helper's tdp.Value on connect (GamingWidget.xaml.cs:4444, :4886).
                if (SaveCPUBoost)
                {
                    // CPUBoost: same as the TDP group above — the helper applies it from its own store
                    // (powerManager.CPUBoost.SetValue on both switch directions) and pushes the result
                    // back. Neither the toggle assignment nor the send belongs here any more.

                    // CPU advanced (ToothNClaw port): restore combo selections + push to helper.
                    ApplyCpuAdvancedFromProfile(profile);
                    // Intel Display (IGCL): the HELPER owns display state — it applies the active
                    // profile and pushes the values back via the property sync (BatchSync on open,
                    // per-property push on in-session switch). The widget must NOT push its own
                    // (possibly stale) stored values here, or it overrides the user's last setting
                    // on every Game Bar reopen (slider snapped back to a stale value). UI follows
                    // the helper instead.
                }
                // CPUEPP: helper-owned (powerManager.CPUEPP.SetValue from the profile it just switched
                // to). The slider follows the helper's push, so there is nothing to load or send here.
                if (SaveCPUState)
                {
                    // Max/MinCPUState: helper-owned as well. Only the derived enable state of the CPU
                    // Boost toggle is a widget concern, and that reads the live combo values.
                    UpdateCPUBoostEnabledState();
                }
                if (SaveAMDFeatures)
                {
                    // RSR and RIS are mutually exclusive - if both are enabled in profile, prefer RSR
                    bool rsrEnabled = profile.RadeonSuperResolution;
                    bool risEnabled = profile.ImageSharpening;
                    if (rsrEnabled && risEnabled)
                    {
                        Logger.Warn("Profile has both RSR and RIS enabled - disabling RIS (mutually exclusive)");
                        risEnabled = false;
                    }

                    // Chill is mutually exclusive with Anti-Lag and Boost - if Chill is enabled, disable the others
                    bool antiLagEnabled = profile.RadeonAntiLag;
                    bool boostEnabled = profile.RadeonBoost;
                    bool chillEnabled = profile.RadeonChill;
                    if (chillEnabled && (antiLagEnabled || boostEnabled))
                    {
                        Logger.Warn("Profile has Chill with Anti-Lag/Boost enabled - disabling Anti-Lag and Boost (mutually exclusive)");
                        antiLagEnabled = false;
                        boostEnabled = false;
                    }

                    AMDFluidMotionFrameToggle.IsOn = profile.FluidMotionFrames;
                    AMDRadeonSuperResolutionToggle.IsOn = rsrEnabled;
                    AMDRadeonSuperResolutionSharpnessSlider.Value = profile.RadeonSuperResolutionSharpness;
                    AMDImageSharpeningToggle.IsOn = risEnabled;
                    AMDImageSharpeningSlider.Value = profile.ImageSharpeningSharpness;
                    AMDRadeonAntiLagToggle.IsOn = antiLagEnabled;
                    AMDRadeonBoostToggle.IsOn = boostEnabled;
                    AMDRadeonBoostResolutionSlider.Value = profile.RadeonBoostResolution;
                    AMDRadeonChillToggle.IsOn = chillEnabled;
                    AMDRadeonChillMinFPSSlider.Value = profile.RadeonChillMinFPS;
                    AMDRadeonChillMaxFPSSlider.Value = profile.RadeonChillMaxFPS;
                    // Send to helper explicitly using ForceSetValue to ensure AMD driver state is synchronized
                    // even if the cached value appears unchanged (driver state may differ from cache)
                    // Send RIS first (to disable it if needed), then RSR
                    // Send Anti-Lag and Boost first (to disable them if needed), then Chill
                    amdFluidMotionFrameEnabled?.ForceSetValue(profile.FluidMotionFrames);
                    amdImageSharpeningEnabled?.ForceSetValue(risEnabled);
                    amdImageSharpeningSharpness?.ForceSetValue((int)profile.ImageSharpeningSharpness);
                    amdRadeonSuperResolutionEnabled?.ForceSetValue(rsrEnabled);
                    amdRadeonSuperResolutionSharpness?.ForceSetValue((int)profile.RadeonSuperResolutionSharpness);
                    amdRadeonAntiLagEnabled?.ForceSetValue(antiLagEnabled);
                    amdRadeonBoostEnabled?.ForceSetValue(boostEnabled);
                    amdRadeonBoostResolution?.ForceSetValue((int)profile.RadeonBoostResolution);
                    amdRadeonChillEnabled?.ForceSetValue(chillEnabled);
                    amdRadeonChillMinFPSProperty?.ForceSetValue((int)profile.RadeonChillMinFPS);
                    amdRadeonChillMaxFPSProperty?.ForceSetValue((int)profile.RadeonChillMaxFPS);
                }
                if (SaveFPSLimit)
                {
                    // FPS cap (RTSS limit, Intel cap, cap mode): helper-owned. It applies the profile's
                    // state through ApplyFpsLimiterFromProfile — including the mutual exclusion between
                    // the two limiters — on both switch directions, then pushes the result back.
                    //
                    // Sending from here was also the one place the two stores disagreed on ENCODING and
                    // not just on the value: IntelFpsTier is a tier INDEX in the widget's copy
                    // (0=Off, 1=P60, 2=B40, 3=E30) but an actual FPS number in the helper's store, so a
                    // push from here fed a 1..3 into a field the helper reads as frames per second.
                    //
                    // UpdateFPSLimitControls already builds the whole section — toggle, slider bounds,
                    // value, mode radio — from the live helper properties, which is exactly what is
                    // wanted now.
                    UpdateFPSLimitControls();
                }
                // OS power mode moved to the helper on 2026-08-01: GameProfile.OSPowerMode is a real
                // int? now, ApplyOsPowerModeFromProfile applies it on every switch and
                // OSPowerMode_PropertyChanged persists a user change. The block that used to load and
                // push the widget's copy here is gone with it — the ComboBox follows the helper's push
                // like every other group-A control.
                //
                // Resolution and refresh rate below are the only genuinely widget-driven leftovers: no
                // helper-side apply-from-profile exists for them yet, and their isExplicitSwitch gate is
                // what keeps them from clobbering until the send path (§5.5) lands. HDR and CPU affinity
                // are gone entirely — no Claw model has HDR, and CPU affinity is a GoTweaks leftover the
                // product does not use (user, 2026-08-01).
                // Legion Performance Mode handling
                // Skip TDP mode loading when:
                // - Default Game Profile is active (DGP controls TDP)
                // - Initial sync is in progress (let helper's value take precedence - DGP state not yet known)
                Logger.Info($"LoadProfileSettings Legion check: legionGoDetected={legionGoDetected?.Value}, LegionPerformanceModeComboBox={LegionPerformanceModeComboBox != null}, TDPModeComboBox={TDPModeComboBox != null}, isInitialSync={isInitialSync}");
                if (legionGoDetected?.Value == true && LegionPerformanceModeComboBox != null && TDPModeComboBox != null && !isInitialSync)
                {
                    int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom

                    if (profileName.StartsWith("Game_"))
                    {
                        // Loading a game profile: save the source profile's TDP mode (not the current UI state)
                        // This ensures we restore to the intended profile mode when the game closes
                        if (savedLegionPerformanceMode < 0)
                        {
                            // Save from the correct source profile based on Power Source Profile toggle
                            if (GetGlobalPowerSourceProfileEnabled())
                            {
                                var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                                bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;
                                savedLegionPerformanceMode = isOnAC ? acProfile.LegionPerformanceMode : dcProfile.LegionPerformanceMode;
                                Logger.Info($"Saved Legion Performance Mode from {(isOnAC ? "AC" : "DC")} profile: {GetLegionModeShortName(savedLegionPerformanceMode)} ({savedLegionPerformanceMode}) before game profile");
                            }
                            else
                            {
                                savedLegionPerformanceMode = globalProfile.LegionPerformanceMode;
                                Logger.Info($"Saved Legion Performance Mode from global profile: {GetLegionModeShortName(savedLegionPerformanceMode)} ({savedLegionPerformanceMode}) before game profile");
                            }
                        }

                        // Apply game profile's TDP Mode if SaveTDP is enabled
                        if (SaveTDP)
                        {
                            int profileMode = profile.LegionPerformanceMode;
                            int modeIndex = GetProfileTDPModeIndex(profile);

                            // For game profiles, the helper manages LegionPerformanceMode in PerGameProfile_PropertyChanged:
                            // it applies the saved mode from the helper's profile (or Custom for new profiles).
                            // Don't send mode to helper here — that would override the helper's mode and cause
                            // "switches to Custom then immediately back" when profiles have stale/corrupted modes.
                            // Just update lastTDPModeIndex so the handler doesn't treat the helper's mode update as a "change".
                            if (modeIndex >= 0)
                                lastTDPModeIndex = modeIndex;
                            Logger.Info($"Game profile: LegionPerformanceMode deferred to helper. Widget profile has: {GetLegionModeShortName(profileMode)} ({profileMode}) for {profileName}");
                        }
                        else
                        {
                            // SaveTDP disabled: let helper manage mode (it defaults to Custom for new profiles)
                            lastTDPModeIndex = 3; // Custom mode index
                            Logger.Info($"SaveTDP disabled - deferring mode to helper for game profile: {profileName}");
                        }
                    }
                    else if (savedLegionPerformanceMode >= 0)
                    {
                        // Loading Global/AC/DC profile and we have a saved mode to restore
                        int index = Array.IndexOf(modeValues, savedLegionPerformanceMode);

                        // THE TWO COMBOBOXES DO NOT SHARE AN INDEX SPACE. `index` is a Legion-mode index
                        // (Quiet/Balanced/Performance/Custom = 0..3), while TDPModeComboBox is REBUILT AT
                        // RUNTIME by PopulateTdpModeComboBox and holds exactly ONE item ("Slider") on the
                        // MSI Claw — the named presets there are deliberately disabled. So a saved mode of
                        // Custom (255 → index 3) wrote 3 into a one-item ComboBox and threw
                        // ArgumentException ("Value does not fall within the expected range").
                        //
                        // Measured on 0.1.8.67: 36 occurrences in a single hour of widget log, every one on
                        // a game end and only for the Global profile (the Game_ branch above never touches
                        // SelectedIndex). It did not crash the widget — the outer catch contains it — but it
                        // ABORTED THE REST of LoadProfileSettings, so the TDP-slider restore, overlay level,
                        // CPU affinity and UpdateProfileDisplay below were silently skipped
                        // on every single game end. ClampComboIndex existed for exactly this failure mode and
                        // was applied to OSPowerModeComboBox only; these sites were missed.
                        int tdpModeIndex = ClampComboIndex(TDPModeComboBox, index);
                        int legionModeIndex = ClampComboIndex(LegionPerformanceModeComboBox, index);
                        if (index >= 0 && (legionPerformanceMode.Value != savedLegionPerformanceMode || TDPModeComboBox.SelectedIndex != tdpModeIndex))
                        {
                            if (LegionPerformanceModeComboBox.SelectedIndex != legionModeIndex)
                                LegionPerformanceModeComboBox.SelectedIndex = legionModeIndex;
                            if (TDPModeComboBox.SelectedIndex != tdpModeIndex)
                            {
                                // lastTDPModeIndex must hold what the ComboBox actually shows — it is
                                // compared against SelectedIndex to tell helper syncs from user edits.
                                lastTDPModeIndex = tdpModeIndex;
                                TDPModeComboBox.SelectedIndex = tdpModeIndex;
                            }
                            // Mode itself is NOT sent: the helper restores the global profile's
                            // LegionPerformanceMode in RestoreGlobalProfileSettings before it even tells
                            // the widget the game ended. Only the ComboBox index (group C) is ours.
                            Logger.Info($"Restored TDP mode ComboBox to {GetLegionModeShortName(savedLegionPerformanceMode)} ({savedLegionPerformanceMode}) after game closed — mode owned by helper");
                        }
                        // The slider itself and the deferred "after the mode change, re-send TDP / PL2 /
                        // EPP" block are gone with the rest of the TDP group: the helper has already
                        // applied the global profile's values by the time we get here.
                        savedLegionPerformanceMode = -1; // Clear saved mode
                    }
                    else if (SaveTDP)
                    {
                        // Loading Global profile directly (not returning from game) - apply profile's TDP Mode
                        int profileMode = profile.LegionPerformanceMode;
                        int modeIndex = GetProfileTDPModeIndex(profile);
                        Logger.Info($"LoadProfileSettings: profileMode={profileMode}, modeIndex={modeIndex}, legionPerformanceMode.Value={legionPerformanceMode?.Value}, TDPModeComboBox.SelectedIndex={TDPModeComboBox?.SelectedIndex}");

                        // Always update UI to match profile when loading Global profile
                        // The internal value may already match (set by helper) but UI may be stale
                        if (modeIndex >= 0)
                        {
                            // Clamp for the same reason as the branch above: TDPModeComboBox is rebuilt at
                            // runtime and can hold fewer items than the stored index.
                            int legionModeIdx = ClampComboIndex(LegionPerformanceModeComboBox, modeIndex);
                            int tdpModeIdx = ClampComboIndex(TDPModeComboBox, modeIndex);
                            // Set lastTDPModeIndex BEFORE touching the ComboBox so
                            // TDPModeComboBox_SelectionChanged does not treat the profile load as a
                            // user-initiated change — and to the CLAMPED value, which is what the control
                            // will actually report back.
                            lastTDPModeIndex = tdpModeIdx;
                            if (LegionPerformanceModeComboBox.SelectedIndex != legionModeIdx)
                                LegionPerformanceModeComboBox.SelectedIndex = legionModeIdx;
                            if (TDPModeComboBox.SelectedIndex != tdpModeIdx)
                                TDPModeComboBox.SelectedIndex = tdpModeIdx;
                            // Neither the mode nor the deferred TDP/PL2/EPP re-send happens here any
                            // more — same reasoning as the game-end branch above. Only the two ComboBox
                            // indices (group C) are still the widget's to set.
                            Logger.Info($"Applied TDP mode ComboBox index for {GetLegionModeShortName(profileMode)} ({profileMode}) for {profileName} — mode owned by helper");
                        }
                    }

                    // Update TDP slider enabled state based on mode
                    // Skip for game profiles: helper manages TDP mode, and the ComboBox hasn't been
                    // updated yet (it still shows the old global mode). Running UpdateTDPSliderEnabledState
                    // now would see the wrong mode and send incorrect values to the helper (e.g.,
                    // AutoTDP=false when the game profile has AutoTDP=true, because the old mode is
                    // non-Custom). UpdateTDPSliderEnabledState runs naturally when the helper sends its
                    // mode via pipe → ComboBox updates → TDPModeComboBox_SelectionChanged.
                    if (!profileName.StartsWith("Game_"))
                    {
                        UpdateTDPSliderEnabledState();
                    }
                }
                // Generic device TDP Mode handling — skip during initial sync to avoid
                // ArgumentOutOfRangeException when the ComboBox has fewer items than the
                // saved profile index. ApplyProfileTDPToHelper sets the correct mode after connection.
                else if (legionGoDetected?.Value != true && TDPModeComboBox != null && !isInitialSync)
                {
                    // Load TDP Mode from profile for generic devices
                    int profileMode = profile.LegionPerformanceMode;
                    int modeIndex = GetProfileTDPModeIndex(profile); // Already defaults to Balanced if not found

                    // Always sync lastTDPModeIndex to match the profile's mode.
                    // Without this, lastTDPModeIndex retains a stale value from the previous
                    // user session, causing TDPModeComboBox_SelectionChanged to skip the first
                    // mode change (selectedIndex == lastTDPModeIndex early return).
                    if (SaveTDP)
                    {
                        // Clamped: the comment above says this branch is skipped during initial sync "to
                        // avoid ArgumentOutOfRangeException when the ComboBox has fewer items than the saved
                        // profile index" — that gate only narrows the window, it does not close it, because
                        // PopulateTdpModeComboBox can shrink the list at any time. Clamping closes it.
                        int genericModeIndex = ClampComboIndex(TDPModeComboBox, modeIndex);
                        lastTDPModeIndex = genericModeIndex;

                        if (TDPModeComboBox.SelectedIndex != genericModeIndex)
                        {
                            TDPModeComboBox.SelectedIndex = genericModeIndex;
                            Logger.Info($"Applied generic device TDP Mode: index {genericModeIndex} (from {modeIndex}, mode {profileMode}) for {profileName}");

                            // The slider value is NOT touched here. It follows the helper's TDP push,
                            // and it must not be overwritten with the hardcoded GoTweaks preset watts
                            // {8,15,25} either — those never matched the Claw's presets anyway.
                        }
                    }

                    // Update TDP slider enabled state based on mode
                    UpdateTDPSliderEnabledState();
                }

                // The MSI-Claw-specific "always re-assert the profile's slider TDP" block that used to
                // sit here is gone. It was the second half of the measured clobber (no isExplicitSwitch
                // gate, so a cosmetic reload re-asserted a stale widget value over the helper's); it had
                // already been reduced to a Debug log, and with the whole TDP group now helper-owned
                // even that log would only print a frozen widget value.

                // Resolution and refresh rate used to be read from the widget's copy and PUSHED here,
                // guarded by isExplicitSwitch so a cosmetic reload would not move the screen. Both are
                // gone with §5.5: the helper applies the display mode from its own store when the
                // profile changes, which is also where the guard belongs — it knows whether a game
                // actually started, while the widget could only approximate that from currentGameName.
                // The combo boxes stay: picking a mode there still sends a Set, and that Set is exactly
                // what the helper treats as the user's choice and persists.

                // Overlay Level
                if (SaveOverlayLevel && PerformanceOverlayComboBox != null)
                {
                    int level = profile.OverlayLevel;
                    if (level >= 0 && level < PerformanceOverlayComboBox.Items.Count)
                    {
                        PerformanceOverlayComboBox.SelectedIndex = level;
                        // The SelectionChanged handler will update PerformanceOverlaySlider and send to system
                    }
                }

                // CPU affinity is no longer restored from the profile: it is a GoTweaks leftover the
                // product does not use (user, 2026-08-01) and the save side is gone, so this block
                // would have re-applied a frozen value on every switch.

                // Update profile display to show correct TDP mode in Profiles tab
                UpdateProfileDisplay();

                // The per-game MSI fan hook that used to sit here is GONE (2026-08-02). Hanging the fan off
                // LoadProfileSettings is exactly what made the feature untenable the first time: this
                // method re-runs on every periodic property BatchGet (~10 s) and on every Game-Bar
                // detection flap, so the curve was re-written ~20×/hr and each write re-entered software
                // fan mode. The helper applies the per-game curve now, from the same events it uses for
                // TDP — see Program.MSIClaw.ApplyFanCurveFromProfile.
            }
            catch (Exception ex)
            {
                // CRITICAL: LoadProfileSettings is invoked from OnGameTextChanged, which is a
                // DependencyProperty-changed callback. An exception escaping here is not a normal
                // managed exception — XAML wraps it into a stowed exception (0xc000027b) and
                // FAIL-FASTS the whole process, taking down the entire Game Bar widget. That is
                // exactly the "helper goes away / no game-start notification after a game ends"
                // crash loop seen in the field: a single profile with an out-of-range ComboBox
                // index (e.g. OSPowerMode/IntelFpsTier/AutoTDPControllerType larger than the
                // control's item count) killed the widget on every game start/stop transition.
                // Swallow + log so one bad value can never crash the widget; the rest of the
                // profile is best-effort applied and the user keeps notifications + connection.
                Logger.Error($"LoadProfileSettings('{profileName}') threw and was contained to prevent a widget crash: {ex}");
            }
            finally
            {
                isLoadingProfile = false;
            }
        }

        /// <summary>
        /// Clamps a desired ComboBox index to the valid range [-1 .. Items.Count-1].
        /// Setting ComboBox.SelectedIndex to a value >= Items.Count throws
        /// ArgumentException ("Value does not fall within the expected range"), which—when it
        /// happens inside a DependencyProperty callback like the profile-switch path—fail-fasts
        /// the entire Game Bar widget. Profiles can legitimately hold an index that no longer
        /// fits the current control (item count differs by device/driver), so clamp instead of throw.
        /// </summary>
        private static int ClampComboIndex(ComboBox combo, int desired)
        {
            if (combo == null) return -1;
            int max = combo.Items.Count - 1;
            if (desired > max) return max;   // -1 when the combo is empty, otherwise last item
            if (desired < -1) return -1;
            return desired;
        }

        private PerformanceProfile GetProfile(string profileName)
        {
            // Never return a game profile for invalid game names (case-insensitive check)
            if (profileName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Warn($"Attempted to get invalid profile: {profileName}, returning global profile");
                return globalProfile;
            }

            // Handle game profiles
            if (profileName.StartsWith("Game_"))
            {
                if (profileName.EndsWith("_AC"))
                    return gameACProfile;
                else if (profileName.EndsWith("_DC"))
                    return gameDCProfile;
                else
                    return gameProfile;
            }

            // Handle global profiles
            switch (profileName)
            {
                case "AC": return acProfile;
                case "DC": return dcProfile;
                default: return globalProfile;
            }
        }

    }
}
