using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Installs the ClawTweaks MSIX (with its dependency packages) via Add-AppxPackage. Mirrors
    /// Install.ps1: -ForceApplicationShutdown so a running instance is replaced, and
    /// -ForceUpdateFromAnyVersion so installs over a higher/rolled-back manifest still work.
    /// Appx cmdlets are most reliable under Windows PowerShell 5.1, so we invoke that explicitly.
    /// The signing certificate must already be trusted (see <see cref="CertInstaller"/>).
    /// </summary>
    public static class PackageInstaller
    {
        /// <summary>Finds the .msix/.msixbundle next to <see cref="SetupContext.AssetRoot"/> (root or a Package subfolder).</summary>
        public static string FindPackage()
        {
            string dir = SetupContext.AssetRoot;
            foreach (var d in new[] { dir, Path.Combine(dir, "Package") })
            {
                if (!Directory.Exists(d)) continue;
                foreach (var ext in new[] { "*.msixbundle", "*.msix", "*.appxbundle", "*.appx" })
                {
                    var f = Directory.GetFiles(d, ext, SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (f != null) return f;
                }
            }
            return null;
        }

        /// <summary>Collects dependency packages from a Dependencies\x64 (and Dependencies) folder.</summary>
        public static List<string> FindDependencies(string packagePath)
        {
            var deps = new List<string>();
            try
            {
                string root = Path.GetDirectoryName(packagePath);
                foreach (var d in new[] { Path.Combine(root, "Dependencies", "x64"), Path.Combine(root, "Dependencies") })
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var f in Directory.GetFiles(d))
                        if (f.EndsWith(".appx", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
                            deps.Add(f);
                }
            }
            catch { }
            return deps;
        }

        /// <summary>Currently installed ClawTweaks package version (via Get-AppxPackage), or null if not installed.
        /// Used by the Center menu to warn before a downgrade.</summary>
        public static Version GetInstalledVersion()
        {
            try
            {
                string winPs = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = File.Exists(winPs) ? winPs : "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                        "\"(Get-AppxPackage -Name 'MSIClaw.ClawTweaks*' | Select-Object -First 1 -ExpandProperty Version)\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                string outp = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(15000)) { try { proc.Kill(); } catch { } return null; }
                return Version.TryParse(outp.Trim(), out var v) ? v : null;
            }
            catch { return null; }
        }

        public static bool Install(string packagePath, IEnumerable<string> dependencies, Action<string> log = null)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("Add-AppxPackage -Path '").Append(packagePath.Replace("'", "''")).Append("'");
                var deps = dependencies?.ToList() ?? new List<string>();
                if (deps.Count > 0)
                {
                    sb.Append(" -DependencyPath ");
                    sb.Append(string.Join(",", deps.Select(p => "'" + p.Replace("'", "''") + "'")));
                }
                sb.Append(" -ForceApplicationShutdown -ForceUpdateFromAnyVersion");

                string winPs = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = File.Exists(winPs) ? winPs : "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + sb.ToString().Replace("\"", "\\\"") + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                log?.Invoke("Installing package…");
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                string outp = proc.StandardOutput.ReadToEnd();
                string err = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(300000)) { try { proc.Kill(); } catch { } log?.Invoke("Install timed out."); return false; }
                if (proc.ExitCode != 0 || !string.IsNullOrWhiteSpace(err))
                {
                    log?.Invoke("Install error: " + (string.IsNullOrWhiteSpace(err) ? outp : err).Trim());
                    return proc.ExitCode == 0;
                }
                log?.Invoke("Package installed.");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke("Install exception: " + ex.Message);
                return false;
            }
        }
    }
}
