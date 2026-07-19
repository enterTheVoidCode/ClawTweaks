using System;
using System.Windows;
using ClawTweaksSetup.Core;
using ClawTweaksSetup.Navigation;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup
{
    public enum InstallCenterMode { Install, Update, AlreadyInstalled }

    /// <summary>
    /// Gate shown before anything else when the running exe is not yet installed to
    /// <see cref="SelfInstaller.InstallDir"/> — see App.xaml.cs, which picks the mode:
    ///   Install           — never installed before.
    ///   Update            — this exe is a genuinely newer build than what's installed.
    ///   AlreadyInstalled  — this exe is the same version or OLDER than what's installed; nothing to
    ///                       do here except point the user at the real installed copy (Start Menu /
    ///                       Game Bar widget) instead of silently launching something else out from
    ///                       under a double-click on a Setup exe they downloaded.
    /// Installs/updates Center as a regular Windows app, then relaunches from there — the normal
    /// CenterMenuWindow/MainWindow flow only ever runs from the installed location, so the widget
    /// MSIX can never be installed before Center itself is.
    /// </summary>
    public partial class InstallCenterWindow : Window
    {
        private XInputNavigator _nav;
        private bool _installing;
        private readonly InstallCenterMode _mode;

        public InstallCenterWindow(InstallCenterMode mode, Version installedVersion = null, Version runningVersion = null)
        {
            _mode = mode;
            InitializeComponent();

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
                    break;
            }

            Loaded += (_, __) =>
            {
                _nav = new XInputNavigator(this);
                _nav.ButtonPressed += b => Dispatcher.Invoke(() =>
                {
                    if (b == PadButton.A && _mode != InstallCenterMode.AlreadyInstalled) StartInstall();
                    else if (b == PadButton.B) Application.Current.Shutdown();
                });
                _nav.Start();
                RenderActionBar();
            };
            Closed += (_, __) => _nav?.Dispose();

            // Keyboard fallback for desk testing, same convention as the other windows.
            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) { Application.Current.Shutdown(); e.Handled = true; }
            };
        }

        private void RenderActionBar()
        {
            ActionBar.Children.Clear();

            // AlreadyInstalled deliberately offers NO shortcut to launch the app from here — the
            // point is to teach the user that Center is a real installed Windows app now, opened via
            // the Start Menu or the Game Bar widget, not by re-running a downloaded Setup file.
            if (_mode != InstallCenterMode.AlreadyInstalled)
            {
                string label = _mode == InstallCenterMode.Update ? "Update" : "Install";
                ActionBar.Children.Add(ActionBarBuilder.BuildChip(PadButton.A, label, !_installing, StartInstall));
            }

            // Always available, even mid-install-attempt — the user must never be stuck on this
            // screen with no way out.
            ActionBar.Children.Add(ActionBarBuilder.BuildChip(PadButton.B, "Exit", true, () => Application.Current.Shutdown()));
        }

        private void StartInstall()
        {
            if (_installing) return;
            _installing = true;
            RenderActionBar();

            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = _mode == InstallCenterMode.Update ? "Updating..." : "Installing...";

            bool ok = SelfInstaller.InstallAndRelaunch(msg => Dispatcher.Invoke(() => StatusText.Text = msg));
            if (ok)
            {
                // A new process is already starting from the installed location; this one is done.
                Application.Current.Shutdown();
            }
            else
            {
                _installing = false;
                StatusText.Foreground = UiHelpers.Error;
                StatusText.Text = (_mode == InstallCenterMode.Update ? "Update" : "Install") + " failed — see the log for details. Try again, or run as Administrator.";
                RenderActionBar();
            }
        }
    }
}
