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
            => SetOnbToolRow(OnbViGEmStatus, OnbViGEmInstallBtn, OnbViGEmUninstallBtn, "ViGEmBus", installed);

        private void UpdateOnboardingHidHide(bool installed)
            => SetOnbToolRow(OnbHidHideStatus, OnbHidHideInstallBtn, OnbHidHideUninstallBtn, "HidHide", installed);

        private void UpdateOnboardingRtss(bool installed)
            => SetOnbToolRow(OnbRtssStatus, OnbRtssInstallBtn, OnbRtssUninstallBtn, "RTSS", installed);

        private void UpdateOnboardingPawnIO(bool installed)
            => SetOnbToolRow(OnbPawnIOStatus, OnbPawnIOInstallBtn, OnbPawnIOUninstallBtn, "PawnIO", installed);

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
