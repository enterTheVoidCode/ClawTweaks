using System;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using XboxGamingBar.Data;

namespace XboxGamingBar
{
    /// <summary>
    /// CPU advanced section (ToothNClaw port): collapsible "CPU" card in the Performance tab
    /// holding Boost on/off, Processor Scheduling Policy and P/E core max frequency. Boost modes
    /// (Enabled/Aggressive/Efficient.../etc.) were removed — there's no meaningful practical
    /// difference between them, and having a mode dropdown AND an on/off tile as two separate
    /// writers to the same Windows PERFBOOSTMODE setting caused repeated desync bugs (the tile
    /// and the dropdown racing each other, a corrupted profile value getting "remembered" and
    /// silently reapplied, etc.). Boost is now a single on/off value, one writer, plainly logged.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private CpuIntComboProperty schedulingPolicy;
        private CpuIntComboProperty maxPCoreFreq;
        private CpuIntComboProperty maxECoreFreq;

        private void InitializeCpuAdvanced()
        {
            try
            {
                UpdateCpuBoostToggleEnabled();
                UpdateCpuBoostStatusText();
            }
            catch (Exception ex)
            {
                Logger.Debug($"InitializeCpuAdvanced: {ex.Message}");
            }
        }

        /// <summary>
        /// Keeps the "Boost"/"Boost off" label in the CPU card header in sync with the toggle,
        /// regardless of whether it changed from a user click or a helper sync (both flow through
        /// ToggleSwitch.IsOn/Toggled, see WidgetToggleProperty.NotifyPropertyChanged).
        /// </summary>
        private void CPUBoostToggle_StatusChanged(object sender, RoutedEventArgs e) => UpdateCpuBoostStatusText();

        private void UpdateCpuBoostStatusText()
        {
            if (CpuBoostStatusText == null || CPUBoostToggle == null) return;
            bool on = CPUBoostToggle.IsOn;
            CpuBoostStatusText.Text = on ? "Boost" : "Boost off";
            CpuBoostStatusText.Foreground = new SolidColorBrush(on ? Color.FromArgb(255, 76, 175, 80) : Color.FromArgb(255, 136, 136, 136));
        }

        /// <summary>
        /// The Boost toggle is only meaningful when boost is AVAILABLE (Max CPU State ≥ 100 %) —
        /// Windows can't boost above a sub-100 % max state, so we grey it out then.
        /// </summary>
        private void UpdateCpuBoostToggleEnabled()
        {
            bool canBoost = MaxCPUStateComboBox == null || GetSelectedCPUStateValue(MaxCPUStateComboBox) >= 100;
            if (CPUBoostToggle != null)
            {
                CPUBoostToggle.IsEnabled = canBoost;
            }
        }

