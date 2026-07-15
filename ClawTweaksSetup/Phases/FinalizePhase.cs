using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ClawTweaksSetup.Core;
using ClawTweaksSetup.Navigation;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup.Phases
{
    /// <summary>
    /// Phase 4 — finalization, AFTER the app is installed. This is where MSI Center M is handled (as
    /// agreed: not before install). Offers to close Center M now, shows a final controller-health
    /// summary, and lets the user finish. Ⓐ closes Center M; Ⓨ re-checks.
    /// </summary>
    public sealed class FinalizePhase : PhaseBase
    {
        private readonly StackPanel _root = new StackPanel();
        private readonly List<PhaseAction> _actions;
        private bool _busy;
        private bool _centerMRunning;
        private readonly TextBlock _log = new TextBlock
        {
            FontSize = 15, Foreground = UiHelpers.Subtle,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0),
        };

        public override string Title => "Finish";
        public override IReadOnlyList<PhaseAction> Actions => _actions;

        public FinalizePhase()
        {
            Content = _root;
            _actions = new List<PhaseAction>
            {
                new PhaseAction(PadButton.A, "Close MSI Center M", CloseCenterM, () => !_busy && _centerMRunning),
                new PhaseAction(PadButton.Y, "Re-check", () => _ = RefreshAsync(), () => !_busy),
            };
        }

        public override void OnEnter() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            State = PhaseState.Working;
            RaiseActionsChanged();

            bool cmRunning = false; bool cmInstalled = false; HealthResult health = null;
            await Task.Run(() =>
            {
                cmRunning = CenterM.IsRunning();
                cmInstalled = CenterM.IsInstalled();
                health = ControllerHealth.Probe();
            });
            _centerMRunning = cmRunning;

            _root.Children.Clear();
            _root.Children.Add(UiHelpers.Title("Almost done"));
            _root.Children.Add(UiHelpers.Body(
                "Final clean-up and check. MSI Center M is handled here (after installation) because it " +
                "fights ClawTweaks for the controller and LED."));
            _root.Children.Add(UiHelpers.Caption($"Last checked {DateTime.Now:HH:mm:ss}"));

            // MSI Center M
            if (cmRunning)
            {
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Warning, "MSI Center M",
                    "Running. Press Ⓐ to close it now. For a permanent fix, uninstall it from Windows " +
                    "Settings › Apps and reinstall the latest version if you still need it."));
                State = PhaseState.Action;
            }
            else if (cmInstalled)
            {
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Info, "MSI Center M",
                    "Installed but not running (fine). Uninstall it from Windows Settings › Apps if you " +
                    "want it gone permanently."));
                State = PhaseState.Ok;
            }
            else
            {
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Ok, "MSI Center M", "Not present (good)."));
                State = PhaseState.Ok;
            }

            // Final controller summary
            var kind = health.Verdict == HealthVerdict.Clean ? StatusKind.Ok
                     : health.Verdict == HealthVerdict.Problem ? StatusKind.Error : StatusKind.Warning;
            string summary = health.Verdict == HealthVerdict.Clean
                ? "Controller looks clean."
                : (health.Problems.Count > 0 ? health.Problems[0]
                   : (health.Warnings.Count > 0 ? health.Warnings[0] : "See the Controller step for details."));
            _root.Children.Add(UiHelpers.StatusRow(kind, "Final controller check", summary));

            if (_log.Text.Length > 0) _root.Children.Add(_log);

            // Finishing is always allowed here (Center M closing is recommended, not mandatory).
            if (State == PhaseState.Working) State = PhaseState.Ok;

            _busy = false;
            RaiseActionsChanged();
        }

        private void CloseCenterM() => _ = CloseAsync();

        private async Task CloseAsync()
        {
            if (_busy) return;
            _busy = true;
            State = PhaseState.Working;
            _log.Text = "";
            RaiseActionsChanged();

            void Log(string s) => Dispatcher.Invoke(() => { _log.Text += (_log.Text.Length > 0 ? "\n" : "") + s; });
            await Task.Run(() => CenterM.CloseNow(Log));
            await RefreshAsync();
        }
    }
}
