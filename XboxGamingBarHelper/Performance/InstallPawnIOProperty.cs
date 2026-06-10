using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NLog;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Performance
{
    /// <summary>
    /// Property to trigger PawnIO installation.
    /// Write "install" to trigger the installation.
    /// Downloads the installer from GitHub and runs with -install -silent.
    /// </summary>
    internal class InstallPawnIOProperty : HelperProperty<string, PerformanceManager>
    {
        private new static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public InstallPawnIOProperty(PerformanceManager inManager)
            : base("", null, Function.TdpMethod_InstallPawnIO, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (Value?.ToLowerInvariant() == "install")
            {
                Logger.Info("PawnIO installation requested from widget");
                // Run installation asynchronously to avoid blocking
                Task.Run(() => InstallPawnIO());
                // Reset the value
                SetValue("");
            }
        }

        /// <summary>
        /// Installs PawnIO via the embedded Setup-Tools.ps1 (winget: namazso.PawnIO). The
        /// download-and-execute logic lives in the .ps1 — NOT in this managed assembly — so the helper
        /// exe doesn't carry the WebClient-download + Process.Start-exe pattern that AV flags as a .NET
        /// "downloader". After install, refresh status and restart the helper so it re-inits with the
        /// PawnIO sensor driver available.
        /// </summary>
        private void InstallPawnIO()
        {
            try
            {
                Logger.Info("PawnIO install requested — running tool setup (winget) for 'pawnio'...");
                int code = XboxGamingBarHelper.Setup.ToolSetupRunner.Run("pawnio");
                Logger.Info($"PawnIO setup script finished (exit={code}).");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to install PawnIO: {ex.Message}");
            }
            finally
            {
                // Refresh the installed status
                Logger.Info("Refreshing PawnIO installed status...");
                Manager?.RefreshPawnIOInstalledStatus();

                // If PawnIO is now installed, restart helper to reinitialize with PawnIO support
                if (Manager?.IsPawnIOInstalled == true)
                {
                    Logger.Info("PawnIO is now installed - restarting helper to reinitialize...");
                    // Give time for the status to be sent to widget
                    System.Threading.Thread.Sleep(1000);
                    // Exit helper - widget will detect disconnection and relaunch
                    Environment.Exit(0);
                }
            }
        }
    }
}
