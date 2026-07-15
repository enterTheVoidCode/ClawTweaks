using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClawTweaksSetup.Core;
using ClawTweaksSetup.Navigation;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup.Phases
{
    /// <summary>
    /// Phase 3 — install the app. One guided Ⓐ action does the whole thing in order: trust the
    /// signing certificate, install the MSIX (+dependencies), open the Game Bar, then wait for the
    /// helper to come up (with a progress bar). Idempotent — safe to re-run on an update.
    /// </summary>
    public sealed class InstallPhase : PhaseBase
    {
        private readonly StackPanel _root = new StackPanel();
        private readonly List<PhaseAction> _actions;
        private bool _busy;
        private bool _canInstall;

        private readonly ProgressBar _progress = new ProgressBar
        {
            Height = 14, Minimum = 0, Maximum = 100, Value = 0,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x38)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        private readonly TextBlock _log = new TextBlock
        {
            FontSize = 15, Foreground = UiHelpers.Subtle,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0),
        };

        public override string Title => "Install";
        public override IReadOnlyList<PhaseAction> Actions => _actions;

        public InstallPhase()
        {
            Content = _root;
            _actions = new List<PhaseAction>
            {
                new PhaseAction(PadButton.A, "Install ClawTweaks", () => _ = InstallAsync(), () => !_busy && _canInstall),
                new PhaseAction(PadButton.Y, "Re-check", () => _ = RefreshAsync(), () => !_busy),
            };
        }

        public override void OnEnter() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            State = PhaseState.Working;
            _canInstall = false;
            RaiseActionsChanged();

            string cer = null; string thumb = null; bool certTrusted = false;
            string pkg = null; bool helper = false;
            await Task.Run(() =>
            {
                cer = CertInstaller.FindSiblingCer();
                if (cer != null) { thumb = CertInstaller.ThumbprintOf(cer); certTrusted = CertInstaller.IsTrusted(thumb); }
                pkg = PackageInstaller.FindPackage();
                helper = HelperControl.HelperRunning();
            });

            _root.Children.Clear();
            _root.Children.Add(UiHelpers.Title("Install ClawTweaks"));
            _root.Children.Add(UiHelpers.Body(
                "Trusts the signing certificate, installs the app package, opens the Game Bar and waits " +
                "for the helper. Safe to run again on an update."));
            _root.Children.Add(UiHelpers.Caption($"Last checked {DateTime.Now:HH:mm:ss}"));

            // Certificate row
            if (cer == null)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Warning, "Signing certificate",
                    "No .cer bundled with this setup build (dev run)."));
            else if (certTrusted)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Ok, "Signing certificate", "Trusted."));
            else
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Info, "Signing certificate",
                    "Not trusted yet — will be trusted during install."));

            // Package row
            if (pkg == null)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Warning, "App package",
                    "No .msix bundled with this setup build (dev run)."));
            else
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Info, "App package",
                    System.IO.Path.GetFileName(pkg)));

            // Helper row
            _root.Children.Add(helper
                ? UiHelpers.StatusRow(StatusKind.Ok, "Helper", "Running.")
                : UiHelpers.StatusRow(StatusKind.Info, "Helper", "Not running yet — starts after install via the Game Bar."));

            _canInstall = pkg != null;

            if (_canInstall)
                _root.Children.Add(UiHelpers.Body("Press Ⓐ to install ClawTweaks."));
            else
                _root.Children.Add(UiHelpers.Body(
                    "This is a scaffold build without a bundled package, so there's nothing to install here. " +
                    "In a real setup bundle the .msix and .cer sit next to this exe."));

            _root.Children.Add(_progress);
            if (_log.Text.Length > 0) _root.Children.Add(_log);

            // Ok if the package is already installed and the helper is up; otherwise Action (may continue
            // in scaffold, or after a successful install this flips to Ok).
            State = (pkg == null) ? PhaseState.Action : PhaseState.Action;

            _busy = false;
            RaiseActionsChanged();
        }

        private async Task InstallAsync()
        {
            if (_busy) return;
            _busy = true;
            State = PhaseState.Working;
            _log.Text = "";
            _progress.Visibility = Visibility.Visible;
            _progress.IsIndeterminate = true;
            RaiseActionsChanged();

            void Log(string s) => Dispatcher.Invoke(() =>
            {
                _log.Text += (_log.Text.Length > 0 ? "\n" : "") + s;
                if (!_root.Children.Contains(_log)) _root.Children.Add(_log);
            });

            bool ok = true;

            // 1) Certificate
            string cer = CertInstaller.FindSiblingCer();
            if (cer != null)
            {
                string thumb = CertInstaller.ThumbprintOf(cer);
                if (!CertInstaller.IsTrusted(thumb))
                {
                    Log("Trusting signing certificate…");
                    ok &= await Task.Run(() => CertInstaller.Install(cer));
                }
                else Log("Certificate already trusted.");
            }

            // 2) Package
            string pkg = PackageInstaller.FindPackage();
            if (ok && pkg != null)
            {
                var deps = PackageInstaller.FindDependencies(pkg);
                ok &= await Task.Run(() => PackageInstaller.Install(pkg, deps, Log));
            }

            // 3) Game Bar + helper wait
            if (ok)
            {
                Log("Opening Game Bar — the ClawTweaks widget will start the helper…");
                HelperControl.OpenGameBar();

                Dispatcher.Invoke(() => { _progress.IsIndeterminate = false; _progress.Value = 0; });
                var progress = new Progress<int>(p => Dispatcher.Invoke(() => _progress.Value = p));
                bool up = await HelperControl.WaitForHelperAsync(60000, progress);
                Log(up ? "Helper is up." : "Helper did not appear in time — open the Game Bar (Win+G) manually.");
                ok &= up;
            }

            Dispatcher.Invoke(() => _progress.Visibility = Visibility.Collapsed);

            if (ok) { State = PhaseState.Ok; _busy = false; RaiseActionsChanged(); }
            else { _busy = false; await RefreshAsync(); }
        }
    }
}
