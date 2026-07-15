using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using ClawTweaksSetup.Navigation;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup.Phases
{
    /// <summary>
    /// Phase 0 — determine the MODE we run through (fresh install vs update). This is context, not a
    /// pass/fail check, so it's shown as a mode banner rather than a status row. Helper-duplicate
    /// cleanup is deliberately NOT done here: multiple helpers only appear in update mode AFTER the
    /// package is installed and the helper restarts, so that check belongs to the post-install phase.
    /// </summary>
    public sealed class DetectPhase : PhaseBase
    {
        private const string PackageFamily = "MSIClaw.ClawTweaks_7eszav2039cvc";

        private readonly StackPanel _root = new StackPanel();
        private readonly List<PhaseAction> _actions;
        private bool _busy;

        /// <summary>True if an existing install was found (update mode). Read by later phases.</summary>
        public bool IsUpdate { get; private set; }

        public override string Title => "Detect";
        public override IReadOnlyList<PhaseAction> Actions => _actions;

        public DetectPhase()
        {
            Content = _root;
            _actions = new List<PhaseAction>
            {
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

            bool installed = await Task.Run(ExistingInstallPresent);
            IsUpdate = installed;

            _root.Children.Clear();
            _root.Children.Add(UiHelpers.Title("Welcome"));

            if (installed)
            {
                _root.Children.Add(UiHelpers.ModeBanner("Mode: Update",
                    "An existing ClawTweaks installation was found. The setup will re-check all " +
                    "prerequisites and install the latest package."));
            }
            else
            {
                _root.Children.Add(UiHelpers.ModeBanner("Mode: Fresh install",
                    "No existing ClawTweaks installation was found. The setup will guide you through " +
                    "the full first-time installation."));
            }

            _root.Children.Add(UiHelpers.Body(
                "Next: controller health, required tools, the signing certificate, then the app " +
                "itself. Each step re-checks live and won't let you continue until it's satisfied."));
            _root.Children.Add(UiHelpers.Caption($"Detected {DateTime.Now:HH:mm:ss}"));

            State = PhaseState.Ok;
            _busy = false;
            RaiseActionsChanged();
        }

        private static bool ExistingInstallPresent()
        {
            try
            {
                string packagesRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                return Directory.Exists(Path.Combine(packagesRoot, PackageFamily));
            }
            catch { return false; }
        }
    }
}
