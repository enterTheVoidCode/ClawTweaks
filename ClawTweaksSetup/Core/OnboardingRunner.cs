using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared.Enums;

namespace ClawTweaksSetup.Core
{
    public enum OnboardingStepState { Unknown, Pending, Working, Ok, Error }

    public sealed class OnboardingStep
    {
        public string Title;
        public OnboardingStepState State = OnboardingStepState.Unknown;
        public string Detail = "";
        /// <summary>False while the helper hasn't confirmed the target isn't already satisfied —
        /// the UI greys the run button out instead of guessing.</summary>
        public bool Actionable = false;
    }

    /// <summary>
    /// The onboarding steps (Center M off, virtual controller on, verify connection, Game Bar
    /// auto-jump) — each one triggered individually by the user, and its status QUERIED from the
    /// helper rather than assumed. If the helper reports a step's target is already satisfied (e.g.
    /// Center M was disabled ages ago, outside Center entirely), that step shows as done and its run
    /// action is greyed out instead of offering to redo something that's already correct.
    /// Every write/read goes through HelperPipeClient, which speaks the exact same wire protocol as
    /// the widget over the helper's second ("ClawTweaksCenter") pipe — no helper logic is duplicated.
    /// </summary>
    public sealed class OnboardingRunner
    {
        private const int AutoJumpPosition = 3; // Microsoft occupies the first two Game Bar slots.
        public const int StepCenterM = 0;
        public const int StepVirtualController = 1;
        public const int StepVerify = 2;
        public const int StepAutoJump = 3;

        public HelperPipeClient PipeClient { get; } = new HelperPipeClient();

        public IReadOnlyList<OnboardingStep> Steps { get; } = new List<OnboardingStep>
        {
            new OnboardingStep { Title = "Disable MSI Center M" },
            new OnboardingStep { Title = "Enable virtual controller" },
            new OnboardingStep { Title = "Verify virtual controller connection" },
            new OnboardingStep { Title = "Set Game Bar auto-jump to ClawTweaks" },
        };

        public event Action StepsChanged;
        public bool IsConnecting { get; private set; }
        public bool IsConnected => PipeClient.IsConnected;

        /// <summary>True once step 2 (verify) has confirmed a working virtual controller this
        /// session — gates step 3 (auto-jump) so it's never offered for a controller that isn't
        /// actually confirmed connected, while still being its own separately-triggered action.</summary>
        private bool _verifiedThisSession;

        private void Notify() => StepsChanged?.Invoke();

        public OnboardingRunner()
        {
            PipeClient.PropertyUpdated += (function, content) =>
            {
                bool value = string.Equals(content, "True", StringComparison.OrdinalIgnoreCase);
                if (function == Function.MsiCenterActive) ApplyCenterMStatus(value);
                else if (function == Function.ControllerEmulationEnabled) ApplyControllerStatus(value);
            };
        }

        private void ApplyCenterMStatus(bool running)
        {
            var step = Steps[StepCenterM];
            if (running) { step.State = OnboardingStepState.Pending; step.Detail = "Currently running."; step.Actionable = true; }
            else { step.State = OnboardingStepState.Ok; step.Detail = "Already disabled."; step.Actionable = false; }
            Notify();
        }

        private void ApplyControllerStatus(bool enabled)
        {
            var step = Steps[StepVirtualController];
            if (enabled) { step.State = OnboardingStepState.Ok; step.Detail = "Already enabled."; step.Actionable = false; }
            else { step.State = OnboardingStepState.Pending; step.Detail = "Currently disabled."; step.Actionable = true; }
            Notify();
        }

