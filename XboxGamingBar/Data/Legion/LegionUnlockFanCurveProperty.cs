using Shared.Enums;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // Mirrors the *active* mode's unlock state from the helper. We still auto-bind to
    // the ToggleSwitch UI for inbound display sync (so e.g. helper-pushed unlock=true
    // for the active mode flips the visible toggle when the user is viewing the active
    // mode), but outbound user toggles do NOT route through this property anymore —
    // they go through LegionUnlockFanCurvePerMode keyed by the dropdown-selected mode.
    // Overriding ToggleSwitch_ValueChanged to a no-op suppresses the auto-bound send.
    internal class LegionUnlockFanCurveProperty : WidgetToggleProperty
    {
        public LegionUnlockFanCurveProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LegionUnlockFanCurve, inUI, inOwner)
        {
        }

        protected override void ToggleSwitch_ValueChanged(object sender, RoutedEventArgs e)
        {
            // Outbound is handled by LegionUnlockFanCurveToggle_Toggled in the widget
            // code-behind via the per-mode pipe channel, with the dropdown-selected
            // mode as target. Do NOT call SetValue here — it would route to the active
            // mode and clobber non-active edits.
        }
    }
}
