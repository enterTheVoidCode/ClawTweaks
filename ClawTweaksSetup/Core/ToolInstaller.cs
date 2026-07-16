using System;
using System.Diagnostics;
using System.IO;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Installs HidHide and RTSS via winget (the same package IDs the helper uses: Nefarius.HidHide,
    /// Guru3D.RTSS). The setup is already elevated, so we resolve the REAL winget.exe inside the
    /// Microsoft.DesktopAppInstaller package (the per-user "winget" alias does not resolve elevated).
    /// usbip is handled separately by <see cref="UsbipSetup"/>.
    /// </summary>
    public static class ToolInstaller
    {
        public static bool InstallHidHide(Action<string> log = null) =>
            InstallViaWinget("Nefarius.HidHide", "HidHide", () => ToolDetect.HidHide().Installed, log);

        public static bool InstallRtss(Action<string> log = null) =>
            InstallViaWinget("Guru3D.RTSS", "RTSS", () => ToolDetect.Rtss().Installed, log);

        private static bool InstallViaWinget(string packageId, string display, Func<bool> isInstalled, Action<string> log)
        {
            string winget = ResolveWinget();
            if (winget == null)
            {
                log?.Invoke($"winget not found — install {display} manually.");
                return false;
            }

            log?.Invoke($"Installing {display} via winget…");
            bool ran;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = winget,
                    Arguments = $"install --id {packageId} -e --silent --accept-package-agreements --accept-source-agreements",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) { log?.Invoke($"{display} winget failed to start."); return false; }
                string _ = proc.StandardOutput.ReadToEnd();
                string __ = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(300000)) { try { proc.Kill(); } catch { } log?.Invoke($"{display} winget timed out."); return false; }
                int code = proc.ExitCode;
                // 0 = ok; 0x8A15002B (-1978335189) = no applicable installer / already installed.
                ran = code == 0 || code == unchecked((int)0x8A15002B);
                log?.Invoke($"{display} winget exit {code} (ran={ran}).");
            }
            catch (Exception ex)
            {
                log?.Invoke($"{display} install error: {ex.Message}");
                return false;
            }

            // winget's exit code alone is not proof — ported from RtssInstallHelper.Install() (the
            // main app's helper), which discovered winget can report success (exit 0) for RTSS while
            // never actually placing RTSS.exe on disk: a known download/certificate failure
            // (0x8A15005E), most often caused by the device's system clock being wrong. Trusting the
            // exit code alone is exactly what silently dropped RTSS from the install here. Re-check
            // the real, on-disk state instead.
            bool installed = isInstalled();
            if (ran && !installed)
                log?.Invoke($"{display}: winget reported success but it's still not installed — this is " +
                    "usually a winget download/certificate failure (0x8A15005E), often caused by an " +
                    $"incorrect system clock. Check the date/time, or install {display} manually.");
            return installed;
        }

        private static string ResolveWinget()
        {
            // Real winget.exe inside the installed Microsoft.DesktopAppInstaller package.
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                                "\"(Get-AppxPackage -AllUsers -Name Microsoft.DesktopAppInstaller | " +
                                "Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty InstallLocation)\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p != null && p.WaitForExit(6000) && p.ExitCode == 0)
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    foreach (var line in outp.Split('\r', '\n'))
                    {
                        string t = line.Trim();
                        if (t.Length > 0)
                        {
                            string exe = Path.Combine(t, "winget.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
            }
            catch { }

            // Fallback: WindowsApps alias.
            string alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            return File.Exists(alias) ? alias : null;
        }
    }
}
