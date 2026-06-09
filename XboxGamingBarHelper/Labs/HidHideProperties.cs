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
        // Direct download from GitHub (winget-via-runas is unreliable from the non-elevated
        // helper because winget is a per-user app-execution alias that does not resolve under
        // elevation — mirror the working ViGEmBus direct-download path instead).
        private const string HidHideReleasesApi = "https://api.github.com/repos/nefarius/HidHide/releases/latest";

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

            string url = ResolveLatestExeAssetUrl();
            if (string.IsNullOrEmpty(url))
            {
                Logger.Warn("HidHide install: could not resolve a release installer URL from GitHub");
                return false;
            }

            Logger.Info($"Starting HidHide installation via direct download: {url}");
            bool ran = DownloadAndRunInstaller(url, "HidHide_Setup.exe", "/passive /norestart");
            bool installed = IsInstalled();
            Logger.Info($"HidHide installation finished. ran={ran}, installed={installed}");
            return installed;
        }

        /// <summary>
        /// Resolves the latest HidHide .exe installer asset URL from the GitHub releases API.
        /// </summary>
        private static string ResolveLatestExeAssetUrl()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string json;
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "ClawTweaks/1.0");
                    json = client.DownloadString(HidHideReleasesApi);
                }

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            && name.IndexOf("symbol", StringComparison.OrdinalIgnoreCase) < 0
                            && asset.TryGetProperty("browser_download_url", out var u))
                        {
                            return u.GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"HidHide release lookup failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Downloads an installer to %TEMP% and runs it elevated/silently. Mirrors the working
        /// ViGEmBus direct-download install path. Returns true if the installer exited cleanly.
        /// </summary>
        private static bool DownloadAndRunInstaller(string url, string fileName, string silentArgs)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), fileName);
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "ClawTweaks/1.0");
                    client.DownloadFile(url, tempPath);
                }
                Logger.Info($"HidHide installer downloaded to {tempPath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = tempPath,
                    Arguments = silentArgs,
                    UseShellExecute = true, // required for Verb = "runas"
                    Verb = "runas",         // triggers the UAC prompt
                };

                using Process process = Process.Start(startInfo);
                if (process == null)
                {
                    Logger.Warn("HidHide installer failed to start (UAC prompt declined?)");
                    return false;
                }

                Logger.Info($"HidHide installer started, PID={process.Id}");
                if (!process.WaitForExit(300000))
                {
                    try { process.Kill(); } catch { }
                    Logger.Warn("HidHide installation timed out after 5 minutes");
                    return false;
                }

                int code = process.ExitCode;
                bool ok = code == 0 || code == 1641 || code == 3010;
                Logger.Info($"HidHide installer exited with code {code} (ok={ok})");
                return ok;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Logger.Info("HidHide installation cancelled by user (UAC prompt declined)");
                return false;
            }
            catch (WebException ex)
            {
                Logger.Error($"Failed to download HidHide installer: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"HidHide installation exception: {ex.Message}");
                return false;
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }
}
