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

            // Uninstall callback (registered as the Add/Remove Programs UninstallString) — clean up
            // and exit immediately, never reaching any window. Removing from Program Files and the
            // registry needs admin; Center is asInvoker now, so unlike before this is not automatic —
            // relaunch elevated with the same --uninstall arg if needed (same gate as the install path).
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

            // A gamepad-driven UI (live XInput polling, background fetches, remote images in the
            // release notes) has plenty of surface for a rare, hard-to-repro exception somewhere deep
            // in a WPF layout/render pass. None of that should be able to take the whole app down —
            // log it so a recurrence is diagnosable, and keep going instead of crashing.
            DispatcherUnhandledException += (_, ex) =>
            {
                LogCrash(ex.Exception);
                ex.Handled = true;
            };

            // Gate #0: Center must be running from its installed location (Program Files) before
            // anything else — including the widget MSIX — can be installed. A naked/portable run
            // shows the install-self prompt and relaunches from there; this window never opens the
            // rest of the app itself.
            if (!SelfInstaller.IsRunningFromInstallDir())
            {
                var installedVersion = SelfInstaller.GetInstalledVersion();
                var runningVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

                InstallCenterMode mode;
                if (installedVersion == null) mode = InstallCenterMode.Install;
                else if (installedVersion < runningVersion) mode = InstallCenterMode.Update;
                else mode = InstallCenterMode.AlreadyInstalled;

                // All three modes show a window and require an explicit user action before anything
                // happens — running a Setup file must never silently do something other than what was
                // double-clicked (AlreadyInstalled points the user at the Start Menu / Game Bar widget
                // instead of launching a different already-installed copy out from under them).
                //
                // autoStart: set only when this launch IS the elevated relaunch triggered by that
                // explicit click (see InstallCenterWindow.ResumeArg / ElevationGate) — the user already
                // acted once and already granted the UAC prompt, so this proceeds straight into the
                // install instead of making them click Install/Update a second time for no extra signal.
                bool autoStart = Array.Exists(e.Args, a => a == InstallCenterWindow.ResumeArg);
                ShowForeground(new InstallCenterWindow(mode, installedVersion, runningVersion, autoStart));
                return;
            }

            // Release-folder run (msix/cer sit next to the exe, as Build-Setup.ps1 assembles it) →
            // unchanged behavior, straight into the wizard. Standalone run (naked exe) → the Center
            // menu lets the user pick and download a build first; it repoints SetupContext.AssetRoot
            // and opens MainWindow itself once staged.
            bool standalone = PackageInstaller.FindPackage() == null && CertInstaller.FindSiblingCer() == null;
            Window window = standalone ? new CenterMenuWindow() : new MainWindow(e.Args);
            ShowForeground(window);
        }

        /// <summary>
        /// Shows a window and actually brings it to the front.
        ///
        /// Plain Show() is not enough when Center is started from the widget: the Game Bar owns the
        /// foreground at that moment, and Windows refuses to let another process take it - the window
        /// opens BEHIND everything and only the taskbar button flashes. The launching side hands over
        /// the right first (helper: AllowSetForegroundWindow, see Program.HotkeyHandlers.cs), and this
        /// is the other half that claims it. Both are needed; either alone does nothing.
        ///
        /// The brief Topmost flip is the fallback for the case where the grant did not arrive (started
        /// from the Start menu while a fullscreen game is up, say). It raises the window without
        /// leaving it permanently on top, which would be worse than the original problem.
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
                // Never let a focus tweak stop the window from being usable.
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

        /// <summary>
        /// Debug-only: --device=8ai or --device=8ex lets the Center's device-specific UI (icon,
        /// per-device version gating) be exercised without the actual hardware.
        /// </summary>
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
