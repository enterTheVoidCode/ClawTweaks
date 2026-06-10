using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace XboxGamingBarHelper.Setup
{
    /// <summary>
    /// Runs the proven prerequisite check/installer (the embedded <c>Setup-Tools.ps1</c>) that
    /// detects + installs all four required tools in one pass. The script is the same detection/
    /// install logic that shipped in the original ClawTweaks installer, extracted into a standalone
    /// non-interactive script.
    ///
    /// The script is shipped as an embedded resource inside this (signed) helper rather than as a
    /// loose .ps1 in the installer ZIP — so the distributed package contains only signed binaries
    /// (a static AV scan never sees a download-and-execute script), while the well-tested logic is
    /// still what runs at install time. The helper already runs elevated, so no UAC prompt appears.
    /// </summary>
    internal static class ToolSetupRunner
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string ResourceSuffix = "Setup-Tools.ps1";

        /// <summary>
        /// Extracts the embedded script and runs it via powershell.exe (elevated, since the helper
        /// is elevated). Blocks until the script finishes. Returns the script's exit code
        /// (0 = all tools present), or -1 if it could not be started.
        ///
        /// <paramref name="only"/> limits the run to a single tool ("pawnio" | "vigem" | "hidhide" |
        /// "rtss") for the per-tool install buttons; null/empty checks and installs all four. Routing
        /// per-tool installs through this embedded script keeps the download-and-execute logic out of
        /// the compiled assembly (no WebClient/Process.Start-exe pattern), which is what some AV
        /// engines flag as a .NET "downloader".
        /// </summary>
        public static int Run(string only = null)
        {
            string scriptPath = null;
            try
            {
                scriptPath = ExtractScript();
                if (scriptPath == null)
                {
                    Logger.Error("ToolSetupRunner: embedded Setup-Tools.ps1 not found.");
                    return -1;
                }

                Logger.Info($"ToolSetupRunner: running tool setup script: {scriptPath}" +
                            (string.IsNullOrEmpty(only) ? "" : $" (only={only})"));

                string onlyArg = string.IsNullOrEmpty(only) ? "" : $" -Only {only}";
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "WindowsPowerShell", "v1.0", "powershell.exe"),
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"{onlyArg}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using (var proc = new Process { StartInfo = psi })
                {
                    proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Logger.Info($"[setup] {e.Data}"); };
                    proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Logger.Warn($"[setup] {e.Data}"); };
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    // Tool downloads + driver installs can take a while; allow up to 15 minutes.
                    if (!proc.WaitForExit(15 * 60 * 1000))
                    {
                        Logger.Warn("ToolSetupRunner: setup script timed out after 15 minutes.");
                        try { proc.Kill(); } catch { }
                        return -1;
                    }
                    int code = proc.ExitCode;
                    Logger.Info($"ToolSetupRunner: setup script finished with exit code {code}.");
                    return code;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ToolSetupRunner.Run failed: {ex.Message}");
                return -1;
            }
            finally
            {
                if (scriptPath != null)
                {
                    try { File.Delete(scriptPath); } catch { }
                }
            }
        }

        private static string ExtractScript()
        {
            var asm = Assembly.GetExecutingAssembly();
            string name = asm.GetManifestResourceNames()
                             .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;

            using (var stream = asm.GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string content = reader.ReadToEnd();
                string outPath = Path.Combine(Path.GetTempPath(), "ClawTweaks_SetupTools.ps1");
                // Write WITH a UTF-8 BOM: Windows PowerShell 5.1 reads a BOM-less file as the system
                // ANSI codepage, which mangles any non-ASCII byte and breaks parsing. The BOM makes it
                // read as UTF-8. (The script itself is kept ASCII-only as a belt-and-braces measure.)
                File.WriteAllText(outPath, content, new UTF8Encoding(true));
                return outPath;
            }
        }
    }
}
