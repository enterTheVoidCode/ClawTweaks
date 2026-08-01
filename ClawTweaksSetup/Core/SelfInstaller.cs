using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ClawTweaksSetup.Core
{
    /// <summary>Installs and registers Center before widget setup or onboarding.</summary>
    public static class SelfInstaller
    {
        private const string AppDisplayName = "ClawTweaks Center";
        private const string UninstallKeyName = "ClawTweaksCenter";
        private const string ExeName = "CTW_Center.exe";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppDisplayName);

        private static string InstalledExePath => Path.Combine(InstallDir, ExeName);

        public static bool HasDesktopShortcut() => File.Exists(DesktopShortcutPath());
        public static bool HasStartMenuShortcut() => File.Exists(StartMenuShortcutPath());

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

        /// <summary>Returns whether Center exists in <see cref="InstallDir"/>.</summary>
        public static bool IsInstalled() => File.Exists(InstalledExePath);

        /// <summary>Reads the installed executable version, or returns null.</summary>
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

        /// <summary>Launches the installed copy and reports failures through <paramref name="log"/>.</summary>
        public static bool LaunchInstalled(Action<string> log = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(InstalledExePath) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Could not launch the installed copy: {ex.Message}");
                return false;
            }
        }

        /// <summary>Copies Center and its assets, applies shortcuts, and registers uninstall.</summary>
        public static bool Install(bool createDesktopShortcut, bool createStartMenuShortcut, Action<string> log = null)
        {
            try
            {
                string sourceExe = Process.GetCurrentProcess().MainModule.FileName;
                string sourceDir = Path.GetDirectoryName(sourceExe);

                log?.Invoke($"Installing to {InstallDir}...");
                Directory.CreateDirectory(InstallDir);
                File.Copy(sourceExe, InstalledExePath, overwrite: true);

                // Preserve sibling release assets; standalone runs have none and use staging.
                CopySiblingIfPresent(sourceDir, "*.msix");
                CopySiblingIfPresent(sourceDir, "*.msixbundle");
                CopySiblingIfPresent(sourceDir, "*.cer");
                CopySiblingIfPresent(sourceDir, "Setup-Tools.ps1");
                CopySiblingDirIfPresent(sourceDir, "Dependencies");

                ApplyShortcutChoice(DesktopShortcutPath(), createDesktopShortcut);
                ApplyShortcutChoice(StartMenuShortcutPath(), createStartMenuShortcut);
                RegisterUninstallEntry();

                log?.Invoke("ClawTweaks Center installed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Install failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Removes registration and shortcuts, then deletes the install after exit.</summary>
        public static void Uninstall()
        {
            try
            {
                string shortcut = DesktopShortcutPath();
                if (File.Exists(shortcut)) File.Delete(shortcut);
            }
            catch { }

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
                // Delay deletion because the running executable cannot remove itself.
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

        private static string DesktopShortcutPath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, $"{AppDisplayName}.lnk");
        }

        /// <summary>Creates or removes a shortcut using Windows Script Host.</summary>
        private static void ApplyShortcutChoice(string shortcutPath, bool create)
        {
            if (!create)
            {
                if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
            CreateShortcut(shortcutPath);
        }

        private static void CreateShortcut(string shortcutPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
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
