using NLog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// RTSS (RivaTuner Statistics Server) detection + in-app installation.
    /// Detection reuses Shared.Utilities.RTSSHelper.IsInstalled() (registry check).
    /// Installation uses winget (Guru3D.RTSS) with UAC elevation, mirroring the
    /// ViGEmBus/HidHide in-app install pattern (HidHideHelper).
    /// </summary>
    internal static class RtssInstallHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string RtssWingetId = "Guru3D.RTSS";

        public static bool IsInstalled() => Shared.Utilities.RTSSHelper.IsInstalled();

        public static bool Install()
        {
            if (IsInstalled())
            {
                Logger.Info("RTSS already installed; skipping installation");
                return true;
            }

            Logger.Info("Starting RTSS installation via winget...");
            if (!TryInstallViaWinget())
            {
                Logger.Warn("RTSS installation via winget failed");
                return false;
            }

            bool installed = IsInstalled();
            Logger.Info($"RTSS installation completed. Installed={installed}");
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
            string arguments = $"install --id {RtssWingetId} -e --accept-package-agreements --accept-source-agreements --silent --disable-interactivity";

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
                    Logger.Warn($"RTSS install failed to start: {executable} {arguments}");
                    return false;
                }

                Logger.Info($"RTSS installer started ({executable}), PID={process.Id}");

                if (!process.WaitForExit(300000))
                {
                    try { process.Kill(); } catch { }
                    Logger.Warn("RTSS installation timed out after 5 minutes");
                    return false;
                }

                if (process.ExitCode == 0 || process.ExitCode == 3010)
                {
                    Logger.Info($"RTSS installation process exited with code {process.ExitCode}");
                    return true;
                }

                Logger.Warn($"RTSS installation failed with exit code {process.ExitCode}");
                return false;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Logger.Info("RTSS installation cancelled by user (UAC prompt declined)");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"RTSS installation exception: {ex.Message}");
                return false;
            }
        }
    }
}
