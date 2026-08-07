using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ClawTweaksSetup.Core;

namespace ClawTweaksSetup
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // The registered uninstall callback requires elevation and must not open a window.
            if (Array.Exists(e.Args, a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                if (ElevationGate.EnsureElevatedOrRelaunch(e.Args))
                {
                    SelfInstaller.Uninstall();
                }
                Shutdown();
                return;
            }

            ApplyDebugDeviceOverride(e.Args);

            // Log recoverable dispatcher failures instead of terminating the UI.
            DispatcherUnhandledException += (_, ex) =>
            {
                LogCrash(ex.Exception);
                ex.Handled = true;
            };

#if DEBUG
            // Debug-only visual QA bypasses the self-install gate.
            if (Array.Exists(e.Args, a => a.Equals("--preview-center", StringComparison.OrdinalIgnoreCase)))
            {
                ShowForeground(new CenterMenuWindow());
                return;
            }
            if (Array.Exists(e.Args, a => a.Equals("--preview-wizard", StringComparison.OrdinalIgnoreCase)))
            {
                ShowForeground(new MainWindow(e.Args));
                return;
            }
            if (Array.Exists(e.Args, a => a.Equals("--preview-install", StringComparison.OrdinalIgnoreCase)))
            {
                ShowForeground(new CenterMenuWindow(previewInstall: true));
                return;
            }
#endif

            // Install Center before allowing the widget or onboarding flow.
            // Portable instances never open the installed flow themselves.
            if (!SelfInstaller.IsRunningFromInstallDir())
            {
                var installedVersion = SelfInstaller.GetInstalledVersion();
                var runningVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

                InstallCenterMode mode;
                if (installedVersion == null) mode = InstallCenterMode.Install;
                else if (installedVersion < runningVersion) mode = InstallCenterMode.Update;
                else mode = InstallCenterMode.AlreadyInstalled;

                // Setup never launches a different installed executable implicitly.
                // Resume only after the user initiated and approved elevation.
                bool autoStart = Array.Exists(e.Args, a => a == InstallCenterWindow.ResumeArg);
                ShowForeground(new InstallCenterWindow(mode, installedVersion, runningVersion, autoStart));
                return;
            }

            // Release folders enter the wizard; standalone executables open the build picker.
            bool standalone = PackageInstaller.FindPackage() == null && CertInstaller.FindSiblingCer() == null;
            Window window = standalone ? new CenterMenuWindow() : new MainWindow(e.Args);
            ShowForeground(window);
        }

        /// <summary>
        /// Shows and activates a window after the launcher grants foreground access.
        /// A temporary Topmost toggle handles launches where that grant is unavailable.
        /// </summary>
        private static void ShowForeground(Window window)
        {
            window.Show();
            try
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;

                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
                window.Focus();

                var hWnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hWnd != IntPtr.Zero) SetForegroundWindow(hWnd);
            }
            catch (Exception ex)
            {
                // Focus promotion is best-effort.
                LogCrash(ex);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static void LogCrash(Exception ex)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "ClawTweaksCenter_crash.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }

        /// <summary>Applies a debug-only device override for UI testing.</summary>
        private static void ApplyDebugDeviceOverride(string[] args)
        {
            foreach (var arg in args)
            {
                string v = arg.Trim().TrimStart('-', '/').ToLowerInvariant();
                if (v.StartsWith("device=")) v = v.Substring("device=".Length);
                else continue;

                switch (v)
                {
                    case "8ai": case "a2vm":
                        DeviceDetect.DebugOverrideModel = DeviceDetect.Model.A2VM;
                        return;
                    case "8ex": case "ex": case "cg3em":
                        DeviceDetect.DebugOverrideModel = DeviceDetect.Model.Ex;
                        return;
                    case "unknown": case "none":
                        DeviceDetect.DebugOverrideModel = DeviceDetect.Model.Unknown;
                        return;
                }
            }
        }
    }
}
