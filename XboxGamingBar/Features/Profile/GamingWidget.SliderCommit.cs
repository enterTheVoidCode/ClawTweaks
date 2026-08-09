using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using XboxGamingBar.Data;

namespace XboxGamingBar
{
    /// <summary>
    /// Slider settings are persisted when the user is FINISHED, not on every step.
    ///
    /// THE MEASUREMENT THAT FORCED THIS (2026-08-06, widget_2026-08-06_19.log). Every single watt on
    /// the PL1 slider ran the full profile save, and <see cref="SaveWidgetUiStateToProfile"/> ends in
    /// <c>UpdateProfileDisplay()</c>, which rebuilds the whole card list: enumerate every LocalSettings
    /// container, DESERIALIZE EVERY SAVED GAME PROFILE, parse a 33 KB snapshot. One step, 77 log lines,
    /// 22 profile loads — 19:47:14.514 → 19:47:15.548, i.e. 1.03 seconds per watt.
    ///
    /// It went unnoticed for as long as it did because the cost scales with the number of saved
    /// profiles: 0 profiles on 08-03, 11 on 08-05, 14 on 08-06. The loop never changed; the work inside
    /// it grew fourteen-fold, which is why it felt like a sudden regression rather than a slow one.
    ///
    /// The old ordering was exactly inverted. <see cref="WidgetSliderProperty"/> debounces the message
    /// to the helper by 500 ms — the CHEAP half — while the profile save ran synchronously off the same
    /// ValueChanged, unthrottled. Its own comment said so out loud.
    ///
    /// WHY ENGAGEMENT IS THE RIGHT BOUNDARY AND NOT ANOTHER TIMER. A gamepad user presses A to grab the
    /// slider, adjusts, and presses A again to let go — a commit boundary the platform already gives us
    /// as <c>Control.FocusDisengaged</c>, with no interval to guess. Pointer users never engage, so
    /// <c>PointerCaptureLost</c> is their equivalent, and <c>LostFocus</c> catches everything else
    /// (D-pad away while still engaged, tab switch, the widget being closed).
    ///
    /// The HARDWARE still follows live while adjusting: the helper push keeps its 500 ms debounce and is
    /// untouched here. Only the persist-and-redraw moves to the end. You feel every watt; you pay for
    /// the bookkeeping once.
    ///
    /// NOT DONE HERE, deliberately: B-to-revert (restore the value the slider had at engage time).
    /// FocusDisengaged fires identically for A and B, so telling commit from cancel needs the key that
    /// caused it — and that A arrives as Space is measured, while B arriving as Escape is NOT. That is
    /// its own change with its own measurement, see [[widget-dpad-spine-slider-rules]].
    /// </summary>
    public sealed partial class GamingWidget
    {
        // Sliders the user has moved since their last commit. A set, not a flag: several sliders can be
        // mid-edit at once (pointer on one, focus on another), and each has to commit on its own event.
        private readonly HashSet<Slider> _slidersAwaitingProfileCommit = new HashSet<Slider>();

        /// <summary>
        /// Wires a slider to save into the profile ONCE, when the user is done with it, instead of on
        /// every value change. Use in place of <c>slider.ValueChanged += SettingChanged</c>.
        /// </summary>
        private void WireSliderProfileCommit(Slider slider)
        {
            if (slider == null) return;
            slider.ValueChanged      += SliderProfile_ValueChanged;
            slider.FocusDisengaged   += SliderProfile_FocusDisengaged;   // gamepad: second A press
            slider.PointerCaptureLost += SliderProfile_PointerCaptureLost; // mouse/touch: released
            slider.LostFocus         += SliderProfile_LostFocus;          // everything else
        }

        /// <summary>Notes that this slider now holds an unsaved user value. Deliberately does NOT save.</summary>
        private void SliderProfile_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Same gate SettingChanged applies. Checked HERE too, so a helper sync or a profile load can
            // never mark a slider dirty — otherwise the next unrelated commit would write a value the
            // user never dialled in.
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync
                || WidgetSliderProperty.HelperSyncCount > 0)
                return;

            if (sender is Slider slider) _slidersAwaitingProfileCommit.Add(slider);
        }

        private void SliderProfile_FocusDisengaged(Control sender, FocusDisengagedEventArgs args)
            => CommitSliderProfileValue(sender as Slider, "disengaged");

        private void SliderProfile_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
            => CommitSliderProfileValue(sender as Slider, "pointer released");

        private void SliderProfile_LostFocus(object sender, RoutedEventArgs e)
            => CommitSliderProfileValue(sender as Slider, "focus left");

        /// <summary>
        /// Writes the slider's value into the current profile, once. A no-op unless that slider is
        /// actually dirty, so the three commit events cannot produce three saves for one edit.
        /// </summary>
        private void CommitSliderProfileValue(Slider slider, string why)
        {
            if (slider == null) return;
            if (!_slidersAwaitingProfileCommit.Remove(slider)) return;

            Logger.Info($"Slider '{slider.Name}' committed to profile ({why}, value={slider.Value}).");
            SettingChanged(slider, null);
        }

        /// <summary>
        /// Saves anything still held open. Called where a slider can be abandoned mid-edit without any
        /// of the three events arriving — closing the widget is the one that matters, because that is
        /// where "deferring the save" would otherwise turn into "losing the value".
        /// </summary>
        internal void FlushPendingSliderProfileCommits()
        {
            if (_slidersAwaitingProfileCommit.Count == 0) return;

            var pending = new List<Slider>(_slidersAwaitingProfileCommit);
            _slidersAwaitingProfileCommit.Clear();
            foreach (var slider in pending)
            {
                try
                {
                    Logger.Info($"Slider '{slider.Name}' committed to profile (flush, value={slider.Value}).");
                    SettingChanged(slider, null);
                }
                catch (Exception ex) { Logger.Warn($"Flushing slider '{slider.Name}' failed: {ex.Message}"); }
            }
        }
    }
}
