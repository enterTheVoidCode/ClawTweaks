using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using ClawTweaksSetup.Core;
using ClawTweaksSetup.Navigation;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup.Phases
{
    /// <summary>
    /// Phase 1 — HW controller health FIRST. Shows exactly which controllers are expected and what is
    /// actually present (physical Claw, virtual VIIPER pad, XInput slots, conflicts). Clean → green,
    /// issues → clearly flagged. Remediation (Center M removal) is wired in a later step; Ⓨ re-checks.
    /// </summary>
    public sealed class ControllerPhase : PhaseBase
    {
        private readonly StackPanel _root = new StackPanel();
        private readonly List<PhaseAction> _actions;
        private bool _busy;

        public override string Title => "Controller";
        public override IReadOnlyList<PhaseAction> Actions => _actions;

        public ControllerPhase()
        {
            Content = _root;
            _actions = new List<PhaseAction>
            {
                new PhaseAction(PadButton.Y, "Re-check", () => _ = RefreshAsync(), () => !_busy),
            };
            ShowChecking();
        }

        public override void OnEnter() => _ = RefreshAsync();

        private void ShowChecking()
        {
            _root.Children.Clear();
            _root.Children.Add(UiHelpers.Title("Controller health"));
            _root.Children.Add(UiHelpers.Body(
                "The native controller must be clean before the virtual controller mode can work. " +
                "In virtual mode the physical pad is hidden and one virtual VIIPER controller is " +
                "active; in hardware mode the physical pad is used directly."));
            _root.Children.Add(UiHelpers.StatusRow(StatusKind.Working, "Checking…", "Probing controller topology"));
        }

        private async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            State = PhaseState.Working;
            RaiseActionsChanged();
            ShowChecking();

            var r = await Task.Run(() => ControllerHealth.Probe());

            _root.Children.Clear();
            _root.Children.Add(UiHelpers.Title("Controller health"));
            _root.Children.Add(UiHelpers.Body(
                "In virtual mode the physical pad is hidden and one virtual VIIPER controller is " +
                "active; in hardware mode the physical pad is used directly."));
            _root.Children.Add(UiHelpers.Caption($"Last checked {DateTime.Now:HH:mm:ss}"));

            // 1) Physical MSI Claw controller
            if (r.ClawPresent)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Ok, "Physical MSI Claw controller",
                    $"Detected — {r.ClawNodes} interface node(s), mode: {r.ClawMode}."));
            else
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Error, "Physical MSI Claw controller",
                    "NOT detected (VID_0DB0). The controller is missing, or MSI Center M has taken it over."));

            // 2) Virtual VIIPER controller
            if (r.VirtualPadCount > 0)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Ok, "Virtual controller (VIIPER)",
                    $"Active: {r.VirtualPadName}"));
            else
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Info, "Virtual controller (VIIPER)",
                    "Not mounted right now. Expected only while virtual mode is running — normal at setup time."));

            // 3) XInput slots (double-input indicator)
            if (r.XInputConnected >= 2)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Warning, "XInput controllers",
                    $"{r.XInputConnected} visible. Two or more while playing = double input."));
            else
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Ok, "XInput controllers",
                    $"{r.XInputConnected} visible (fine)."));

            // 4) MSI Center M conflict — flagged here, but only handled AFTER installation.
            if (r.CenterMRunning)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Warning, "MSI Center M",
                    "Running — it can fight ClawTweaks for the controller and LED. You'll be guided " +
                    "to deactivate it after the app is installed."));
            else
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Ok, "MSI Center M",
                    "Not running (good)."));

            // 5) Steam Xbox filter (optional double-input source)
            if (r.SteamFilterPresent)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Warning, "Steam Xbox filter driver",
                    "Present — a common double-input source. Check Steam Input if you see doubled inputs."));

            switch (r.Verdict)
            {
                case HealthVerdict.Clean:   State = PhaseState.Ok; break;
                case HealthVerdict.Warning: State = PhaseState.Action; break;
                default:                    State = PhaseState.Blocked; break;
            }

            _busy = false;
            RaiseActionsChanged();
        }
    }
}
