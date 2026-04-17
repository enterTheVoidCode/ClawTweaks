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

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU State to Max={powerManager.MaxCPUState.Value}%, Min={powerManager.MinCPUState.Value}%.");
            profileManager.CurrentProfile.MaxCPUState = powerManager.MaxCPUState.Value;
            profileManager.CurrentProfile.MinCPUState = powerManager.MinCPUState.Value;
        }

        private static void SystemManager_ResumeFromSleep(object sender)
        {
            Logger.Info("System resumed from sleep/hibernation, refreshing hardware sensors and re-applying profile.");

            // Reset RTSS OSD connection (can become stale after hibernation, causing frozen OSD values)
            rtssManager?.ResetRTSSConnection();

            // Force refresh hardware sensors (battery values can be stale after hibernation)
            performanceManager?.ForceRefreshHardware();

            // Re-apply current profile settings (TDP, CPU boost, EPP, CPU state)
            CurrentProfile_PropertyChanged(sender, null);
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

            // Skip stale widget messages during cooldown after profile switch
            if (IsInProfileSwitchCooldown())
            {
                Logger.Debug($"Skipping CPUBoost_PropertyChanged - in profile switch cooldown");
                return;
            }

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU Boost from {profileManager.CurrentProfile.CPUBoost} to {powerManager.CPUBoost}.");
            profileManager.CurrentProfile.CPUBoost = powerManager.CPUBoost;
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

            Logger.Info($"Set current profile {profileManager.CurrentProfile.GameId.Name}'s CPU EPP from {profileManager.CurrentProfile.CPUEPP} to {powerManager.CPUEPP}.");
            profileManager.CurrentProfile.CPUEPP = powerManager.CPUEPP;
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
