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

            // Release-folder run (msix/cer sit next to the exe, as Build-Setup.ps1 assembles it) →
            // unchanged behavior, straight into the wizard. Standalone run (naked exe) → the Center
            // menu lets the user pick and download a build first; it repoints SetupContext.AssetRoot
            // and opens MainWindow itself once staged.
            bool standalone = PackageInstaller.FindPackage() == null && CertInstaller.FindSiblingCer() == null;
            Window window = standalone ? new CenterMenuWindow() : new MainWindow();
            window.Show();
        }

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
