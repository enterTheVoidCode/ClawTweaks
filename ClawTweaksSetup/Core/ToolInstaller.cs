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
            InstallViaWinget("Nefarius.HidHide", "HidHide", log);

        public static bool InstallRtss(Action<string> log = null) =>
            InstallViaWinget("Guru3D.RTSS", "RTSS", log);

        private static bool InstallViaWinget(string packageId, string display, Action<string> log)
        {
            string winget = ResolveWinget();
            if (winget == null)
            {
                log?.Invoke($"winget not found — install {display} manually.");
                return false;
            }

            log?.Invoke($"Installing {display} via winget…");
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
                if (proc == null) return false;
                string _ = proc.StandardOutput.ReadToEnd();
                string __ = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(300000)) { try { proc.Kill(); } catch { } return false; }
                int code = proc.ExitCode;
                // 0 = ok; 0x8A15002B (-1978335189) = no applicable installer / already installed.
                bool ok = code == 0 || code == unchecked((int)0x8A15002B);
                log?.Invoke($"{display} winget exit {code} (ok={ok}).");
                return ok;
            }
            catch (Exception ex)
            {
                log?.Invoke($"{display} install error: {ex.Message}");
                return false;
            }
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
