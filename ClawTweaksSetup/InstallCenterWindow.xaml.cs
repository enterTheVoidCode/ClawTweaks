using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClawTweaksSetup.Core;
using ClawTweaksSetup.Navigation;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup
{
    public enum InstallCenterMode { Install, Update, AlreadyInstalled }

    /// <summary>Installs or updates Center before the main setup flow can run.</summary>
    public partial class InstallCenterWindow : Window
    {
        private XInputNavigator _nav;
        private bool _installing;
        private bool _installSucceeded;
        private readonly InstallCenterMode _mode;

        /// <summary>Resumes installation after the elevated relaunch.</summary>
        public const string ResumeArg = "--resume-install";
        public const string DesktopShortcutArg = "--desktop-shortcut";
        public const string StartMenuShortcutArg = "--start-menu-shortcut";

        public InstallCenterWindow(InstallCenterMode mode, Version installedVersion = null, Version runningVersion = null, bool autoStart = false)
        {
            _mode = mode;
            InitializeComponent();
            ModernWindow.Apply(this);

            var args = Environment.GetCommandLineArgs();
            DesktopShortcutCheckBox.IsChecked = autoStart
                ? args.Contains(DesktopShortcutArg)
                : SelfInstaller.HasDesktopShortcut();
            StartMenuShortcutCheckBox.IsChecked = autoStart
                ? args.Contains(StartMenuShortcutArg)
                : (mode == InstallCenterMode.Install || SelfInstaller.HasStartMenuShortcut());

            switch (mode)
            {
                case InstallCenterMode.Update:
                    TitleText.Text = "Update ClawTweaks Center";
                    DescriptionText.Text = $"Updates the installed copy from {installedVersion} to {runningVersion}.";
                    break;
                case InstallCenterMode.AlreadyInstalled:
                    TitleText.Text = "ClawTweaks Center is already installed";
                    DescriptionText.Text = $"Version {installedVersion} is already installed. Open it from the Start Menu " +
                                            "or the ClawTweaks Game Bar widget instead of running this Setup file again.";
                    ShortcutOptions.Visibility = Visibility.Collapsed;
                    break;
            }

            Loaded += (_, __) =>
            {
                _nav = new XInputNavigator(this);
                _nav.ButtonPressed += b => Dispatcher.Invoke(() =>
                {
                    if (b == PadButton.A && _mode != InstallCenterMode.AlreadyInstalled) RunPrimaryAction();
                    else if (b == PadButton.X) ToggleShortcut(DesktopShortcutCheckBox);
                    else if (b == PadButton.Y) ToggleShortcut(StartMenuShortcutCheckBox);
                    else if (b == PadButton.B) Application.Current.Shutdown();
                });
                _nav.Start();
                RenderActionBar();

                // Continue the action already approved through UAC.
                if (autoStart && _mode != InstallCenterMode.AlreadyInstalled) StartInstall();
            };
            Closed += (_, __) => _nav?.Dispose();

            // Keyboard fallback for desktop testing.
            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) { Application.Current.Shutdown(); e.Handled = true; }
            };
        }

        private void RenderActionBar()
        {
            ActionBar.Children.Clear();
            DesktopShortcutCheckBox.IsEnabled = !_installing && !_installSucceeded;
            StartMenuShortcutCheckBox.IsEnabled = !_installing && !_installSucceeded;

            // Do not turn a downloaded Setup file into an implicit app launcher.
            if (_mode != InstallCenterMode.AlreadyInstalled)
            {
                string label = _installSucceeded
                    ? "Open CTW"
                    : (_mode == InstallCenterMode.Update ? "Update" : "Install");
                ActionBar.Children.Add(ActionBarBuilder.BuildChip(PadButton.A, label, !_installing, RunPrimaryAction));
            }

            // Keep Exit available during installation.
            ActionBar.Children.Add(ActionBarBuilder.BuildChip(PadButton.B, "Exit", true, () => Application.Current.Shutdown()));
        }

        private void RunPrimaryAction()
        {
            if (_installSucceeded) OpenInstalledCenter();
            else StartInstall();
        }

        private void ToggleShortcut(CheckBox option)
        {
            if (_installing || _installSucceeded || _mode == InstallCenterMode.AlreadyInstalled) return;
            option.IsChecked = option.IsChecked != true;
        }

        private void StartInstall()
        {
            if (_installing) return;
            _installing = true;
            RenderActionBar();

            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = _mode == InstallCenterMode.Update ? "Updating..." : "Installing...";

            // Program Files and registry writes require an elevated relaunch.
            // Exclude argv[0]; the elevation gate supplies the executable path.
            var realArgs = Environment.GetCommandLineArgs().Skip(1)
                .Where(a => a != ResumeArg && a != DesktopShortcutArg && a != StartMenuShortcutArg)
                .Append(ResumeArg)
                .ToArray();
            if (DesktopShortcutCheckBox.IsChecked == true)
                realArgs = realArgs.Append(DesktopShortcutArg).ToArray();
            if (StartMenuShortcutCheckBox.IsChecked == true)
                realArgs = realArgs.Append(StartMenuShortcutArg).ToArray();
            if (!ElevationGate.EnsureElevatedOrRelaunch(realArgs))
            {
                _installing = false;
                StatusText.Foreground = UiHelpers.Error;
                StatusText.Text = "Administrator rights are required to " +
                                   (_mode == InstallCenterMode.Update ? "update" : "install") + ".";
                RenderActionBar();
                return;
            }

            bool ok = SelfInstaller.Install(
                DesktopShortcutCheckBox.IsChecked == true,
                StartMenuShortcutCheckBox.IsChecked == true,
                msg => Dispatcher.Invoke(() => StatusText.Text = msg));
            if (ok)
            {
                _installing = false;
                _installSucceeded = true;
                TitleText.Text = _mode == InstallCenterMode.Update
                    ? "ClawTweaks Center updated"
                    : "ClawTweaks Center installed";
                DescriptionText.Text = "Setup completed successfully. Open CTW when you're ready.";
                StatusText.Foreground = UiHelpers.Ok;
                StatusText.Text = _mode == InstallCenterMode.Update
                    ? "Update successful."
                    : "Installation successful.";
                ShortcutOptions.Visibility = Visibility.Collapsed;
                RenderActionBar();
            }
            else
            {
                _installing = false;
                StatusText.Foreground = UiHelpers.Error;
                StatusText.Text = (_mode == InstallCenterMode.Update ? "Update" : "Install") + " failed — see the log for details. Try again, or run as Administrator.";
                RenderActionBar();
            }
        }

        private void OpenInstalledCenter()
        {
            bool launched = SelfInstaller.LaunchInstalled(msg => Dispatcher.Invoke(() => StatusText.Text = msg));
            if (launched)
            {
                Application.Current.Shutdown();
                return;
            }

            StatusText.Foreground = UiHelpers.Error;
            RenderActionBar();
        }
    }
}
