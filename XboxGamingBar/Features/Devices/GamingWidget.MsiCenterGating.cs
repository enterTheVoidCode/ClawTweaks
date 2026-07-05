using NLog;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace XboxGamingBar
{
    /// <summary>
    /// MSI Center M conflict gating.
    ///
    /// When MSI Center M is active it owns TDP, controller input, and gyro.
    /// ClawTweaks must not try to control these simultaneously — doing so would
    /// cause conflicts (TDP writes rejected by firmware, HidHide fighting MSI
    /// driver, gyro state undefined).
    ///
    /// This file:
    ///   - Subscribes to msiCenterActive.PropertyChanged
    ///   - Disables TDP controls (Mode, Slider, Boost, AutoTDP) when active
    ///   - Disables ControllerEmulationEnabledToggle when active
    ///   - Shows/hides amber warning banners in both sections
    ///   - Re-enables everything (via the normal state-update functions) when
    ///     MSI Center M is stopped
    /// </summary>
    public sealed partial class GamingWidget
    {
        private void SubscribeMsiCenterGating()
        {
            if (msiCenterActive != null)
                msiCenterActive.PropertyChanged += MsiCenterActive_PropertyChanged;
        }

        private async void MsiCenterActive_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UpdateMsiCenterGatedFeatures();
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"MsiCenterActive_PropertyChanged dispatch error: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies or lifts the MSI Center M feature gate.
        /// Called whenever msiCenterActive changes and also after initial sync.
        /// Must run on the UI thread.
        /// </summary>
        internal void UpdateMsiCenterGatedFeatures()
        {
            bool msiActive = msiCenterActive?.Value == true;

            // ── TDP section ──────────────────────────────────────────────────
            if (MsiCenterTDPWarning != null)
                MsiCenterTDPWarning.Visibility = Visibility.Collapsed;  // TDP no longer gated (registry mirror applies via MSI)

            // TDPModeCard is Legion-only (hardware presets). Always collapsed for MSI Claw —
            // the MSI Center warning has been moved inline into the TDP Power Limit card.
            // No action needed here for non-Legion devices.

            if (TDPModeComboBox != null)
                TDPModeComboBox.IsEnabled = true;

            // TDP is NO LONGER gated by MSI Center M. The helper mirrors PL1/PL2 into MSI Center M's own
            // model (HKLM\...\User Scenario\ManualPL*), which MSI watches and applies to the EC itself — so
            // setting TDP works AND stays MSI-conform while MSI Center M runs. The old lock only existed
            // because the direct EC/WMI write was refused while MSI held the ACPI WMI. Let the normal
            // state machine decide the enabled state regardless of msiActive.
            if (TDPBoostFPPTSlider != null)     TDPBoostFPPTSlider.IsEnabled     = true;
            if (TDPBoostFPPTSliderCard != null) TDPBoostFPPTSliderCard.IsEnabled = true;
            UpdateTDPSliderEnabledState();

            // ── Controller Emulation + Gyro section ──────────────────────────
            if (MsiCenterControllerWarning != null)
                MsiCenterControllerWarning.Visibility = msiActive ? Visibility.Visible : Visibility.Collapsed;

            // Delegate IsEnabled to the centralized gate — it checks MSI, Steam, and onboarding.
            UpdateControllerEmulationToggleEnabled();

            Logger.Info($"UpdateMsiCenterGatedFeatures: msiActive={msiActive}");
        }
    }
}