        private static int GetSelectedTagInt(ComboBox combo, int fallback)
        {
            if (combo?.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int v))
                return v;
            return fallback;
        }

        /// <summary>
        /// Builds a compact one-line CPU advanced summary for the live profile cards
        /// (e.g. "Aggressive · Prefer P · P4800 · E2400"). Returns null when nothing is set.
        /// </summary>
        private string BuildCpuAdvancedSummary(PerformanceProfile p)
        {
            if (p == null) return null;
            var parts = new System.Collections.Generic.List<string>();

            string sched = GetSchedulingPolicyName(p.ProcessorSchedulingPolicy);
            if (p.ProcessorSchedulingPolicy >= 0 && sched != null) parts.Add(sched);

            if (p.MaxPCoreFreqMHz > 0) parts.Add($"P{p.MaxPCoreFreqMHz}");
            if (p.MaxECoreFreqMHz > 0) parts.Add($"E{p.MaxECoreFreqMHz}");

            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        /// <summary>
        /// Syncs the CPU advanced combo selections (Scheduling Policy, P/E max freq)
        /// from a profile — UI ONLY. The HELPER owns CPU advanced state: it applies the active
        /// profile's values on every switch (Program.ProfileHandlers Apply... paths) AND re-enforces
        /// them every 3 s against Windows scheme resets, then pushes the live values back via the
        /// property BatchSync (on open) / per-property push (in-session switch). The widget must NOT
        /// push its own (possibly stale / default-0) stored values here, or it overrides the user's
        /// last P/E freq on every Game Bar reopen — the cap "flew out" to unlimited
        /// because this path pushed MaxPCoreFreqMHz=0 over the helper's enforced value. Same fix as
        /// the Intel Display path above. UI follows the helper.
        /// </summary>
        private void ApplyCpuAdvancedFromProfile(PerformanceProfile profile)
        {
            try
            {
                // INTENTIONALLY does NOT set the advanced combos from the widget's profile copy.
                // The HELPER is the source of truth for Scheduling Policy / P-/E-core max
                // freq: it holds the live value in memory, re-applies it every 3 s, and pushes it to the
                // widget via the property BatchSync (on open) and a per-property push (in-session switch).
                // Driving the combos from `profile.*` here used to clobber the just-synced helper value
                // with the widget's stale stored value (typically 0) on every Game Bar reopen — that's
                // exactly why the P/E freq dropdowns "snapped back to unlimited" the instant the widget
                // opened, even though the helper still held the real cap. Same fix as the Intel Display
                // path above: UI follows the helper. We only refresh the toggle's enable state here.
                UpdateCpuBoostToggleEnabled();
            }
            catch (Exception ex)
            {
                Logger.Debug($"ApplyCpuAdvancedFromProfile: {ex.Message}");
            }
        }

        private void CpuSectionExpandButton_Click(object sender, RoutedEventArgs e)
        {
            if (CpuSectionContent == null || CpuSectionExpandIcon == null) return;
            bool expanded = CpuSectionExpandButton?.IsChecked == true;
            CpuSectionContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            // Chevron: down (E70D) collapsed, up (E70E) expanded
            CpuSectionExpandIcon.Glyph = expanded ? "" : "";
            if (expanded)
            {
                try { SchedulingPolicyComboBox?.Focus(FocusState.Keyboard); } catch { }
            }
        }

        /// <summary>
        /// D-Pad navigation for the "More settings" expander that sits directly below the CPU Boost
        /// toggle. Up returns to the toggle; Down drops into the first advanced combo when the section
        /// is open, otherwise loops back to the top of the Performance tab.
        /// </summary>
        private void CpuSectionExpandButton_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadLeftThumbstickUp:
                case VirtualKey.Up:
                    try { CPUBoostToggle?.Focus(FocusState.Keyboard); } catch { }
                    e.Handled = true;
                    break;
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadLeftThumbstickDown:
                case VirtualKey.Down:
                    bool expanded = CpuSectionExpandButton?.IsChecked == true
                                    && CpuSectionContent?.Visibility == Visibility.Visible;
                    Windows.UI.Xaml.Controls.Control target = expanded
                        ? (Windows.UI.Xaml.Controls.Control)SchedulingPolicyComboBox
                        : (PerGameProfileToggle ?? (Windows.UI.Xaml.Controls.Control)FPSStateCycleButton);
                    try { target?.Focus(FocusState.Keyboard); } catch { }
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// Controller/keyboard navigation between the CPU advanced combos. Up/Down move between
        /// the four combos; Left or B returns focus to the expand toggle so the user can leave
        /// the section. ComboBoxes consume A/Enter themselves to open the drop-down.
        /// </summary>
        private void CpuAdvancedCombo_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null) return;

            // Let an open ComboBox handle its own up/down.
            if (combo.IsDropDownOpen) return;

            switch (e.Key)
            {
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadLeftThumbstickDown:
                case VirtualKey.Down:
                    // Move to the next combo; from the last one, leave the CPU section downward to the
                    // next card (Saved Profiles header) so the rest of the tab stays reachable.
                    if (!MoveCpuComboFocus(combo, +1))
                    {
                        try { PerfSavedProfilesExpandButton?.Focus(FocusState.Keyboard); } catch { }
                    }
                    e.Handled = true;
                    break;
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadLeftThumbstickUp:
                case VirtualKey.Up:
                    // Move to the previous combo; from the first one, leave the section upward to the
                    // CPU card header (the visible collapse chevron).
                    if (!MoveCpuComboFocus(combo, -1))
                    {
                        try { CpuCardExpandButton?.Focus(FocusState.Keyboard); } catch { }
                    }
                    e.Handled = true;
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.Left:
                    try { OSPowerModeComboBox?.Focus(FocusState.Keyboard); e.Handled = true; } catch { }
                    break;
            }
        }

        private bool MoveCpuComboFocus(ComboBox current, int dir)
        {
            var order = new ComboBox[] { SchedulingPolicyComboBox, MaxPCoreFreqComboBox, MaxECoreFreqComboBox };
            int idx = Array.IndexOf(order, current);
            if (idx < 0) return false;
            int next = idx + dir;
            while (next >= 0 && next < order.Length)
            {
                var target = order[next];
                if (target != null && target.IsEnabled && target.Visibility == Visibility.Visible)
                {
                    try { target.Focus(FocusState.Keyboard); return true; } catch { return false; }
                }
                next += dir;
            }
            return false; // let the event bubble (e.g. scroll / leave section)
        }

        // ── Performance-tab card-header navigation (below the Overlay card) ──────────────
        // The lower cards (CPU, Saved Profiles, Profile Settings) were moved into the Performance
        // tab; their collapse chevrons are the only focusable headers, so the D-Pad spine is wired
        // here. The Global performance & display profile card is display-only (no focusable control)
        // and is intentionally skipped. Works in both collapsed and expanded states.

        /// <summary>CPU card header. Up → Overlay combo (card above). Down → the CPU content (Scheduling
        /// Policy combo) when the section is expanded, otherwise the next card (Saved Profiles header).</summary>
        private void CpuCardExpandButton_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadLeftThumbstickUp:
                case VirtualKey.Up:
                    try { PerformanceOverlayComboBox?.Focus(FocusState.Keyboard); } catch { }
                    e.Handled = true;
                    break;
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadLeftThumbstickDown:
                case VirtualKey.Down:
                    bool expanded = CpuSectionContent?.Visibility == Visibility.Visible;
                    Control target = (expanded && SchedulingPolicyComboBox?.IsEnabled == true)
                        ? (Control)SchedulingPolicyComboBox
                        : PerfSavedProfilesExpandButton;
                    try { target?.Focus(FocusState.Keyboard); } catch { }
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Saved Profiles card header. Up → CPU content (last combo) when CPU is expanded, else
        /// the CPU card header. Down → Profile Settings header.</summary>
        private void PerfSavedProfilesExpandButton_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadLeftThumbstickUp:
                case VirtualKey.Up:
                    bool cpuExpanded = CpuSectionContent?.Visibility == Visibility.Visible;
                    Control up = cpuExpanded ? (LastEnabledCpuCombo() ?? (Control)CpuCardExpandButton) : CpuCardExpandButton;
                    try { up?.Focus(FocusState.Keyboard); } catch { }
                    e.Handled = true;
                    break;
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadLeftThumbstickDown:
                case VirtualKey.Down:
                    try { ProfileSettingsExpandToggle?.Focus(FocusState.Keyboard); } catch { }
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Profile Settings card header (last card on the tab). Up → Saved Profiles header.</summary>
        private void ProfileSettingsExpandToggle_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.GamepadDPadUp || e.Key == VirtualKey.GamepadLeftThumbstickUp || e.Key == VirtualKey.Up)
            {
                try { PerfSavedProfilesExpandButton?.Focus(FocusState.Keyboard); } catch { }
                e.Handled = true;
            }
        }

        /// <summary>The last enabled+visible CPU advanced combo (bottom of the CPU section), used as the
        /// up-target from the Saved Profiles header when the CPU section is expanded.</summary>
        private Control LastEnabledCpuCombo()
        {
            var order = new ComboBox[] { MaxECoreFreqComboBox, MaxPCoreFreqComboBox, SchedulingPolicyComboBox };
            foreach (var c in order)
                if (c != null && c.IsEnabled && c.Visibility == Visibility.Visible) return c;
            return null;
        }
    }
}
