using NLog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using XboxGamingBarHelper.ControllerEmulation;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Utility class for HidHide detection and installation.
    /// </summary>
    internal static class HidHideHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string HidHideWingetId = "NefariusSoftwareSolutions.HidHide";

        public static bool IsInstalled()
        {
            string cliPath = ControllerSuppressionManager.GetDetectedCliPath();
            bool installed = !string.IsNullOrEmpty(cliPath);
            Logger.Debug($"HidHide installed check: {installed}{(installed ? $" ({cliPath})" : string.Empty)}");
            return installed;
        }

        public static bool Install()
        {
            if (IsInstalled())
            {
                Logger.Info("HidHide already installed; skipping installation");
                return true;
            }

            Logger.Info("Starting HidHide installation via winget...");
            if (!TryInstallViaWinget())
            {
                Logger.Warn("HidHide installation via winget failed");
                return false;
            }

            bool installed = IsInstalled();
            Logger.Info($"HidHide installation completed. Installed={installed}");
            return installed;
        }

        private static bool TryInstallViaWinget()
        {
            string localWinget = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps",
                "winget.exe");

            string[] candidates = new[]
            {
                "winget",
                localWinget,
            };

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
                {
                    continue;
                }

                if (RunWingetInstall(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RunWingetInstall(string executable)
        {
            string arguments = $"install --id {HidHideWingetId} -e --accept-package-agreements --accept-source-agreements --silent --disable-interactivity";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                using Process process = Process.Start(startInfo);
                if (process == null)
                {
                    Logger.Warn($"HidHide install failed to start: {executable} {arguments}");
                    return false;
                }

                Logger.Info($"HidHide installer started ({executable}), PID={process.Id}");

                if (!process.WaitForExit(300000))
                {
                    try { process.Kill(); } catch { }
                    Logger.Warn("HidHide installation timed out after 5 minutes");
                    return false;
                }

                if (process.ExitCode == 0 || process.ExitCode == 3010)
                {
                    Logger.Info($"HidHide installation process exited with code {process.ExitCode}");
                    return true;
                }

                Logger.Warn($"HidHide installation failed with exit code {process.ExitCode}");
                return false;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Logger.Info("HidHide installation cancelled by user (UAC prompt declined)");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"HidHide installation exception: {ex.Message}");
                return false;
            }
        }
    }
}