        /// <summary>Connects (if needed) and asks the helper for a fresh status snapshot. Steps 0/1
        /// update themselves from the resulting PropertyUpdated pushes (see ctor). Steps 2/3 have no
        /// reliable "already done" query (see plan doc's Auto-Jump persistence caveat) — they stay
        /// manually actionable always.</summary>
        public async Task RefreshStatusAsync(Action<string> log = null)
        {
            if (IsConnecting) return;
            IsConnecting = true;
            Notify();
            try
            {
                if (!PipeClient.IsConnected)
                {
                    // Generous window: right after an in-app update the pipe-serving helper is being
                    // swapped (old keeper killed, new one launched once the single-instance mutex frees).
                    // The verified/auto-reconnecting client rides that out instead of failing on a
                    // one-shot bind to the dying instance.
                    bool connected = await PipeClient.ConnectAsync(TimeSpan.FromSeconds(45), log).ConfigureAwait(false);
                    if (!connected)
                    {
                        foreach (var s in Steps) { s.State = OnboardingStepState.Error; s.Detail = "Could not connect to the helper."; s.Actionable = false; }
                        return;
                    }
                }

                Steps[StepVerify].Actionable = true;
                Steps[StepAutoJump].Actionable = _verifiedThisSession;
                if (string.IsNullOrEmpty(Steps[StepVerify].Detail)) Steps[StepVerify].Detail = "Not checked yet.";
                if (string.IsNullOrEmpty(Steps[StepAutoJump].Detail)) Steps[StepAutoJump].Detail = _verifiedThisSession ? "" : "Verify the connection first.";

                PipeClient.RequestStatusRefresh();
            }
            finally
            {
                IsConnecting = false;
                Notify();
            }
        }

        public async Task RunStepAsync(int index, Action<string> log = null)
        {
            if (!Steps[index].Actionable && Steps[index].State != OnboardingStepState.Unknown) return;
            if (!PipeClient.IsConnected)
            {
                await RefreshStatusAsync(log).ConfigureAwait(false);
                if (!PipeClient.IsConnected) return;
            }

            switch (index)
            {
                case StepCenterM: await RunCenterMAsync().ConfigureAwait(false); break;
                case StepVirtualController: await RunVirtualControllerAsync().ConfigureAwait(false); break;
                case StepVerify: await RunVerifyAsync().ConfigureAwait(false); break;
                case StepAutoJump: RunAutoJump(); break;
            }
        }

        private async Task RunCenterMAsync()
        {
            var step = Steps[StepCenterM];
            step.State = OnboardingStepState.Working; step.Detail = "Disabling…"; Notify();

            bool ok = await PipeClient.SetAndWaitForConfirmationAsync(
                Function.MsiCenterActive, false, "False", TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            if (ok) { step.State = OnboardingStepState.Ok; step.Detail = "Disabled."; step.Actionable = false; }
            else { step.State = OnboardingStepState.Error; step.Detail = "Did not confirm in time."; }
            Notify();
        }

        private async Task RunVirtualControllerAsync()
        {
            var step = Steps[StepVirtualController];
            step.State = OnboardingStepState.Working; step.Detail = "Enabling…"; Notify();

            bool ok = await PipeClient.SetAndWaitForConfirmationAsync(
                Function.ControllerEmulationEnabled, true, "True", TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            if (ok) { step.State = OnboardingStepState.Ok; step.Detail = "Enabled."; step.Actionable = false; }
            else { step.State = OnboardingStepState.Error; step.Detail = "Did not confirm in time."; }
            Notify();
        }

        private async Task RunVerifyAsync()
        {
            var step = Steps[StepVerify];
            step.State = OnboardingStepState.Working; step.Detail = "Checking…"; Notify();

            bool healthy = false;
            HealthResult health = null;
            for (int attempt = 0; attempt < 5 && !healthy; attempt++)
            {
                if (attempt > 0) await Task.Delay(1000).ConfigureAwait(false);
                health = await Task.Run(() => ControllerHealth.Probe()).ConfigureAwait(false);
                healthy = health.ClawPresent && health.VirtualPadCount >= 1;
            }

            _verifiedThisSession = healthy;
            step.State = healthy ? OnboardingStepState.Ok : OnboardingStepState.Error;
            step.Detail = healthy ? $"Connected ({health.VirtualPadName ?? "virtual pad"})." : "No virtual controller detected.";

            Steps[StepAutoJump].Actionable = healthy;
            if (healthy && string.IsNullOrEmpty(Steps[StepAutoJump].Detail)) Steps[StepAutoJump].Detail = "";
            Notify();
        }

        private void RunAutoJump()
        {
            var step = Steps[StepAutoJump];
            bool sent = PipeClient.SetProperty(Function.GameBarWidgetPosition, AutoJumpPosition);
            step.State = sent ? OnboardingStepState.Ok : OnboardingStepState.Error;
            step.Detail = sent ? $"Position {AutoJumpPosition}." : "Could not reach the helper.";
            step.Actionable = false;
            Notify();
        }
    }
}
