using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Phase 2 — required tools. Per-tool rows describe what each tool does and its live status.
    /// Ⓐ installs the missing ones: the silent tools first (HidHide, RTSS via winget; PawnIO via its
    /// own direct-download-then-winget-fallback path), then usbip (VIIPER backend) which cannot be
    /// silent — its installer window opens and a reboot is required afterwards (flagged in bold red).
    /// Ⓨ re-checks. Content scrolls with the D-Pad / left stick.
    /// </summary>
    public sealed class ToolsPhase : PhaseBase
    {
        private const string DescHidHide = "Hides the physical controller so games only see the virtual one — prevents double input.";
        private const string DescUsbip = "Kernel driver behind the VIIPER virtual controller. Mandatory for virtual mode.";
        private const string DescRtss = "RivaTuner Statistics Server — powers the FPS limiter and the on-screen overlay.";
        private const string DescPawnIO = "Kernel driver used for TDP control.";

        private readonly StackPanel _root = new StackPanel();
        private readonly List<PhaseAction> _actions;
        private bool _busy;
        private bool _anyMissing;
        private bool _rebootNeeded;

        private readonly ProgressBar _spinner = new ProgressBar
        {
            Height = 6, IsIndeterminate = true,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x38)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 10),
            Visibility = Visibility.Collapsed,
        };
        private readonly TextBlock _log = new TextBlock
        {
            FontSize = 15, Foreground = UiHelpers.Subtle,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0),
        };

        public override string Title => "Tools";
        public override IReadOnlyList<PhaseAction> Actions => _actions;

        /// <summary>One-shot: set from the constructor when MainWindow is landing back on this phase
        /// after an elevated relaunch it triggered (ResumeSilentArg). Consumed on the first OnEnter so
        /// re-visiting this phase later in the same run behaves normally.</summary>
        private bool _autoResumeSilent;

        public ToolsPhase(bool autoResumeSilent = false)
        {
            _autoResumeSilent = autoResumeSilent;
            Content = _root;
            _actions = new List<PhaseAction>
            {
                new PhaseAction(PadButton.A, "Install missing", () => _ = InstallAsync(), () => !_busy && _anyMissing),
                new PhaseAction(PadButton.Y, "Re-check", () => _ = RefreshAsync(), () => !_busy),
            };
        }

        public override void OnEnter()
        {
            bool autoResume = _autoResumeSilent;
            _autoResumeSilent = false;
            _ = autoResume ? InstallAsync(skipUsbip: true) : RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            State = PhaseState.Working;
            RaiseActionsChanged();

            var hidhide = await Task.Run(() => ToolDetect.HidHide());
            var usbip = await Task.Run(() => ToolDetect.Usbip());
            var rtss = await Task.Run(() => ToolDetect.Rtss());
            var pawnio = await Task.Run(() => ToolDetect.PawnIO());

            _anyMissing = !hidhide.Installed || !usbip.Installed || !rtss.Installed || !pawnio.Installed;
            Render(hidhide, usbip, rtss, pawnio);

            State = _anyMissing ? PhaseState.Action : PhaseState.Ok;
            _busy = false;
            RaiseActionsChanged();
        }

        private void Render(ToolStatus hidhide, ToolStatus usbip, ToolStatus rtss, ToolStatus pawnio)
        {
            _root.Children.Clear();
            _root.Children.Add(UiHelpers.Title("Required tools"));
            _root.Children.Add(UiHelpers.Caption(
                $"Last checked {DateTime.Now:HH:mm:ss}   ·   Scroll with the D-Pad or left stick"));

            // Prominent call-to-action at the top when something is missing.
            if (_anyMissing && !_busy)
                _root.Children.Add(UiHelpers.ActionCallout("Press Ⓐ to install the missing tools."));

            if (_rebootNeeded)
                _root.Children.Add(UiHelpers.StatusRow(StatusKind.Error, "REBOOT REQUIRED",
                    "usbip was installed. Reboot the device once so its driver activates, then run this setup again."));

            _root.Children.Add(ToolRow(hidhide, "HidHide", DescHidHide));
            _root.Children.Add(ToolRow(usbip, "usbip  (required for virtual controller)", DescUsbip));
            _root.Children.Add(ToolRow(rtss, "RTSS", DescRtss));
            _root.Children.Add(ToolRow(pawnio, "PawnIO", DescPawnIO));

            _root.Children.Add(_spinner);
            if (_log.Text.Length > 0) _root.Children.Add(_log);
        }

        /// <summary>Command-line marker that survives an elevated relaunch triggered from here: tells
        /// MainWindow to land back on this phase and resume automatically — but only the SILENT part
        /// (HidHide/RTSS/PawnIO). usbip is deliberately excluded from auto-resume: its installer opens
        /// a visible, non-silent third-party window, and popping that unprompted right after the UAC
        /// dialog closes would be surprising. The user gets one more explicit click for that step
        /// (already elevated by then, so no second UAC).</summary>
        public const string ResumeSilentArg = "--resume-tools-silent";

        private async Task InstallAsync(bool skipUsbip = false)
        {
            if (_busy) return;

            // Installing drivers (HidHide/usbip/PawnIO) needs admin; Center runs unelevated by default.
            // Relaunches elevated if needed (one UAC prompt covers this and every later privileged step
            // in the same run) or returns false if the user declined. ResumeSilentArg is appended so the
            // relaunch knows to auto-continue the silent tools without a second click.
            var realArgs = Environment.GetCommandLineArgs().Skip(1)
                .Where(a => a != ResumeSilentArg)
                .Append(ResumeSilentArg)
                .ToArray();
            if (!ElevationGate.EnsureElevatedOrRelaunch(realArgs))
            {
                _log.Text = "Administrator rights are required to install these tools.";
                Render(ToolDetect.HidHide(), ToolDetect.Usbip(), ToolDetect.Rtss(), ToolDetect.PawnIO());
                return;
            }

            _busy = true;
            State = PhaseState.Working;
            _log.Text = "";
            _spinner.Visibility = Visibility.Visible;
            RaiseActionsChanged();

            void Log(string s) => Dispatcher.Invoke(() =>
            {
                _log.Text += (_log.Text.Length > 0 ? "\n" : "") + s;
                if (!_root.Children.Contains(_log)) _root.Children.Add(_log);
            });

            // 1) Silent tools first: HidHide + RTSS via winget, PawnIO via its own direct-download-
            // then-winget-fallback path (PawnIoSetup.Run) — none of these open a visible window.
            await Task.Run(() =>
            {
                if (!ToolDetect.HidHide().Installed) ToolInstaller.InstallHidHide(Log);
                if (!ToolDetect.Rtss().Installed) ToolInstaller.InstallRtss(Log);
                if (!ToolDetect.PawnIO().Installed) PawnIoSetup.Run(Log);
            });

            // Re-render so the silent tools flip to green before we tackle usbip.
            await RefreshSilentAsync();

            // 2) usbip last — it can't be silent (its installer window opens; confirm the driver prompt).
            // Skipped on an auto-resume (skipUsbip): don't pop a third-party installer window unprompted
            // right after the UAC dialog closes. The phase just re-renders with usbip still missing, so
            // "Install missing" is still available — one more explicit, already-elevated click, no UAC.
            if (skipUsbip)
            {
                if (!await Task.Run(() => ToolDetect.Usbip().Installed))
                    Log("usbip still needs to be installed — press Install missing again to continue.");
            }
            else if (!await Task.Run(() => ToolDetect.Usbip().Installed))
            {
                Log("usbip cannot be installed silently — an installer window will open now.");
                Log("Confirm the driver-install prompt. A REBOOT is required afterwards.");
                var r = await Task.Run(() => UsbipSetup.Run(Log));
                if (r == UsbipSetup.Result.RebootRequired || r == UsbipSetup.Result.Success)
                    _rebootNeeded = true;
            }

            Dispatcher.Invoke(() => _spinner.Visibility = Visibility.Collapsed);
            _busy = false;
            await RefreshAsync();
        }

        /// <summary>Re-render the tool rows (keeps spinner/log) after the silent installs.</summary>
        private async Task RefreshSilentAsync()
        {
            var h = await Task.Run(() => ToolDetect.HidHide());
            var u = await Task.Run(() => ToolDetect.Usbip());
            var r = await Task.Run(() => ToolDetect.Rtss());
            var p = await Task.Run(() => ToolDetect.PawnIO());
            Render(h, u, r, p);
        }

        private static Border ToolRow(ToolStatus s, string label, string desc)
        {
            var kind = s.Installed ? StatusKind.Ok : StatusKind.Warning;
            var status = s.Installed ? "Installed — " + s.Detail : "Not installed";
            return UiHelpers.ToolRow(kind, label, desc, status);
        }
    }
}
