using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        /// <summary>
        /// The PL2 slider's appearance, in ONE place: shown and editable exactly while Overboost is on.
        /// Two properties, one fact — see the note in <see cref="TDPBoostToggle_Toggled"/> for what it
        /// cost to have them set from two different spots.
        /// </summary>
        private void ApplyTdpBoostSliderVisuals()
        {
            bool on = TDPBoostToggle?.IsOn == true;
            if (TDPBoostSliderArea != null)
                TDPBoostSliderArea.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (TDPBoostFPPTSliderCard != null)
                TDPBoostFPPTSliderCard.IsEnabled = on;
        }

        private void TDPBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (TDPBoostToggle == null) return;

            // Show the PL2-Boost slider only while Overboost is ON; collapse it (compact card) when
            // off. Done before the early-returns below so it also tracks programmatic toggles from
            // profile/helper sync (those skip the save/send logic but the visibility must still follow).
            //
            // IsEnabled BELONGS HERE, NEXT TO Visibility, and used to sit below the early-returns.
            // Both express the same single fact — "Overboost is on" — and separating them by a return
            // meant a programmatic toggle (helper sync, profile load, TDP-mode change) made the slider
            // APPEAR while leaving it disabled: reported 2026-08-06 as "PL2 slider greyed out although
            // the toggle is on", curable only by flipping the toggle twice by hand, which is precisely
            // the one path that reached the old line. It surfaced with the PL1/PL2 card merge because
            // that is when visibility became conditional at all; before, the mismatch had nothing to
            // reveal it. If a third property ever describes this same state, put it here too.
            ApplyTdpBoostSliderVisuals();

            if (isApplyingHelperUpdate) return;
            // Skip during mode changes - don't save forced-off state
            if (isUpdatingTDPMode) return;
            // Skip while LoadTDPBoostSettings() is applying stored values
            if (isLoadingTDPBoostSettings) return;

            Logger.Info($"TDP Boost toggled to: {TDPBoostToggle.IsOn}");

            // Send to helper
            tdpBoostEnabled?.SetValue(TDPBoostToggle.IsOn);

            // Save to local settings for persistence across widget restarts
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostEnabled"] = TDPBoostToggle.IsOn;

            // The helper now persists and applies PL2 itself (GlobalPL2Boost + per-game
            // TDPBoostFPPTWatts), so re-asserting the slider here is no longer needed — and it was
            // actively harmful: on a widget instance that had not yet received the helper's value the
            // slider still held its DEFAULT (Maximum, 37W on the A2VM), so this pushed 37W over the
            // user's real PL2. Same clobber shape PL1 had with its 30W ComboBox default. The slider's
            // own ValueChanged handler remains the one place a user PL2 change is sent.

            // Save to profile if not loading. The early-returns above already excluded helper sync
            // (isApplyingHelperUpdate), mode changes and LoadTDPBoostSettings, so reaching here means
            // the user flipped the toggle — mark it so the save is allowed to persist the TDP group.
            if (!isLoadingProfile && !isSwitchingProfile)
            {
                SaveWidgetUiStateToProfile(currentProfileName);
            }
        }

        private void TDPBoostSPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // SPPT Boost slider removed — Intel Lunar Lake uses PL1/PL2 only (no SPPT).
        }

        /// <summary>
        /// Puts the helper's PL2 on the sliders. Without this the PL2 channel is WRITE-ONLY: the widget
        /// sends the user's value and never learns the helper's.
        ///
        /// HOW THAT WENT UNNOTICED. PL1 and the six display sliders are WidgetSliderProperty instances
        /// constructed WITH their control, so the base class pushes every helper value into the UI on
        /// its own. PL2's widget property was built with a null control
        /// (<see cref="Data.TDPBoostFPPTProperty"/>) and nothing in the widget ever read it back — a
        /// grep for "tdpBoostFPPT." found exactly one hit, and that was the send. So the slider showed
        /// whatever LocalSettings["TDPBoostFPPT"] held, i.e. the last value dragged ANYWHERE, while the
        /// helper ran the per-game one. Measured 2026-08-06 on a Silksong start: helper applied
        /// PL2=27W, the notification and the OSD said 27W (both read the profile directly), the slider
        /// sat at 22W. It dates from the single-writer change: the widget stopped reading PL2 from its
        /// own profile copy and never gained a replacement source.
        ///
        /// Receive-only on purpose. It sets Value under the same _syncingBoostSlider guard the mirror
        /// logic uses, so no send goes back out and the helper stays the only writer
        /// ([[tdp-single-writer-global]]).
        /// </summary>
        private void TdpBoostFPPT_HelperValueChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RunOnUiThread(() =>
            {
                try
                {
                    int pl2 = tdpBoostFPPT?.Value ?? 0;
                    if (pl2 <= 0) return;   // 0 = "no PL2 in force"; leave the last shown value alone

                    if (_syncingBoostSlider) return;
                    _syncingBoostSlider = true;
                    try
                    {
                        if (TDPBoostFPPTSliderCard != null && (int)Math.Round(TDPBoostFPPTSliderCard.Value) != pl2)
                            TDPBoostFPPTSliderCard.Value = pl2;
                        if (TDPBoostFPPTSlider != null && (int)Math.Round(TDPBoostFPPTSlider.Value) != pl2)
                            TDPBoostFPPTSlider.Value = pl2;
                        if (TDPBoostFPPTValue != null) TDPBoostFPPTValue.Text = $"{pl2}W";
                        if (TDPBoostFPPTValueInCard != null) TDPBoostFPPTValueInCard.Text = $"{pl2}W";
                    }
                    finally { _syncingBoostSlider = false; }

                    BuildWattScaleLabels(TDPBoostScaleLabels, TDPBoostFPPTSliderCard);
                    Logger.Info($"PL2 slider follows the helper: {pl2}W");
                }
                catch (Exception ex) { Logger.Warn($"TdpBoostFPPT_HelperValueChanged: {ex.Message}"); }
            });
        }

        private bool _syncingBoostSlider = false;

        private void TDPBoostFPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings || _syncingBoostSlider) return;
            if (TDPBoostFPPTSlider == null) return;

            int fpptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost FPPT changed to: {fpptBoost}W");

            // Sync both labels and both sliders (card + settings panel)
            if (TDPBoostFPPTValue != null)
                TDPBoostFPPTValue.Text = $"{fpptBoost}W";
            if (TDPBoostFPPTValueInCard != null)
                TDPBoostFPPTValueInCard.Text = $"{fpptBoost}W";

            _syncingBoostSlider = true;
            try
            {
                if (TDPBoostFPPTSliderCard != null && (int)Math.Round(TDPBoostFPPTSliderCard.Value) != fpptBoost)
                    TDPBoostFPPTSliderCard.Value = fpptBoost;
                if (TDPBoostFPPTSlider != null && sender != TDPBoostFPPTSlider &&
                    (int)Math.Round(TDPBoostFPPTSlider.Value) != fpptBoost)
                    TDPBoostFPPTSlider.Value = fpptBoost;
            }
            finally { _syncingBoostSlider = false; }

            // Keep the PL2 watt scale's highlight tracking the thumb.
            BuildWattScaleLabels(TDPBoostScaleLabels, TDPBoostFPPTSliderCard);

            // Send to helper
            tdpBoostFPPT?.SetValue(fpptBoost);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostFPPT"] = fpptBoost;

            // Persist to profile (always saved alongside TDP Boost toggle). Same reasoning as the
            // toggle: isLoadingTDPBoostSettings / _syncingBoostSlider already ruled out everything
            // that is not a user drag.
            //
            // MARKED, not saved. PL2 has its own ValueChanged instead of going through
            // SettingChanged, which is exactly why it was missed when the other sliders moved to the
            // commit boundary — PL1 became responsive and PL2 stayed laggy, reported 2026-08-06. The
            // save itself is the expensive part (it rebuilds every profile card); the helper still
            // gets the value immediately through tdpBoostFPPT.SetValue above.
            if (!isLoadingProfile && !isSwitchingProfile)
            {
                if (TDPBoostFPPTSliderCard != null) _slidersAwaitingProfileCommit.Add(TDPBoostFPPTSliderCard);
                if (TDPBoostFPPTSlider != null)     _slidersAwaitingProfileCommit.Add(TDPBoostFPPTSlider);
            }
        }

        private void LoadTDPBoostSettings()
        {
            isLoadingTDPBoostSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load TDP Boost enabled state (default OFF)
                if (settings.Values.TryGetValue("TDPBoostEnabled", out object enabledObj) && enabledObj is bool enabled)
                {
                    if (TDPBoostToggle != null)
                        TDPBoostToggle.IsOn = enabled;
                    // Not just IsEnabled: setting IsOn only raises Toggled when the value actually
                    // CHANGES, so a stored value equal to the current one would leave the slider's
                    // appearance untouched. Calling the one owner directly covers both cases.
                    ApplyTdpBoostSliderVisuals();
                    tdpBoostEnabled?.SetValue(enabled);
                    Logger.Info($"TDP Boost enabled state loaded from settings: {enabled}");
                }

                // Load PL2-Boost — absolute PL2 target value. The fallback used to be the slider's
                // Maximum, which is no longer a device ceiling: until the helper reports DeviceMaxPL2
                // the slider carries a deliberately-too-wide placeholder, and seeding a default from
                // it would invent a nonsense PL2 on a fresh install. The device ceiling now comes from
                // the helper property itself, and if that has not arrived either there is simply no
                // local default worth having — the helper pushes the authoritative PL2 on connect.
                int fpptBoost = -1;
                if (settings.Values.TryGetValue("TDPBoostFPPT", out object fpptObj) && fpptObj != null)
                {
                    try
                    {
                        fpptBoost = Convert.ToInt32(fpptObj);
                    }
                    catch
                    {
                        fpptBoost = 3;
                    }
                }
                if (fpptBoost <= 0)
                {
                    fpptBoost = deviceMaxPL2?.Value ?? 0;
                }
                if (fpptBoost <= 0)
                {
                    Logger.Info("TDP Boost settings loaded - PL2-Boost: no stored value and no device ceiling yet, leaving it to the helper's push.");
                    return;
                }
                if (TDPBoostFPPTSlider != null)
                    TDPBoostFPPTSlider.Value = fpptBoost;
                if (TDPBoostFPPTSliderCard != null)
                    TDPBoostFPPTSliderCard.Value = fpptBoost;
                if (TDPBoostFPPTValue != null)
                    TDPBoostFPPTValue.Text = $"{fpptBoost}W";
                if (TDPBoostFPPTValueInCard != null)
                    TDPBoostFPPTValueInCard.Text = $"{fpptBoost}W";
                // Display only — the helper owns PL2 and pushes the authoritative value. Sending the
                // locally-loaded value back here overwrote it with a stale/default one on every load.
                // Ensure value is saved (in case it was missing or converted)
                settings.Values["TDPBoostFPPT"] = fpptBoost;

                Logger.Info($"TDP Boost settings loaded - PL2-Boost: {fpptBoost}W");
            }
            finally
            {
                isLoadingTDPBoostSettings = false;
            }
        }

        private void TDPBoostEnabled_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // The helper syncs TDPBoostEnabled. The old reasoning here — "the widget (LocalSettings) is
            // the source of truth, the helper doesn't persist TDPBoostEnabled and always sends False on
            // a fresh start" — no longer holds: the helper now persists both the Overboost state
            // (GlobalTDPBoostEnabled) and the PL2 value (GlobalPL2Boost), and applies the per-game
            // values itself. So this callback no longer pushes anything back; it only reflects what the
            // helper reports. Pushing from here is what let a not-yet-synced instance send its slider
            // default (37W on the A2VM) over the user's PL2.
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (TDPBoostToggle == null || tdpBoostEnabled == null) return;
                if (TDPBoostToggle.IsOn == tdpBoostEnabled.Value) return;

                // isApplyingHelperUpdate is mandatory here: assigning IsOn raises TDPBoostToggle_Toggled,
                // which would send the value straight back and save it to the profile — the echo loop
                // this whole change exists to remove.
                bool previous = isApplyingHelperUpdate;
                isApplyingHelperUpdate = true;
                try
                {
                    TDPBoostToggle.IsOn = tdpBoostEnabled.Value;
                    Logger.Debug($"TDP Boost PropertyChanged - adopted helper state: {tdpBoostEnabled.Value}");
                }
                finally { isApplyingHelperUpdate = previous; }
            });
        }

    }
}
