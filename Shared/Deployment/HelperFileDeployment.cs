using System;
using System.Collections.Generic;
using System.IO;

namespace Shared.Deployment
{
    /// <summary>
    /// Copies the helper's files from a package folder to the stable location the scheduled task points
    /// at (<c>…\LocalCache\ClawTweaks\Helper</c>), so the task path survives package updates.
    ///
    /// WHY THIS LIVES IN Shared. It used to exist only inside the helper's own startup, which meant the
    /// only thing that could ever deploy was a helper launcher — the one actor that races with itself and
    /// may come out of a package that is already superseded. Measured 2026-08-03: a launcher from the old
    /// package compared its own version against the deployed one, found them equal, skipped the copy, and
    /// started a stale helper; the real deployment then landed underneath the running process.
    ///
    /// Center does not have that problem. It stops every helper first (the handover in
    /// <see cref="Shared.IPC"/>, which old builds already support), installs the package, and only then
    /// copies — with nothing running there are no locked files and nothing to race. The helper keeps its
    /// own deployment path for starts that do not go through Center, but that is now the exception.
    ///
    /// Everything here is plain file IO on the user's own AppData: no elevation, by design.
    /// </summary>
    public static class HelperFileDeployment
    {
        /// <summary>Written LAST, after every file is copied — it is the commit marker for a deployment.</summary>
        public const string VersionFileName = ".version";

        /// <summary>
        /// Files the deployed helper needs. Missing entries are skipped rather than failing the run:
        /// several are optional per device, and a hard failure on an absent optional DLL would block an
        /// otherwise valid deployment.
        /// </summary>
        public static readonly IReadOnlyList<string> RequiredFiles = new[]
        {
            "XboxGamingBarHelper.exe",
            "XboxGamingBarHelper.exe.config",
            "ADLXCSharpBind.dll",
            "libryzenadj.dll",
            "inpoutx64.dll",
            "NLog.config",
            "NLog.dll",
            "Shared.dll",
            // Managed DLLs
            "LibreHardwareMonitorLib.dll",
            "HidSharp.dll",
            "Nefarius.ViGEm.Client.dll",
            "RTSSSharedMemoryNET.dll",
            "RAMSPDToolkit-NDD.dll",
            // Native OSD renderer (Doku/PLAN_Native_OSD.md). SharpDX.dll was already deployed for
            // DirectInput; the Direct2D/DXGI/Mathematics parts are what draws the overlay. Missing any
            // of these does not break the helper - the OSD manager logs once and stays dark - but it
            // does mean the feature silently never appears, which is worse than an error.
            "GameOverlay.dll",
            "SharpDX.Direct2D1.dll",
            "SharpDX.DXGI.dll",
            "SharpDX.Mathematics.dll",
            // System dependencies
            "System.Text.Json.dll",
            "System.Text.Encodings.Web.dll",
            "System.Buffers.dll",
            "System.Memory.dll",
            "System.Numerics.Vectors.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Threading.Tasks.Extensions.dll",
            "System.ValueTuple.dll",
            "System.IO.Pipelines.dll",
            "System.CodeDom.dll",
            "Microsoft.Bcl.AsyncInterfaces.dll"
        };

        /// <summary>
        /// The deployment folder for a package family, derived from the family name alone so a caller
        /// without package identity (Center) can compute it exactly like the packaged helper does.
        /// </summary>
        public static string ResolveHelperFolder(string packageFamilyName)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Packages", packageFamilyName, "LocalCache", "ClawTweaks", "Helper");
        }

        /// <summary>The version a previous deployment committed, or null when there is none.</summary>
        public static string ReadDeployedVersion(string helperFolder)
        {
            try
            {
                string path = Path.Combine(helperFolder, VersionFileName);
                if (File.Exists(path)) return File.ReadAllText(path).Trim();
            }
            catch { /* treated as "no deployment" */ }
            return null;
        }

        /// <summary>Outcome of a deployment run.</summary>
        public sealed class Result
        {
            public bool Success { get; set; }
            public int Copied { get; set; }
            public int Failed { get; set; }
            public string Error { get; set; }
        }

