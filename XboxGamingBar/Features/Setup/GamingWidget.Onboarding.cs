using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace XboxGamingBar
{
    /// <summary>
    /// Onboarding tab: a single button runs the proven prerequisite check/installer
    /// (the embedded Setup-Tools.ps1, via the elevated helper) that detects + installs
    /// all four required tools (ViGEm, HidHide, RTSS, PawnIO) in one pass. Read-only
    /// status rows reflect the result; optional MSI Center M / controller-emulation hints
    /// follow.
    ///
    /// The Quick Settings tiles are NOT locked. While not all four tools are installed the
    /// Onboarding nav item shows a badge; once all four are present the tab moves to the far
    /// right (after Settings). No other tab is hidden.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private static readonly SolidColorBrush OnbGreenBrush = new SolidColorBrush(Colors.LimeGreen);
        private static readonly SolidColorBrush OnbGrayBrush = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136));
        private static readonly SolidColorBrush OnbAmberBrush = new SolidColorBrush(Color.FromArgb(255, 255, 165, 0));

        // Tracks the last-applied onboarding tab position (null = not yet applied) so the nav
        // item is only re-ordered on an actual complete/incomplete transition.
        private bool? _onbLayoutComplete = null;

        // Required tools present. The "primary emulation" tool depends on the active backend:
        // VIIPER needs usbip-win2, Legacy ViGEm needs ViGEmBus. The other class is then optional
        // (e.g. with VIIPER active, a missing ViGEm must NOT keep onboarding "open"). HidHide/
        // RTSS/PawnIO are always required. ViGEm/HidHide come from cached install states (their
        // property Value is unreliable — status arrives via a Command.Get response); usbip/RTSS/
        // PawnIO use .Value, which IS set via the property push/sync path.
        private bool OnbAllToolsInstalled
        {
            get
            {
                bool viiper = emulationBackend?.Value == true;
                bool primaryEmu = viiper ? (usbipInstalled?.Value == true) : _gateVigemInstalled;
                return primaryEmu
                    && _gateHidHideInstalled
                    && (rtssInstalled?.Value == true)
                    && (pawnIOInstalled?.Value == true);
            }
        }

        // Cached ViGEm / HidHide install states (set by Update*InstalledUI). See OnbAllToolsInstalled.
        private bool _gateVigemInstalled;
        private bool _gateHidHideInstalled;

        // "Reported" flags: true once the helper has actually told us each tool's real install state.
        // Until ALL required tools are known, RefreshOnboardingState shows the *persisted* last-known
        // completion state instead of assuming "incomplete" — otherwise the yellow badge flashed on
        // every start for users who long finished onboarding (gates default to false until confirmed).
        private bool _onbVigemReported, _onbHidHideReported, _onbRtssReported, _onbPawnReported, _onbUsbipReported;
        private bool OnbToolStatesKnown
        {
            get
            {
                bool viiper = emulationBackend?.Value == true;
                bool primaryKnown = viiper ? _onbUsbipReported : _onbVigemReported;
                return primaryKnown && _onbHidHideReported && _onbRtssReported && _onbPawnReported;
            }
        }
        private const string OnboardingCompleteKey = "Onboarding_Complete";

        // Keeps the backend-switch notice blinking (held as a field so the Storyboard isn't GC'd).
        private Storyboard _onbBackendBlinkSb;

        // Starts the looping fade on the backend-switch headline once it's loaded into the tree.
        private void OnbBackendBlink_Loaded(object sender, RoutedEventArgs e)
        {
            if (_onbBackendBlinkSb != null) return;
            if (!(sender is FrameworkElement fe)) return;
            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.3,
                Duration = new Duration(TimeSpan.FromSeconds(0.7)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(anim, fe);
            Storyboard.SetTargetProperty(anim, "Opacity");
            _onbBackendBlinkSb = new Storyboard();
            _onbBackendBlinkSb.Children.Add(anim);
            try { _onbBackendBlinkSb.Begin(); } catch { }
        }

        // Called when the Onboarding tab becomes visible: render all rows from current state.
        private void RefreshOnboardingTab()
        {
            try
            {
                UpdateOnboardingUsbip(usbipInstalled?.Value == true);
                UpdateOnboardingViGEm(_gateVigemInstalled);
                UpdateOnboardingHidHide(_gateHidHideInstalled);
                UpdateOnboardingRtss(rtssInstalled?.Value == true);
                UpdateOnboardingPawnIO(pawnIOInstalled?.Value == true);
                UpdateOnboardingSteps();
            }
            catch (Exception ex)
            {
                Logger.Warn($"RefreshOnboardingTab failed: {ex.Message}");
            }
        }

        private static void SetOnbStatus(TextBlock status, string toolName, bool installed)
        {
            if (status == null) return;
            status.Text = installed ? $"{toolName}: Installed" : $"{toolName}: Not installed";
            status.Foreground = installed ? OnbGreenBrush : OnbGrayBrush;
        }

        // usbip-win2 row (VIIPER backend prerequisite). The Install button is offered when the
        // driver is missing; the status doubles as the Legacy-vs-VIIPER primary-tool gate.
        private void UpdateOnboardingUsbip(bool installed)
        {
            _onbUsbipReported = true;
            SetOnbStatus(OnbUsbipStatus, "usbip-win2", installed);
            if (OnbUsbipInstallBtn != null)
            {
                OnbUsbipInstallBtn.Content = installed ? "Installed" : "Install usbip-win2";
                OnbUsbipInstallBtn.IsEnabled = !installed;
            }
            // Keep the System → Debug → Driver dependencies row in sync (status + Install/Uninstall).
            SetDebugToolRow(DebugUsbipStatusText, DebugUsbipInstallButton, DebugUsbipUninstallButton, "usbip-win2", "Install usbip-win2", installed);
            RefreshOnboardingState();
        }

        private void UpdateOnboardingViGEm(bool installed)
        {
            _onbVigemReported = true;
            SetOnbStatus(OnbViGEmStatus, "ViGEmBus", installed);
            RefreshOnboardingState();
        }

        private void UpdateOnboardingHidHide(bool installed)
        {
            _onbHidHideReported = true;
            SetOnbStatus(OnbHidHideStatus, "HidHide", installed);
            RefreshOnboardingState();
        }

        private void UpdateOnboardingRtss(bool installed)
        {
            _onbRtssReported = true;
            SetOnbStatus(OnbRtssStatus, "RTSS", installed);
            SetDebugToolRow(DebugRtssStatusText, DebugRtssInstallButton, DebugRtssUninstallButton, "RTSS", "Install RTSS", installed);
            RefreshOnboardingState();
        }

        private void UpdateOnboardingPawnIO(bool installed)
        {
            _onbPawnReported = true;
            SetOnbStatus(OnbPawnIOStatus, "PawnIO", installed);
            SetDebugToolRow(DebugPawnIOStatusText, DebugPawnIOInstallButton, DebugPawnIOUninstallButton, "PawnIO", "Install PawnIO", installed);
            RefreshOnboardingState();
        }

        // RTSS installed-status callback (replaces the direct UpdateFPSLimitControls wiring):
        // keeps the FPS-limit controls in sync AND updates the onboarding + Debug RTSS rows.
        private void OnRtssInstalledChanged(bool installed)
        {
            UpdateFPSLimitControls(installed);
            UpdateOnboardingRtss(installed);
            MaybeFinishOnbSetup();
        }

        // Optional guidance steps (MSI Center M off + controller emulation). These do NOT gate
        // anything — they are hints. Completion (the badge / tab move) depends only on the tools.
        private void UpdateOnboardingSteps()
        {
            bool msiActive = msiCenterActive?.Value == true;
            bool emuOn = controllerEmulationEnabled?.Value == true;

            if (OnbMsiCenterStatus != null)
            {
                OnbMsiCenterStatus.Text = msiActive
                    ? "MSI Center M is running — disable it so ClawTweaks can control the device."
                    : "MSI Center M: off";
                OnbMsiCenterStatus.Foreground = msiActive ? OnbAmberBrush : OnbGreenBrush;
            }
            if (OnbMsiCenterBtn != null)
            {
                OnbMsiCenterBtn.IsEnabled = msiActive;
            }

            if (OnbEmulationStatus != null)
            {
                OnbEmulationStatus.Text = emuOn
                    ? "Controller emulation: enabled"
                    : (msiActive ? "Disable MSI Center M first, then enable controller emulation." : "Controller emulation: off");
                OnbEmulationStatus.Foreground = emuOn ? OnbGreenBrush : OnbGrayBrush;
            }
            if (OnbEmulationBtn != null)
            {
                OnbEmulationBtn.IsEnabled = !emuOn && !msiActive;
            }
        }

        /// <summary>
        /// Refresh the onboarding nav badge and tab position from current tool state. No tile
        /// locking, no banner, no hiding of other tabs. Cheap; safe to call often.
        /// </summary>
        internal void RefreshOnboardingState()
        {
            try
            {
                bool known = OnbToolStatesKnown;
                // Until the helper has reported every tool's real state, fall back to the persisted
                // last-known completion state so completed users don't get a yellow-badge flash on start.
                bool complete = known ? OnbAllToolsInstalled : GetPersistedOnboardingComplete();
                ApplyOnboardingTabLayout(complete);

                // Auto-expand the Setup section while onboarding is still pending so the user
                // immediately sees what is missing. Never auto-collapse once they've opened it.
                if (SetupExpander != null && !complete)
                    SetupExpander.IsExpanded = true;

                if (OnboardingNavBadge != null)
                {
                    OnboardingNavBadge.Visibility = complete ? Visibility.Collapsed : Visibility.Visible;
                }

                // Persist only the *confirmed* state so the next cold start initialises from truth.
                if (known) SetPersistedOnboardingComplete(OnbAllToolsInstalled);

                // Onboarding completion affects the emulation toggle gate — re-evaluate.
                UpdateControllerEmulationToggleEnabled();
            }
            catch (Exception ex)
            {
                Logger.Warn($"RefreshOnboardingState failed: {ex.Message}");
            }
        }

        private static bool GetPersistedOnboardingComplete()
        {
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings;
                return ls.Values.TryGetValue(OnboardingCompleteKey, out var v) && v is bool b && b;
            }
            catch { return false; }
        }

        private static void SetPersistedOnboardingComplete(bool complete)
        {
            try { Windows.Storage.ApplicationData.Current.LocalSettings.Values[OnboardingCompleteKey] = complete; }
            catch { }
        }

        /// <summary>
        /// Re-applies tab placement on a setup complete/incomplete transition. Placement itself is
        /// owned by <see cref="ApplyTabPrefs"/> / <c>ReorderNavChildren</c>, which forces the Setup
        /// tab to the FIRST position while onboarding is incomplete (overriding the saved order) and
        /// otherwise honours the user's saved/factory order (Setup sits last by default).
        /// </summary>
        private void ApplyOnboardingTabLayout(bool complete)
        {
            try
            {
                if (_onbLayoutComplete == complete) return;
                _onbLayoutComplete = complete;
                ApplyTabPrefs();
            }
            catch (Exception ex)
            {
                Logger.Warn($"ApplyOnboardingTabLayout failed: {ex.Message}");
            }
        }

        /// <summary>
        /// True while onboarding is still pending — drives the live-only "Setup tab to the front"
        /// override in <see cref="ApplyTabPrefs"/>. Uses the confirmed layout state once known,
        /// otherwise the persisted completion flag so a returning, fully-set-up user never gets a
        /// transient Setup-first flash on cold start.
        /// </summary>
        private bool OnboardingIncompleteForOrder()
            => _onbLayoutComplete.HasValue ? !_onbLayoutComplete.Value : !GetPersistedOnboardingComplete();

        // True between clicking the setup button and the helper reporting back. Gates the spinner and
        // the completion handler so a tab refresh doesn't prematurely stop the spinner.
        private bool _onbSetupRunning;
        private int _onbSetupRunSeq;

        // --- Step 1: run the proven tool check/installer for all four tools ---
        private void OnbRunSetup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Onboarding: running tool setup (Setup-Tools.ps1)");
                _onbSetupRunning = true;
                int seq = ++_onbSetupRunSeq;

                if (OnbRunSetupBtn != null)
                {
                    OnbRunSetupBtn.Content = "Installing…";
                    OnbRunSetupBtn.IsEnabled = false;
                }
                if (OnbSetupProgressPanel != null) OnbSetupProgressPanel.Visibility = Visibility.Visible;
                if (OnbSetupSpinner != null) { OnbSetupSpinner.Visibility = Visibility.Visible; OnbSetupSpinner.IsActive = true; }
                if (OnbSetupProgress != null)
                {
                    OnbSetupProgress.Text = "Checking and installing missing tools via winget — this can take a few minutes. The status lines below update when it's done.";
                }
                runToolSetup?.Trigger("install");

                // Fallback: if the helper never reports back (e.g. it died), stop the spinner after a
                // generous timeout. Normal completion is driven by the *Installed pushes that arrive
                // when the script finishes (see MaybeFinishOnbSetup).
                _ = OnbSetupFallbackAsync(seq);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Onboarding tool setup failed: {ex.Message}");
                _onbSetupRunning = false;
                if (OnbSetupSpinner != null) { OnbSetupSpinner.IsActive = false; OnbSetupSpinner.Visibility = Visibility.Collapsed; }
                if (OnbRunSetupBtn != null) { OnbRunSetupBtn.Content = "Check & install tools"; OnbRunSetupBtn.IsEnabled = true; }
            }
        }

        // Called from the *Installed push handlers (UpdateViGEm/HidHide/PawnIOInstalledUI + RTSS),
        // which only fire when the helper actually reports a status — i.e. after the setup script
        // finished. Stops the spinner and shows the result (incl. a reboot hint for drivers).
        private void MaybeFinishOnbSetup()
        {
            if (!_onbSetupRunning) return;
            _onbSetupRunning = false;

            if (OnbSetupSpinner != null) { OnbSetupSpinner.IsActive = false; OnbSetupSpinner.Visibility = Visibility.Collapsed; }
            if (OnbRunSetupBtn != null)
            {
                OnbRunSetupBtn.Content = OnbAllToolsInstalled ? "Re-check tools" : "Check & install tools";
                OnbRunSetupBtn.IsEnabled = true;
            }
            if (OnbSetupProgress != null)
            {
                OnbSetupProgress.Text = OnbAllToolsInstalled
                    ? "All required tools are installed. ✓"
                    : "Setup finished. If a tool still shows „Not installed“, reboot once so its driver can activate, then tap „Re-check tools“.";
            }
        }

        private async System.Threading.Tasks.Task OnbSetupFallbackAsync(int seq)
        {
            try
            {
                // The helper's own script timeout is 15 min; give it a little more before we give up.
                await System.Threading.Tasks.Task.Delay(16 * 60 * 1000);
                if (!_onbSetupRunning || seq != _onbSetupRunSeq) return; // already finished / re-run
                _onbSetupRunning = false;
                if (OnbSetupSpinner != null) { OnbSetupSpinner.IsActive = false; OnbSetupSpinner.Visibility = Visibility.Collapsed; }
                if (OnbRunSetupBtn != null)
                {
                    OnbRunSetupBtn.Content = OnbAllToolsInstalled ? "Re-check tools" : "Check & install tools";
                    OnbRunSetupBtn.IsEnabled = true;
                }
                if (OnbSetupProgress != null && !OnbAllToolsInstalled)
                {
                    OnbSetupProgress.Text = "Still working or no response — check the status lines, or tap „Re-check tools“.";
                }
            }
            catch { }
        }

        // --- usbip-win2 install (VIIPER prerequisite): triggers the bundled-MSI install on the helper ---
        private void OnbUsbipInstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Onboarding: installing bundled usbip-win2");
                if (OnbUsbipInstallBtn != null) { OnbUsbipInstallBtn.Content = "Installing…"; OnbUsbipInstallBtn.IsEnabled = false; }
                installUsbip?.TriggerInstall();
            }
            catch (Exception ex) { Logger.Warn($"Onboarding usbip install failed: {ex.Message}"); }
        }

        // --- Step 2: disable MSI Center M (optional hint) ---
        private void OnbMsiCenterDisable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Onboarding: disabling MSI Center M");
                msiCenterActive?.SetValue(false);
            }
            catch (Exception ex) { Logger.Warn($"Onboarding MSI Center disable failed: {ex.Message}"); }
            UpdateOnboardingSteps();
        }

        // --- Step 3: enable controller emulation (optional hint) ---
        private void OnbEmulationEnable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Onboarding: enabling controller emulation");
                if (ControllerEmulationEnabledToggle != null)
                {
                    ControllerEmulationEnabledToggle.IsOn = true; // fires ControllerEmulationEnabledToggle_Toggled
                }
                else
                {
                    controllerEmulationEnabled?.SetValue(true);
                }
            }
            catch (Exception ex) { Logger.Warn($"Onboarding enable emulation failed: {ex.Message}"); }
            UpdateOnboardingSteps();
        }

        // --- Disable the controller Guide button opening the Windows Game Bar (frees it for Steam BPM etc.) ---
        private async void OnbDisableGuideGameBar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Onboarding: disabling Guide-button → Game Bar shortcuts");
                if (OnbGuideGameBarBtn != null) { OnbGuideGameBarBtn.IsEnabled = false; OnbGuideGameBarBtn.Content = "Applying…"; }

                // The widget is sandboxed and can't write HKCU; the elevated helper sets the registry.
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "DisableGuideGameBar", true } });

                if (OnbGuideGameBarBtn != null) OnbGuideGameBarBtn.Content = "Done";
                if (OnbGuideGameBarStatus != null)
                {
                    OnbGuideGameBarStatus.Text = "Done. Sign out and back in (or reboot) so Windows stops opening the Game Bar on the Guide button.";
                    OnbGuideGameBarStatus.Foreground = OnbGreenBrush;
                    OnbGuideGameBarStatus.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Onboarding disable Guide Game Bar failed: {ex.Message}");
                if (OnbGuideGameBarBtn != null) { OnbGuideGameBarBtn.IsEnabled = true; OnbGuideGameBarBtn.Content = "Disable Game Bar on Guide button"; }
            }
        }

        // --- System > Debug: per-tool install/uninstall (RTSS + PawnIO; ViGEm/HidHide handled in LegionGo.cs) ---

        private static void SetDebugToolRow(TextBlock status, Button installBtn, Button uninstallBtn, string toolName, string installLabel, bool installed)
        {
            if (status != null)
            {
                status.Text = installed ? $"{toolName}: Installed" : $"{toolName}: Not Installed";
                status.Foreground = installed ? OnbGreenBrush : OnbGrayBrush;
            }
            if (installBtn != null)
            {
                installBtn.Content = installed ? "Installed" : installLabel;
                installBtn.IsEnabled = !installed;
            }
            if (uninstallBtn != null)
            {
                uninstallBtn.Content = "Uninstall";
                uninstallBtn.IsEnabled = installed;
            }
        }

        private void DebugRtssInstall_Click(object sender, RoutedEventArgs e)
        {
            if (DebugRtssInstallButton != null) { DebugRtssInstallButton.Content = "Installing..."; DebugRtssInstallButton.IsEnabled = false; }
            installRTSS?.Trigger("install");
        }
        private void DebugRtssUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (DebugRtssUninstallButton != null) { DebugRtssUninstallButton.Content = "Uninstalling..."; DebugRtssUninstallButton.IsEnabled = false; }
            uninstallRTSS?.Trigger("uninstall");
        }

        private void DebugUsbipInstall_Click(object sender, RoutedEventArgs e)
        {
            if (DebugUsbipInstallButton != null) { DebugUsbipInstallButton.Content = "Installing..."; DebugUsbipInstallButton.IsEnabled = false; }
            installUsbip?.TriggerInstall();
        }
        private void DebugUsbipUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (DebugUsbipUninstallButton != null) { DebugUsbipUninstallButton.Content = "Uninstalling..."; DebugUsbipUninstallButton.IsEnabled = false; }
            uninstallUsbip?.Trigger("uninstall");
        }

        private void DebugPawnIOInstall_Click(object sender, RoutedEventArgs e)
        {
            if (DebugPawnIOInstallButton != null) { DebugPawnIOInstallButton.Content = "Installing..."; DebugPawnIOInstallButton.IsEnabled = false; }
            installPawnIO?.TriggerInstall();
        }
        private void DebugPawnIOUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (DebugPawnIOUninstallButton != null) { DebugPawnIOUninstallButton.Content = "Uninstalling..."; DebugPawnIOUninstallButton.IsEnabled = false; }
            uninstallPawnIO?.Trigger("uninstall");
        }
    }
}
