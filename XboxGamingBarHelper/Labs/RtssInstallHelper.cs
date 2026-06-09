using NLog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// RTSS (RivaTuner Statistics Server) detection + in-app installation.
    /// Detection reuses Shared.Utilities.RTSSHelper.IsInstalled() (registry check).
    ///
    /// RTSS has no stable direct-download URL, so installation uses winget (Guru3D.RTSS).
    /// To run winget elevated from the non-elevated helper we resolve the REAL winget.exe
    /// inside the Microsoft.DesktopAppInstaller package — the per-user "winget" app-execution
    /// alias does NOT resolve under ShellExecute "runas", which is the same failure mode that
    /// broke the old winget-based HidHide install.
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

            string winget = ResolveWingetExecutable();
            if (string.IsNullOrEmpty(winget))
            {
                Logger.Warn("RTSS install: winget not found; cannot install RTSS automatically.");
                return false;
            }

            Logger.Info($"Starting RTSS installation via winget: {winget}");
            bool ran = RunWingetInstall(winget);
            bool installed = IsInstalled();
            if (ran && !installed)
            {
                // winget returned success but RTSS.exe is not present — typically a winget download/
                // certificate failure (e.g. 0x8A15005E), often caused by an incorrect system clock.
                Logger.Warn("RTSS: winget reported success but RTSS.exe was not found. This is usually a "
                    + "winget download/certificate failure (error 0x8A15005E) — check the device date/time, "
                    + "or install RTSS manually from guru3d.com.");
            }
            Logger.Info($"RTSS installation finished. ran={ran}, installed={installed}");
            return installed;
        }

        /// <summary>
        /// Resolves the real winget.exe path. Prefers the actual executable inside the
        /// Microsoft.DesktopAppInstaller package install location (resolvable as the current
        /// user via Get-AppxPackage and launchable elevated), then the WindowsApps alias.
        /// </summary>
        private static string ResolveWingetExecutable()
        {
            try
            {
                string installLocation = QueryDesktopAppInstallerLocation();
                if (!string.IsNullOrEmpty(installLocation))
                {
                    string exe = Path.Combine(installLocation, "winget.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"winget package-location resolve failed: {ex.Message}");
            }

            // Fallback: WindowsApps app-execution alias (works non-elevated; last resort).
            string alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(alias)) return alias;

            return null;
        }

        private static string QueryDesktopAppInstallerLocation()
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.Arguments =
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                    "\"(Get-AppxPackage -Name Microsoft.DesktopAppInstaller | Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty InstallLocation)\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                if (!process.Start()) return null;
                if (!process.WaitForExit(5000)) { try { process.Kill(); } catch { } return null; }
                if (process.ExitCode != 0) return null;

                string output = process.StandardOutput.ReadToEnd();
                if (string.IsNullOrWhiteSpace(output)) return null;

                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) return trimmed;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Get-AppxPackage DesktopAppInstaller query failed: {ex.Message}");
            }

            return null;
        }

        private static bool RunWingetInstall(string executable)
        {
            string arguments = $"install --id {RtssWingetId} -e --accept-package-agreements --accept-source-agreements --silent";

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
                    Logger.Warn($"RTSS install failed to start: {executable}");
                    return false;
                }

                Logger.Info($"RTSS winget started ({executable}), PID={process.Id}");
                if (!process.WaitForExit(300000))
                {
                    try { process.Kill(); } catch { }
                    Logger.Warn("RTSS installation timed out after 5 minutes");
                    return false;
                }

                int code = process.ExitCode;
                // 0 = success; -1978335189 = no applicable installer (already installed / up to date)
                bool ok = code == 0 || code == unchecked((int)0x8A15002B);
                Logger.Info($"RTSS winget exited with code {code} (ok={ok})");
                return ok;
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