        /// <summary>
        /// Copies every required file from <paramref name="sourceDir"/> into
        /// <paramref name="helperFolder"/> and commits <paramref name="version"/>.
        ///
        /// The version file is written only when the executable itself arrived — committing a version for
        /// a deployment whose main binary failed to copy would tell every later check that the update is
        /// done, which is the single most damaging thing this code could get wrong.
        /// </summary>
        public static Result Deploy(string sourceDir, string helperFolder, string version, Action<string> log = null)
        {
            var result = new Result();
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                {
                    result.Error = $"Source folder not found: {sourceDir}";
                    log?.Invoke(result.Error);
                    return result;
                }

                Directory.CreateDirectory(helperFolder);
                SweepStaleFiles(helperFolder, log);

                bool exeCopied = false;
                foreach (string fileName in RequiredFiles)
                {
                    string sourcePath = Path.Combine(sourceDir, fileName);
                    string targetPath = Path.Combine(helperFolder, fileName);
                    if (!File.Exists(sourcePath)) continue;   // optional per device

                    try
                    {
                        CopyFileWithRetry(sourcePath, targetPath);
                        result.Copied++;
                        if (string.Equals(fileName, "XboxGamingBarHelper.exe", StringComparison.OrdinalIgnoreCase))
                            exeCopied = true;
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        log?.Invoke($"Failed to copy {fileName}: {ex.Message}");
                    }
                }

                // Anything else the build dropped next to the helper. The explicit list above is the
                // contract; this sweep is what keeps a newly added dependency from being forgotten in two
                // places at once.
                try
                {
                    foreach (string dllPath in Directory.GetFiles(sourceDir, "*.dll"))
                    {
                        string fileName = Path.GetFileName(dllPath);
                        bool alreadyHandled = false;
                        foreach (string known in RequiredFiles)
                        {
                            if (string.Equals(known, fileName, StringComparison.OrdinalIgnoreCase))
                            {
                                alreadyHandled = true;
                                break;
                            }
                        }
                        if (alreadyHandled) continue;

                        try
                        {
                            CopyFileWithRetry(dllPath, Path.Combine(helperFolder, fileName));
                            result.Copied++;
                        }
                        catch { /* extras are best-effort */ }
                    }
                }
                catch { /* ditto */ }

                if (!exeCopied)
                {
                    result.Error = "Helper executable was not copied — version not committed.";
                    log?.Invoke(result.Error);
                    return result;
                }

                File.WriteAllText(Path.Combine(helperFolder, VersionFileName), version);
                result.Success = true;
                log?.Invoke($"Deployed helper {version}: {result.Copied} file(s) copied"
                    + (result.Failed > 0 ? $", {result.Failed} failed" : "") + ".");
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                log?.Invoke("Deployment failed: " + ex.Message);
                return result;
            }
        }

        /// <summary>
        /// Removes ".old.&lt;guid&gt;" leftovers. <see cref="CopyFileWithRetry"/> renames a locked target
        /// aside and then tries to delete it, which necessarily fails while a helper still has it mapped —
        /// so leftovers only ever become deletable on a LATER run. Measured: one update over a running
        /// helper left 20 of them.
        /// </summary>
        private static void SweepStaleFiles(string helperFolder, Action<string> log)
        {
            try
            {
                int swept = 0;
                foreach (string stale in Directory.GetFiles(helperFolder, "*.old.*"))
                {
                    try { File.Delete(stale); swept++; } catch { /* still mapped — next time */ }
                }
                if (swept > 0) log?.Invoke($"Swept {swept} stale .old.* file(s) from the deployment folder.");
            }
            catch { /* sweeping is housekeeping, never a reason to abort a deployment */ }
        }

        private static void CopyFileWithRetry(string sourcePath, string targetPath, int maxRetries = 3)
        {
            Exception lastException = null;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(targetPath))
                    {
                        try { File.Delete(targetPath); }
                        catch
                        {
                            // Locked: move it out of the way so the copy can still land on the real name.
                            string tempPath = targetPath + ".old." + Guid.NewGuid().ToString("N").Substring(0, 8);
                            try
                            {
                                File.Move(targetPath, tempPath);
                                try { File.Delete(tempPath); } catch { /* swept on a later run */ }
                            }
                            catch { /* truly locked — fall through and try to overwrite */ }
                        }
                    }

                    File.Copy(sourcePath, targetPath, overwrite: true);

                    // An interrupted copy can leave a 0-byte or short file, and for the helper exe that
                    // leaves the app permanently stuck on "Setup running". A size mismatch counts as a
                    // failure so the retry loop copies again.
                    long srcLen = new FileInfo(sourcePath).Length;
                    long dstLen = new FileInfo(targetPath).Length;
                    if (dstLen != srcLen)
                        throw new IOException($"Copy verification failed for '{Path.GetFileName(targetPath)}': "
                            + $"{dstLen} bytes, expected {srcLen}");

                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (i < maxRetries - 1) System.Threading.Thread.Sleep(100 * (i + 1));
                }
            }

            throw lastException ?? new IOException($"Failed to copy {sourcePath}");
        }
    }
}
