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
        private static void SetAutoHibernateEnabled(bool enabled)
        {
            autoHibernateEnabled = enabled;
            if (enabled)
            {
                if (autoHibernateTimer == null)
                {
                    autoHibernateTimer = new System.Threading.Timer(AutoHibernateIdleCheck, null, AutoHibernateCheckIntervalMs, AutoHibernateCheckIntervalMs);
                    Logger.Info("Auto Hibernate: Started idle monitoring timer");
                }
            }
            else
            {
                if (autoHibernateTimer != null)
                {
                    autoHibernateTimer.Dispose();
                    autoHibernateTimer = null;
                    Logger.Info("Auto Hibernate: Stopped idle monitoring timer");
                }
            }
        }

        private static void UpdateAutoHibernateIdleTimeout(int minutes)
        {
            if (minutes < 1) minutes = 1;
            autoHibernateIdleTimeoutMs = minutes * 60 * 1000;
            Logger.Info($"Auto Hibernate: Idle timeout set to {minutes} minutes");
        }

        private static void AutoHibernateIdleCheck(object state)
        {
            if (!autoHibernateEnabled) return;

            try
            {
                var lastInput = new LASTINPUTINFO();
                lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

                if (GetLastInputInfo(ref lastInput))
                {
                    uint idleMs = (uint)Environment.TickCount - lastInput.dwTime;
                    if (idleMs < autoHibernateIdleTimeoutMs)
                    {
                        return;
                    }

                    if ((DateTime.UtcNow - lastAutoHibernateAttemptUtc).TotalMilliseconds < AutoHibernateCooldownMs)
                    {
                        return;
                    }

                    // Check power source mode: 1=AC Only, 2=DC Only
                    if (autoHibernateMode == 1) // AC Only
                    {
                        var powerStatus = global::Windows.System.Power.PowerManager.PowerSupplyStatus;
                        if (powerStatus != global::Windows.System.Power.PowerSupplyStatus.Adequate)
                        {
                            return; // Not on AC, skip
                        }
                    }
                    else if (autoHibernateMode == 2) // DC Only
                    {
                        var powerStatus = global::Windows.System.Power.PowerManager.PowerSupplyStatus;
                        if (powerStatus == global::Windows.System.Power.PowerSupplyStatus.Adequate)
                        {
                            return; // On AC, skip
                        }
                    }

                    // Avoid hibernating while a game is in the foreground
                    if (systemManager?.RunningGame?.Value.IsValid() == true && systemManager.RunningGame.Value.IsForeground)
                    {
                        Logger.Info("Auto Hibernate: Skipping - game is in foreground");
                        return;
                    }

                    lastAutoHibernateAttemptUtc = DateTime.UtcNow;
                    Logger.Info($"Auto Hibernate: Idle for {idleMs}ms, hibernating now");

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown",
                            Arguments = "/h",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Auto Hibernate: Failed to initiate hibernate: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Auto Hibernate: Idle check failed: {ex.Message}");
            }
        }

    }
}
