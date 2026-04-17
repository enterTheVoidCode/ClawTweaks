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
        private static void SetScreenSaverEnabled(bool enabled)
        {
            screenSaverEnabled = enabled;
            if (enabled)
            {
                if (screenSaverTimer == null)
                {
                    screenSaverTimer = new System.Threading.Timer(ScreenSaverIdleCheck, null, ScreenSaverCheckIntervalMs, ScreenSaverCheckIntervalMs);
                    Logger.Info("Screen Saver: Started idle monitoring timer");
                }
            }
            else
            {
                if (screenSaverTimer != null)
                {
                    screenSaverTimer.Dispose();
                    screenSaverTimer = null;
                    Logger.Info("Screen Saver: Stopped idle monitoring timer");
                }
            }
        }

        private static void ScreenSaverIdleCheck(object state)
        {
            if (!screenSaverEnabled) return;

            try
            {
                var lastInput = new LASTINPUTINFO();
                lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

                if (GetLastInputInfo(ref lastInput))
                {
                    uint idleMs = (uint)Environment.TickCount - lastInput.dwTime;
                    if (idleMs >= ScreenSaverIdleTimeoutMs)
                    {
                        if (!screenSaverTriggered)
                        {
                            screenSaverTriggered = true;
                            Logger.Info($"Screen Off: Idle for {idleMs}ms, turning off display");
                            SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, MONITOR_OFF);
                        }
                    }
                    else if (screenSaverTriggered)
                    {
                        // User moved mouse/pressed key — re-arm for next idle period
                        screenSaverTriggered = false;
                        Logger.Info("Screen Saver: Input detected, re-armed");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Screen Saver: Idle check failed: {ex.Message}");
            }
        }

    }
}
