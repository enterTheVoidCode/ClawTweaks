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
                MsiCenterTDPWarning.Visibility = msiActive ? Visibility.Visible : Visibility.Collapsed;

            // TDPModeCard is Legion-only (hardware presets). Always collapsed for MSI Claw —
            // the MSI Center warning has been moved inline into the TDP Power Limit card.
            // No action needed here for non-Legion devices.

            if (TDPModeComboBox != null)
                TDPModeComboBox.IsEnabled = !msiActive;

            if (msiActive)
            {
                // Disable all TDP controls while MSI Center M owns the hardware.
                if (TDPSlider != null)              TDPSlider.IsEnabled              = false;  // was incorrectly = true before
                if (TDPBoostToggle != null)         TDPBoostToggle.IsEnabled         = false;
                if (TDPBoostFPPTSlider != null)     TDPBoostFPPTSlider.IsEnabled     = false;
                if (TDPBoostFPPTSliderCard != null) TDPBoostFPPTSliderCard.IsEnabled = false;
                if (AutoTDPToggle != null)          AutoTDPToggle.IsEnabled          = false;
                if (StickyTDPToggle != null)        StickyTDPToggle.IsEnabled        = false;
            }
            else
            {
                // Lift the gate — let the normal state machine decide the enabled state
                if (TDPBoostFPPTSlider != null)     TDPBoostFPPTSlider.IsEnabled     = true;
                if (TDPBoostFPPTSliderCard != null) TDPBoostFPPTSliderCard.IsEnabled = true;
                UpdateTDPSliderEnabledState();
            }

            // ── Controller Emulation + Gyro section ──────────────────────────
            if (MsiCenterControllerWarning != null)
                MsiCenterControllerWarning.Visibility = msiActive ? Visibility.Visible : Visibility.Collapsed;

            // Delegate IsEnabled to the centralized gate — it checks MSI, Steam, and onboarding.
            UpdateControllerEmulationToggleEnabled();

            Logger.Info($"UpdateMsiCenterGatedFeatures: msiActive={msiActive}");
        }
    }
}
