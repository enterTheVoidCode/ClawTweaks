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

            if (TDPModeComboBox != null)
                TDPModeComboBox.IsEnabled = !msiActive;

            if (msiActive)
            {
                // Hard-disable TDP slider and sub-controls; UpdateTDPSliderEnabledState
                // will also gate on msiActive, but we set IsEnabled=false explicitly
                // here so the control state is immediate (before the full refresh runs).
                if (TDPSlider != null)       TDPSlider.IsEnabled       = false;
                if (TDPBoostToggle != null)  TDPBoostToggle.IsEnabled  = false;
                if (AutoTDPToggle != null)   AutoTDPToggle.IsEnabled   = false;
                if (StickyTDPToggle != null) StickyTDPToggle.IsEnabled = false;
            }
            else
            {
                // Lift the gate — let the normal state machine decide the enabled state
                UpdateTDPSliderEnabledState();
            }

            // ── Controller Emulation + Gyro section ──────────────────────────
            if (MsiCenterControllerWarning != null)
                MsiCenterControllerWarning.Visibility = msiActive ? Visibility.Visible : Visibility.Collapsed;

            if (ControllerEmulationEnabledToggle != null && msiActive)
            {
                // Force-off while MSI Center M is active.
                // Do NOT call SetValue here — just visually/logically disable.
                ControllerEmulationEnabledToggle.IsEnabled = false;
            }
            else if (ControllerEmulationEnabledToggle != null && !msiActive)
            {
                // Re-enable only if the underlying support conditions are met
                // (ViGEmBus, HidHide etc.) — rely on the existing support-check
                // path that sets IsEnabled after helper reports capabilities.
                // We just remove our extra gate; the normal path will set the
                // final enabled state on the next UpdateControllerEmulationControlState call.
                UpdateControllerEmulationControlState();
            }

            Logger.Info($"UpdateMsiCenterGatedFeatures: msiActive={msiActive}");
        }
    }
}
