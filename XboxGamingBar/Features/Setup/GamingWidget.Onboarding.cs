using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

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

        // All four tools present. ViGEm/HidHide come from the cached install states (their property
        // Value is unreliable — status arrives via a Command.Get response). RTSS/PawnIO use .Value,
        // which IS set via the property push/sync path.
        private bool OnbAllToolsInstalled =>
            _gateVigemInstalled
            && _gateHidHideInstalled
            && (rtssInstalled?.Value == true)
            && (pawnIOInstalled?.Value == true);

        // Cached ViGEm / HidHide install states (set by Update*InstalledUI). See OnbAllToolsInstalled.
        private bool _gateVigemInstalled;
        private bool _gateHidHideInstalled;

        // Called when the Onboarding tab becomes visible: render all rows from current state.
        private void RefreshOnboardingTab()
        {
            try
            {
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

        private void UpdateOnboardingViGEm(bool installed)
        {
            SetOnbStatus(OnbViGEmStatus, "ViGEmBus", installed);
            RefreshOnboardingState();
        }

        private void UpdateOnboardingHidHide(bool installed)
        {
            SetOnbStatus(OnbHidHideStatus, "HidHide", installed);
            RefreshOnboardingState();
        }

        private void UpdateOnboardingRtss(bool installed)
        {
            SetOnbStatus(OnbRtssStatus, "RTSS", installed);
            SetDebugToolRow(DebugRtssStatusText, DebugRtssInstallButton, DebugRtssUninstallButton, "RTSS", "Install RTSS", installed);
            RefreshOnboardingState();
        }

        private void UpdateOnboardingPawnIO(bool installed)
        {
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
                bool complete = OnbAllToolsInstalled;
                ApplyOnboardingTabLayout(complete);

                if (OnboardingNavBadge != null)
                {
                    OnboardingNavBadge.Visibility = complete ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"RefreshOnboardingState failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Move only the Onboarding nav item: right after Main while setup is incomplete, and to the
        /// far right (after Settings/System) once all tools are installed. Other tabs are untouched.
        /// </summary>
        private void ApplyOnboardingTabLayout(bool complete)
        {
            try
            {
                if (_onbLayoutComplete == complete) return;
                _onbLayoutComplete = complete;

                if (MainNavPanel == null || OnboardingNavItem == null
                    || !MainNavPanel.Children.Contains(OnboardingNavItem))
                {
                    return;
                }

                MainNavPanel.Children.Remove(OnboardingNavItem);
                int insertAt;
                if (complete && SystemNavItem != null && MainNavPanel.Children.Contains(SystemNavItem))
                {
                    insertAt = MainNavPanel.Children.IndexOf(SystemNavItem) + 1; // right after Settings
                }
                else if (QuickNavItem != null && MainNavPanel.Children.Contains(QuickNavItem))
                {
                    insertAt = MainNavPanel.Children.IndexOf(QuickNavItem) + 1;  // right after Main
                }
                else
                {
                    insertAt = MainNavPanel.Children.Count;
                }
                if (insertAt < 0 || insertAt > MainNavPanel.Children.Count) insertAt = MainNavPanel.Children.Count;
                MainNavPanel.Children.Insert(insertAt, OnboardingNavItem);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ApplyOnboardingTabLayout failed: {ex.Message}");
            }
        }

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
