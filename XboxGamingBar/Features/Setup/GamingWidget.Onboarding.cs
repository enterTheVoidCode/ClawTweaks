using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// Onboarding tab: a guided setup flow.
    ///   1. Install the four required tools (ViGEm, HidHide, RTSS, PawnIO).
    ///   2. If MSI Center M is running, disable it (it blocks controller emulation).
    ///   3. Enable controller emulation.
    /// Reuses the existing install/uninstall trigger properties and the
    /// MsiCenterActive / ControllerEmulationEnabled properties.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private static readonly SolidColorBrush OnbGreenBrush = new SolidColorBrush(Colors.LimeGreen);
        private static readonly SolidColorBrush OnbGrayBrush = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136));
        private static readonly SolidColorBrush OnbAmberBrush = new SolidColorBrush(Color.FromArgb(255, 255, 165, 0));

        // Dependency gate: the Quick Settings tiles are locked until all four required tools are
        // installed AND controller emulation is enabled. Default open so already-set-up users are
        // not briefly locked; RecomputeDependencyGate() re-evaluates from the live property values.
        private bool _dependencyGateOpen = true;

        // Tracks the last-applied onboarding tab layout (null = not yet applied) so the nav item
        // is only re-ordered on an actual complete/incomplete transition, not on every recompute.
        private bool? _onbLayoutComplete = null;

        // Called when the Onboarding tab becomes visible and after any onboarding action,
        // so the whole flow reflects current state without per-property subscriptions.
        private void RefreshOnboardingTab()
        {
            try
            {
                UpdateOnboardingViGEm(vigemBusInstalled?.Value == true);
                UpdateOnboardingHidHide(hidHideInstalled?.Value == true);
                UpdateOnboardingRtss(rtssInstalled?.Value == true);
                UpdateOnboardingPawnIO(pawnIOInstalled?.Value == true);
                UpdateOnboardingSteps();
            }
            catch (Exception ex)
            {
                Logger.Warn($"RefreshOnboardingTab failed: {ex.Message}");
            }
        }

        private static void SetOnbToolRow(TextBlock status, Button installBtn, Button uninstallBtn, string toolName, bool installed)
        {
            if (status != null)
            {
                status.Text = installed ? $"{toolName}: Installed" : $"{toolName}: Not installed";
                status.Foreground = installed ? OnbGreenBrush : OnbGrayBrush;
            }
            if (installBtn != null)
            {
                installBtn.Content = installed ? "Installed" : "Install";
                installBtn.IsEnabled = !installed;
            }
            if (uninstallBtn != null)
            {
                uninstallBtn.Content = "Uninstall";
                uninstallBtn.IsEnabled = installed;
            }
        }

        private void UpdateOnboardingViGEm(bool installed)
        {
            SetOnbToolRow(OnbViGEmStatus, OnbViGEmInstallBtn, OnbViGEmUninstallBtn, "ViGEmBus", installed);
            RecomputeDependencyGate();
        }

        private void UpdateOnboardingHidHide(bool installed)
        {
            SetOnbToolRow(OnbHidHideStatus, OnbHidHideInstallBtn, OnbHidHideUninstallBtn, "HidHide", installed);
            RecomputeDependencyGate();
        }

        private void UpdateOnboardingRtss(bool installed)
        {
            SetOnbToolRow(OnbRtssStatus, OnbRtssInstallBtn, OnbRtssUninstallBtn, "RTSS", installed);
            RecomputeDependencyGate();
        }

        private void UpdateOnboardingPawnIO(bool installed)
        {
            SetOnbToolRow(OnbPawnIOStatus, OnbPawnIOInstallBtn, OnbPawnIOUninstallBtn, "PawnIO", installed);
            RecomputeDependencyGate();
        }

        // RTSS installed-status callback (replaces the direct UpdateFPSLimitControls wiring):
        // keeps the FPS-limit controls in sync AND updates the onboarding RTSS row.
        private void OnRtssInstalledChanged(bool installed)
        {
            UpdateFPSLimitControls(installed);
            UpdateOnboardingRtss(installed);
        }

        // Steps 2 + 3 depend on each other: controller emulation can only be enabled
        // once MSI Center M is off (matches the existing MsiCenterGating behaviour).
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
        /// Dependency gate: lock the Quick Settings tiles until all four required tools are
        /// installed AND controller emulation is enabled. Shows a warning banner on the Quick tab
        /// and a badge on the Setup nav item while setup is incomplete. Cheap; safe to call often.
        /// </summary>
        internal void RecomputeDependencyGate()
        {
            try
            {
                bool emuOn = controllerEmulationEnabled?.Value == true;
                bool open = (vigemBusInstalled?.Value == true)
                         && (hidHideInstalled?.Value == true)
                         && (rtssInstalled?.Value == true)
                         && (pawnIOInstalled?.Value == true)
                         && emuOn;
                _dependencyGateOpen = open;

                // Hide the other tabs until onboarding is complete; move the Onboarding tab to the
                // far right once it is.
                ApplyOnboardingTabLayout(open);

                // Lock/unlock all Quick Settings tiles at once. StackPanel is a Panel (not a Control)
                // so it has no IsEnabled — gate interactivity via hit-testing and dim it so the
                // locked state is visible.
                if (QuickSettingsTilesContainer != null)
                {
                    QuickSettingsTilesContainer.IsHitTestVisible = open;
                    QuickSettingsTilesContainer.Opacity = open ? 1.0 : 0.4;
                }

                // Warning banner on the Quick tab.
                if (MissingAddonsWarning != null)
                {
                    MissingAddonsWarning.IsOpen = !open;
                    if (!open)
                    {
                        var missing = new System.Collections.Generic.List<string>();
                        if (vigemBusInstalled?.Value != true) missing.Add("ViGEmBus");
                        if (hidHideInstalled?.Value != true) missing.Add("HidHide");
                        if (rtssInstalled?.Value != true) missing.Add("RTSS");
                        if (pawnIOInstalled?.Value != true) missing.Add("PawnIO");
                        if (!emuOn) missing.Add("controller emulation");
                        MissingAddonsWarning.Message =
                            "Finish setup to use ClawTweaks — still required: " + string.Join(", ", missing) + ".";
                    }
                }

                // Warning badge on the Setup nav item.
                if (OnboardingNavBadge != null)
                {
                    OnboardingNavBadge.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"RecomputeDependencyGate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// While onboarding is incomplete, only Main (Quick tiles) and the Onboarding tab are shown
        /// and Onboarding sits right after Main. Once complete, all tabs are shown and the Onboarding
        /// tab moves to the far right, after Settings (System).
        /// </summary>
        private void ApplyOnboardingTabLayout(bool complete)
        {
            try
            {
                // Tabs hidden during onboarding, shown once complete. (Display/Fan stay as-is —
                // they are device-gated elsewhere.)
                Windows.UI.Xaml.Visibility v = complete ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
                if (PerformanceNavItem != null) PerformanceNavItem.Visibility = v;
                if (LegionNavItem != null) LegionNavItem.Visibility = v;
                if (ScalingNavItem != null) ScalingNavItem.Visibility = v;
                if (ProfilesNavItem != null) ProfilesNavItem.Visibility = v;
                if (SystemNavItem != null) SystemNavItem.Visibility = v;

                // Fan + Display are device-gated (shown only on the MSI Claw). Hide them during
                // onboarding too; on completion restore them to their device-appropriate visibility.
                Windows.UI.Xaml.Visibility deviceVis = IsMsiClawDevice()
                    ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
                Windows.UI.Xaml.Visibility fanDisplayVis = complete ? deviceVis : Windows.UI.Xaml.Visibility.Collapsed;
                if (FanNavItem != null) FanNavItem.Visibility = fanDisplayVis;
                if (DisplayNavItem != null) DisplayNavItem.Visibility = fanDisplayVis;

                // Only reorder on an actual complete/incomplete transition.
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

        // InfoBar action button on the Quick tab: jump to the Setup/Onboarding tab.
        private void MissingAddonsGoToSetup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OnboardingNavItem != null)
                {
                    OnboardingNavItem.IsChecked = true; // fires NavRadioButton_Checked
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MissingAddonsGoToSetup failed: {ex.Message}");
            }
        }

        // --- Tool install/uninstall buttons (reuse the shared trigger properties) ---

        private void OnbViGEmInstall_Click(object sender, RoutedEventArgs e)
        {
            if (OnbViGEmInstallBtn != null) { OnbViGEmInstallBtn.Content = "Installing..."; OnbViGEmInstallBtn.IsEnabled = false; }
            installViGEmBus?.TriggerInstall();
        }
        private void OnbViGEmUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (OnbViGEmUninstallBtn != null) { OnbViGEmUninstallBtn.Content = "Uninstalling..."; OnbViGEmUninstallBtn.IsEnabled = false; }
            uninstallViGEm?.Trigger("uninstall");
        }

        private void OnbHidHideInstall_Click(object sender, RoutedEventArgs e)
        {
            if (OnbHidHideInstallBtn != null) { OnbHidHideInstallBtn.Content = "Installing..."; OnbHidHideInstallBtn.IsEnabled = false; }
            installHidHide?.TriggerInstall();
        }
        private void OnbHidHideUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (OnbHidHideUninstallBtn != null) { OnbHidHideUninstallBtn.Content = "Uninstalling..."; OnbHidHideUninstallBtn.IsEnabled = false; }
            uninstallHidHide?.Trigger("uninstall");
        }

        private void OnbRtssInstall_Click(object sender, RoutedEventArgs e)
        {
            if (OnbRtssInstallBtn != null) { OnbRtssInstallBtn.Content = "Installing..."; OnbRtssInstallBtn.IsEnabled = false; }
            installRTSS?.Trigger("install");
        }
        private void OnbRtssUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (OnbRtssUninstallBtn != null) { OnbRtssUninstallBtn.Content = "Uninstalling..."; OnbRtssUninstallBtn.IsEnabled = false; }
            uninstallRTSS?.Trigger("uninstall");
        }

        private void OnbPawnIOInstall_Click(object sender, RoutedEventArgs e)
        {
            if (OnbPawnIOInstallBtn != null) { OnbPawnIOInstallBtn.Content = "Installing..."; OnbPawnIOInstallBtn.IsEnabled = false; }
            installPawnIO?.TriggerInstall();
        }
        private void OnbPawnIOUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (OnbPawnIOUninstallBtn != null) { OnbPawnIOUninstallBtn.Content = "Uninstalling..."; OnbPawnIOUninstallBtn.IsEnabled = false; }
            uninstallPawnIO?.Trigger("uninstall");
        }

        // --- Step 2: disable MSI Center M ---
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

        // --- Step 3: enable controller emulation ---
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
    }
}
