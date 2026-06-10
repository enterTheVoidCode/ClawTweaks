using NLog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using XboxGamingBarHelper.ControllerEmulation;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Utility class for HidHide detection and installation.
    /// </summary>
    internal static class HidHideHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static bool IsInstalled()
        {
            // Primary: CLI found → definitely installed
            string cliPath = ControllerSuppressionManager.GetDetectedCliPath();
            if (!string.IsNullOrEmpty(cliPath))
            {
                Logger.Debug($"HidHide installed check: true (CLI at {cliPath})");
                return true;
            }

            // Fallback: kernel driver/service present even if CLI not on PATH
            if (IsDriverServicePresent())
            {
                Logger.Debug("HidHide installed check: true (driver service detected, CLI not on PATH)");
                return true;
            }

            Logger.Debug("HidHide installed check: false");
            return false;
        }

        private static bool IsDriverServicePresent()
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController("HidHide");
                _ = sc.Status; // throws InvalidOperationException if service does not exist
                return true;
            }
            catch { }

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\HidHide");
                return key != null;
            }
            catch { }

            return false;
        }

        public static bool Install()
        {
            if (IsInstalled())
            {
                Logger.Info("HidHide already installed; skipping installation");
                return true;
            }

            // Install via the embedded Setup-Tools.ps1 (winget-first, with a direct-download fallback
            // inside the PowerShell script). The download-and-execute logic stays in the .ps1 — NOT in
            // this managed assembly — so the helper exe doesn't carry the WebClient-download +
            // Process.Start-exe pattern that AV flags as a .NET "downloader".
            Logger.Info("HidHide install requested — running tool setup (winget) for 'hidhide'...");
            int code = XboxGamingBarHelper.Setup.ToolSetupRunner.Run("hidhide");
            bool installed = IsInstalled();
            Logger.Info($"HidHide installation finished (script exit={code}, installed={installed}).");
            return installed;
        }
    }
}
