using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Installs CTW_Center.exe itself as a regular Windows app (Program Files + Start Menu shortcut +
    /// Add/Remove Programs entry) instead of running as a portable exe from wherever it was extracted.
    /// This is the gate the rest of the app (widget MSIX install, onboarding) sits behind: nothing else
    /// runs until Center is running from its installed location.
    ///
    /// No new build dependency (no Inno/WiX) — copy-self-and-relaunch, same elevation the app.manifest
    /// already requests. Uninstall is registered as a real Add/Remove Programs entry that calls back
    /// into the installed exe with --uninstall.
    /// </summary>
    public static class SelfInstaller
    {
        private const string AppDisplayName = "ClawTweaks Center";
        private const string UninstallKeyName = "ClawTweaksCenter";
        private const string ExeName = "CTW_Center.exe";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppDisplayName);

        private static string InstalledExePath => Path.Combine(InstallDir, ExeName);

        private static string UninstallRegistryKey =>
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{UninstallKeyName}";

        /// <summary>True when the currently running exe already lives in <see cref="InstallDir"/>.</summary>
        public static bool IsRunningFromInstallDir()
        {
            string current = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            return string.Equals(
                current?.TrimEnd('\\'),
                InstallDir.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when a previous run already installed Center to <see cref="InstallDir"/>,
        /// regardless of what version.</summary>
        public static bool IsInstalled() => File.Exists(InstalledExePath);

        /// <summary>Version of the currently INSTALLED exe (read straight off the file, not the
        /// registry — can't drift out of sync with what's actually there), or null if not installed.</summary>
        public static Version GetInstalledVersion()
        {
            try
            {
                if (!File.Exists(InstalledExePath)) return null;
                var info = FileVersionInfo.GetVersionInfo(InstalledExePath);
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            }
            catch { return null; }
        }

        /// <summary>Launches the already-installed copy as-is (no copy/relaunch dance) — used when the
        /// running exe is the same version or older than what's already installed, so there's nothing
        /// to install or update. Caller should shut down right after calling this.</summary>
        public static void LaunchInstalledAndExit(Action<string> log = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(InstalledExePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                log?.Invoke($"Could not launch the installed copy: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies the running exe to <see cref="InstallDir"/>, creates a Start Menu shortcut, registers
        /// an Add/Remove Programs entry, then launches the installed copy and exits this process.
        /// Caller should not do anything after this returns true — the app is about to shut down.
        /// </summary>
        public static bool InstallAndRelaunch(Action<string> log = null)
        {
            try
            {
                string sourceExe = Process.GetCurrentProcess().MainModule.FileName;
                string sourceDir = Path.GetDirectoryName(sourceExe);

                log?.Invoke($"Installing to {InstallDir}...");
                Directory.CreateDirectory(InstallDir);
                File.Copy(sourceExe, InstalledExePath, overwrite: true);

                // Release-folder run (msix + cer + Dependencies sit next to the exe, as Build-Setup.ps1
                // assembles it) — bring those along too, so PackageInstaller/CertInstaller still find
                // them via AssetRoot (= the exe's own directory) after relaunching from Program Files.
                // A standalone/portable run has none of these next to it — nothing to copy, the normal
                // CenterMenuWindow browse-and-download path (staging into %TEMP%) takes over instead.
                CopySiblingIfPresent(sourceDir, "*.msix");
                CopySiblingIfPresent(sourceDir, "*.msixbundle");
                CopySiblingIfPresent(sourceDir, "*.cer");
                CopySiblingIfPresent(sourceDir, "Setup-Tools.ps1");
                CopySiblingDirIfPresent(sourceDir, "Dependencies");

                CreateStartMenuShortcut();
                RegisterUninstallEntry();

                log?.Invoke("Relaunching from install location...");
                Process.Start(new ProcessStartInfo(InstalledExePath) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Install failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes the Start Menu shortcut and Add/Remove Programs entry immediately, then spawns a
        /// short-lived cmd.exe that waits for this process to exit and deletes the install folder — a
        /// running exe cannot delete its own file, so the actual folder cleanup happens after exit.
        /// </summary>
        public static void Uninstall()
        {
            try
            {
                string shortcut = StartMenuShortcutPath();
                if (File.Exists(shortcut)) File.Delete(shortcut);
            }
            catch { }

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(UninstallRegistryKey, throwOnMissingSubKey: false);
            }
            catch { }

            try
            {
                // A running exe can't delete its own file, and this process exits right after
                // spawning this (via Application.Current.Shutdown() in the caller) — a fixed short
                // delay is plenty and, unlike a tasklist-polling loop, doesn't depend on `goto`
                // jumping to a label defined inside a parenthesized block, which cmd.exe handles
                // unreliably (that's what silently left the folder behind on the first test).
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C \"timeout /t 2 /nobreak >nul & rmdir /S /Q \"{InstallDir}\"\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                Process.Start(psi);
            }
            catch { }
        }

        private static void CopySiblingIfPresent(string sourceDir, string searchPattern)
        {
            foreach (string file in Directory.GetFiles(sourceDir, searchPattern))
                File.Copy(file, Path.Combine(InstallDir, Path.GetFileName(file)), overwrite: true);
        }

        private static void CopySiblingDirIfPresent(string sourceDir, string dirName)
        {
            string src = Path.Combine(sourceDir, dirName);
            if (!Directory.Exists(src)) return;

            string dest = Path.Combine(InstallDir, dirName);
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(src, file);
                string destFile = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile));
                File.Copy(file, destFile, overwrite: true);
            }
        }

        private static string StartMenuShortcutPath()
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            return Path.Combine(startMenu, "Programs", $"{AppDisplayName}.lnk");
        }

        /// <summary>
        /// Creates a .lnk via the WScript.Shell COM object (no extra NuGet package — ships with
        /// Windows). Machine-wide (CommonStartMenu) since the app already runs elevated.
        /// </summary>
        private static void CreateStartMenuShortcut()
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic shortcut = shell.CreateShortcut(StartMenuShortcutPath());
                try
                {
                    shortcut.TargetPath = InstalledExePath;
                    shortcut.WorkingDirectory = InstallDir;
                    shortcut.Description = AppDisplayName;
                    shortcut.Save();
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void RegisterUninstallEntry()
        {
            using var key = Registry.LocalMachine.CreateSubKey(UninstallRegistryKey);
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

            key.SetValue("DisplayName", AppDisplayName);
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "ClawTweaks");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("DisplayIcon", InstalledExePath);
            key.SetValue("UninstallString", $"\"{InstalledExePath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            try
            {
                long sizeKb = new FileInfo(InstalledExePath).Length / 1024;
                key.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
