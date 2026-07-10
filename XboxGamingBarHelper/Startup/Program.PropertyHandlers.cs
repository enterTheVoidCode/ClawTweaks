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

        private static void CPUState_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUState_PropertyChanged - already applying profile");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUState_PropertyChanged - in profile switch cooldown");
                return;
            }

            // TEST [ProfileSaveFlags-CPUState]: With ProfileSaveCPUState unchecked, change
            // Min/Max CPU State sliders in-game. Verify the change goes to GlobalProfile, not
            // the per-game profile. Pre-flag baseline: always wrote to CurrentProfile.
            RouteProfileSave(ProfileSaveFlagsState.CPUState, "CPUState",
                cur => { cur.MaxCPUState = powerManager.MaxCPUState.Value; cur.MinCPUState = powerManager.MinCPUState.Value; },
                (ref Shared.Data.GameProfile glo) => { glo.MaxCPUState = powerManager.MaxCPUState.Value; glo.MinCPUState = powerManager.MinCPUState.Value; });
        }

        private static void SystemManager_ResumeFromSleep(object sender)
        {
            Logger.Info("System resumed from sleep/hibernation, refreshing hardware sensors and re-applying profile.");

            // Reset RTSS OSD connection (can become stale after hibernation, causing frozen OSD values)
            rtssManager?.ResetRTSSConnection();

            // Force refresh hardware sensors (battery values can be stale after hibernation)
            performanceManager?.ForceRefreshHardware();

            // Re-assert the MSI Claw TDP unlock FIRST: the EC power-cycles during sleep/hibernate
            // and clears the OverBoost + ceiling unlock (the helper process survives, so the
            // once-per-run guard would otherwise skip it). Without this the TDP re-apply below is
            // clamped to ~17W — the "17W cap after hibernation" community reports. No-op on non-Claw.
            performanceManager?.ReassertMsiClawTdpUnlock();

            // Re-apply current profile settings (TDP, CPU boost, EPP, CPU state)
            CurrentProfile_PropertyChanged(sender, null);

            // The controller power-cycles across hibernate and comes back on its EEPROM (normal)
            // colour; force the SoC tint to re-apply so the LED doesn't stay on the normal colour
            // until the widget is opened. No-op if LED-by-SoC is off; retries via the timer if the
            // LED HID isn't reachable yet during the controller re-mount.
            OnResumeReassertLedColorBySoc();
        }

        // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
        //private static void GPUClock_PropertyChanged(object sender, PropertyChangedEventArgs e)
        //{
        //    // GPU Clock is saved per-profile
        //    // Note: Profiles would need GPUClockMin/Max properties added to support per-game GPU clocks
        //    Logger.Info($"GPU Clock settings changed: Enabled={powerManager.LimitGPUClock}, Min={powerManager.GPUClockMin}, Max={powerManager.GPUClockMax}");
        //}

        private static void CPUBoost_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUBoost_PropertyChanged - already applying profile");
                return;
            }

            // Skip a display-only readback sync (EnforceCpuAdvanced correcting the widget to match
            // Windows' true state) — persisting it here would adopt a transient external change
            // (e.g. MSI Center M) into the profile permanently. See PowerManager.IsSyncingBoostDisplay.
            if (Power.PowerManager.IsSyncingBoostDisplay)
            {
                Logger.Debug($"Skipping CPUBoost_PropertyChanged - system readback sync, not a real change");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUBoost_PropertyChanged - in profile switch cooldown");
                return;
            }

            // TEST [ProfileSaveFlags-CPUBoost]: With ProfileSaveCPUBoost unchecked, toggle
            // CPU Boost in-game. Verify the change goes to GlobalProfile, not the per-game
            // profile. Pre-flag baseline: always wrote to CurrentProfile.
            RouteProfileSave(ProfileSaveFlagsState.CPUBoost, "CPUBoost",
                cur => cur.CPUBoost = powerManager.CPUBoost,
                (ref Shared.Data.GameProfile glo) => glo.CPUBoost = powerManager.CPUBoost);

            // Persist the user's GLOBAL boost state reliably (same fix as TDP_PropertyChanged):
            // RouteProfileSave's onCurrent path saves into CurrentProfile (a struct copy) which
            // does not reliably reach global.xml on disk (GlobalProfile stays stale — same root
            // cause documented in RestoreGlobalProfileSettings). Without this, the very next
            // "Refreshed GlobalProfile from disk" (fires whenever Game Bar reopens with no game
            // running) re-reads the stale on-disk value and silently reverts the user's toggle —
            // observed as "boost turns back on by itself". LocalSettingsHelper is a separate,
            // reliably-written store (same one TDP uses) and RestoreGlobalProfileSettings now
            // prefers it over the flaky GlobalProfile.CPUBoost.
            try
            {
                bool gameRunning = systemManager?.RunningGame?.Value.IsValid() == true;
                if (!gameRunning)
                {
                    bool globalBoost = powerManager.CPUBoost.Value;
                    Settings.LocalSettingsHelper.SetValue("GlobalCPUBoost", globalBoost);
                    Logger.Info($"[CPUBoost-Persist] Saved global CPU Boost = {globalBoost} (no game running)");
                }
                else
                {
                    Logger.Debug("[CPUBoost-Persist] Skipped (game running → per-game boost, not global)");
                }
            }
            catch (Exception ex) { Logger.Debug($"Persist GlobalCPUBoost failed: {ex.Message}"); }
        }

        private static void CPUEPP_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Skip during profile application to prevent cross-contamination
            if (isApplyingProfile)
            {
                Logger.Debug($"Skipping CPUEPP_PropertyChanged - already applying profile");
                return;
            }

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUEPP_PropertyChanged - in profile switch cooldown");
                return;
            }

            // TEST [ProfileSaveFlags-CPUEPP]: With ProfileSaveCPUEPP unchecked, change CPU EPP
            // in-game. Verify the change goes to GlobalProfile, not the per-game profile.
            // Pre-flag baseline: always wrote to CurrentProfile.
            RouteProfileSave(ProfileSaveFlagsState.CPUEPP, "CPUEPP",
                cur => cur.CPUEPP = powerManager.CPUEPP,
                (ref Shared.Data.GameProfile glo) => glo.CPUEPP = powerManager.CPUEPP);
        }

        // ===== CPU advanced (ToothNClaw port): scheduling policy, P/E max freq =====
        // Boost mode was removed — Boost is now plain on/off (CPUBoost_PropertyChanged above),
        // a single writer to Windows' PERFBOOSTMODE instead of two racing ones.

        private static void SchedulingPolicy_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile) { Logger.Debug("Skipping SchedulingPolicy_PropertyChanged - applying profile"); return; }
            if (IsInProfileSwitchCooldown()) { Logger.Debug("Skipping SchedulingPolicy_PropertyChanged - cooldown"); return; }

            RouteProfileSave(ProfileSaveFlagsState.CpuAdvanced, "ProcessorSchedulingPolicy",
                cur => cur.ProcessorSchedulingPolicy = powerManager.SchedulingPolicy,
                (ref Shared.Data.GameProfile glo) => glo.ProcessorSchedulingPolicy = powerManager.SchedulingPolicy);
        }

        private static void MaxCoreFreq_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile) { Logger.Debug("Skipping MaxCoreFreq_PropertyChanged - applying profile"); return; }
            if (IsInProfileSwitchCooldown()) { Logger.Debug("Skipping MaxCoreFreq_PropertyChanged - cooldown"); return; }

            // Both P and E core freq are captured together (single CpuAdvanced flag).
            RouteProfileSave(ProfileSaveFlagsState.CpuAdvanced, "MaxCoreFreq",
                cur => { cur.MaxPCoreFreqMHz = powerManager.MaxPCoreFreq; cur.MaxECoreFreqMHz = powerManager.MaxECoreFreq; },
                (ref Shared.Data.GameProfile glo) => { glo.MaxPCoreFreqMHz = powerManager.MaxPCoreFreq; glo.MaxECoreFreqMHz = powerManager.MaxECoreFreq; });
        }

        // ===== Intel Display (IGCL): adaptive sharpness + saturation =====
        // Stored in the existing performance profile (renamed "Performance & Display").
        private static void IntelDisplay_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (isApplyingProfile) { Logger.Debug("Skipping IntelDisplay_PropertyChanged - applying profile"); return; }
            if (IsInProfileSwitchCooldown()) { Logger.Debug("Skipping IntelDisplay_PropertyChanged - cooldown"); return; }

            // Per-game capture only makes sense WHILE a game is running. With no game, CurrentProfile
            // IS the global profile and RouteProfileSave's onCurrent path only mutates it in memory
            // (GameProfileProperty setters don't Save) — so the change never reached global.xml and the
            // boot-time re-apply read the stale on-disk value (observed: Adaptive Sharpness stuck at a
            // high value, reverting every restart). Gate on gameRunning so the no-game case takes the
            // onGlobal path, which explicitly g.Save()s to global.xml. (CPUBoost/TDP work around the
            // same onCurrent-doesn't-persist root cause via LocalSettingsHelper; IntelDisplay never did.)
            bool gameRunning = systemManager?.RunningGame?.Value.IsValid() == true;
            bool saveToProfile = ProfileSaveFlagsState.IntelDisplay && gameRunning;

            RouteProfileSave(saveToProfile, "IntelDisplay",
                cur =>
                {
                    cur.IntelAdaptiveSharpness = intelGpuManager.IntelAdaptiveSharpness;
                    cur.IntelColorSaturation   = intelGpuManager.IntelColorSaturation;
                    cur.IntelColorHue          = intelGpuManager.IntelColorHue;
                    cur.IntelDisplayContrast   = intelGpuManager.IntelDisplayContrast;
                    cur.IntelDisplayBrightness = intelGpuManager.IntelDisplayBrightness;
                    cur.IntelDisplayGamma      = intelGpuManager.IntelDisplayGamma;
                },
                (ref Shared.Data.GameProfile glo) =>
                {
                    glo.IntelAdaptiveSharpness = intelGpuManager.IntelAdaptiveSharpness;
                    glo.IntelColorSaturation   = intelGpuManager.IntelColorSaturation;
                    glo.IntelColorHue          = intelGpuManager.IntelColorHue;
                    glo.IntelDisplayContrast   = intelGpuManager.IntelDisplayContrast;
                    glo.IntelDisplayBrightness = intelGpuManager.IntelDisplayBrightness;
                    glo.IntelDisplayGamma      = intelGpuManager.IntelDisplayGamma;
                });
        }

        private static void AutoHibernateEnabled_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (settingsManager?.AutoHibernateEnabled == null) return;
            SetAutoHibernateEnabled(settingsManager.AutoHibernateEnabled.Value);
        }

        private static void AutoHibernateIdleMinutes_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (settingsManager?.AutoHibernateIdleMinutes == null) return;
            UpdateAutoHibernateIdleTimeout(settingsManager.AutoHibernateIdleMinutes.Value);
        }

    }
}
