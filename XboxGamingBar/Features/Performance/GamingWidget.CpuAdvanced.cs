using System;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using XboxGamingBar.Data;

namespace XboxGamingBar
{
    /// <summary>
    /// CPU advanced section (ToothNClaw port): collapsible "CPU" card in the Performance tab
    /// holding Boost Mode, Processor Scheduling Policy and P/E core max frequency. The on/off
    /// CPU Boost toggle stays the quick switch; when ON the Boost Mode combo is the authority
    /// for the exact mode applied. All values persist per-game and globally via GameProfile.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private CpuIntComboProperty cpuBoostMode;
        private CpuIntComboProperty schedulingPolicy;
        private CpuIntComboProperty maxPCoreFreq;
        private CpuIntComboProperty maxECoreFreq;

        private void InitializeCpuAdvanced()
        {
            try
            {
                if (CPUBoostToggle != null)
                {
                    CPUBoostToggle.Toggled += CpuBoostToggle_AdvancedToggled;
                }
                UpdateCpuBoostModeEnabled();
            }
            catch (Exception ex)
            {
                Logger.Debug($"InitializeCpuAdvanced: {ex.Message}");
            }
        }

        // The Boost on/off ToggleSwitch (CPUBoostToggle) is now HIDDEN; the visible control is the
        // Boost Mode dropdown (with an "Off" entry). The two are kept in sync here so the hidden
        // toggle still carries the on/off state for the Quick-Settings boost tile and the performance
        // profile cards. Invariant: cpuBoost (toggle on) == (CpuBoostMode > 0).
        private int lastBoostMode = 1;        // last non-off mode, to restore when the tile flips boost back on
        private bool _syncingBoostUi;          // re-entrancy guard for the dropdown <-> toggle cross-sync

        /// <summary>
        /// The hidden Boost toggle flipped (Quick-Settings tile, or CPUState forcing it off when the
        /// Max CPU State drops below 100 %). Reflect on/off into the visible Boost Mode dropdown so the
        /// UI and the pushed CpuBoostMode follow. Skips helper-driven flips (the dropdown is synced from
        /// its own property then) and our own dropdown→toggle sync (guarded).
        /// </summary>
        private void CpuBoostToggle_AdvancedToggled(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateCpuBoostModeEnabled();

                if (_syncingBoostUi) return;          // came from the dropdown; it's already authoritative
                if (isApplyingHelperUpdate) return;   // helper/profile drives both controls directly
                if (CPUBoostToggle == null) return;

                int mode = CPUBoostToggle.IsOn ? (lastBoostMode > 0 ? lastBoostMode : 1) : 0;
                _syncingBoostUi = true;
                try { SelectComboByTag(CpuBoostModeComboBox, mode); }
                finally { _syncingBoostUi = false; }
                cpuBoostMode?.SetValue(mode);
            }
            catch (Exception ex)
            {
                Logger.Debug($"CpuBoostToggle_AdvancedToggled: {ex.Message}");
            }
        }

        /// <summary>
        /// The visible Boost Mode dropdown changed by genuine user action. Keep the hidden on/off
        /// carrier (CPUBoostToggle → cpuBoost → tile + profiles) in sync: "Off" (0) = boost off, any
        /// 1-6 mode = boost on. The mode value itself is pushed to the helper by the CpuIntComboProperty
        /// bound to this combo. Helper/profile-driven changes are skipped (isApplyingHelperUpdate).
        /// </summary>
        private void CpuBoostModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                int mode = GetSelectedTagInt(CpuBoostModeComboBox, 1);
                if (mode > 0) lastBoostMode = mode;   // remember across any source (incl. helper sync)

                if (_syncingBoostUi) return;
                if (isApplyingHelperUpdate) return;

                bool shouldBeOn = mode > 0;
                if (CPUBoostToggle != null && CPUBoostToggle.IsOn != shouldBeOn)
                {
                    _syncingBoostUi = true;
                    try { CPUBoostToggle.IsOn = shouldBeOn; }  // drives cpuBoost (tile/profile state)
                    finally { _syncingBoostUi = false; }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"CpuBoostModeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// The Boost Mode dropdown is the single boost control now (it includes "Off"), so it is enabled
        /// whenever boost is AVAILABLE (Max CPU State ≥ 100 %), not only when boost is on. Windows can't
        /// boost above a sub-100 % max state, so we grey it out then — mirroring UpdateCPUBoostEnabledState.
        /// </summary>
        private void UpdateCpuBoostModeEnabled()
        {
            bool canBoost = MaxCPUStateComboBox == null || GetSelectedCPUStateValue(MaxCPUStateComboBox) >= 100;
            if (CpuBoostModeComboBox != null)
            {
                CpuBoostModeComboBox.IsEnabled = canBoost;
                CpuBoostModeComboBox.Opacity = canBoost ? 1.0 : 0.5;
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

            string mode = GetCpuBoostModeName(p.CpuBoostMode);
            if (p.CpuBoostMode > 0 && mode != null) parts.Add(mode);

            string sched = GetSchedulingPolicyName(p.ProcessorSchedulingPolicy);
            if (p.ProcessorSchedulingPolicy >= 0 && sched != null) parts.Add(sched);

            if (p.MaxPCoreFreqMHz > 0) parts.Add($"P{p.MaxPCoreFreqMHz}");
            if (p.MaxECoreFreqMHz > 0) parts.Add($"E{p.MaxECoreFreqMHz}");

            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        private static void SelectComboByTag(ComboBox combo, int value)
        {
            if (combo == null) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Tag is string tag
                    && int.TryParse(tag, out int v) && v == value)
                {
                    if (combo.SelectedIndex != i) combo.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>
        /// Syncs the CPU advanced combo selections (Boost Mode, Scheduling Policy, P/E max freq)
        /// from a profile — UI ONLY. The HELPER owns CPU advanced state: it applies the active
        /// profile's values on every switch (Program.ProfileHandlers Apply... paths) AND re-enforces
        /// them every 3 s against Windows scheme resets, then pushes the live values back via the
        /// property BatchSync (on open) / per-property push (in-session switch). The widget must NOT
        /// push its own (possibly stale / default-0) stored values here, or it overrides the user's
        /// last P/E freq + boost mode on every Game Bar reopen — the cap "flew out" to unlimited
        /// because this path pushed MaxPCoreFreqMHz=0 over the helper's enforced value. Same fix as
        /// the Intel Display path above. UI follows the helper.
        /// </summary>
        private void ApplyCpuAdvancedFromProfile(PerformanceProfile profile)
        {
            try
            {
                // INTENTIONALLY does NOT set the four advanced combos from the widget's profile copy.
                // The HELPER is the source of truth for Boost Mode / Scheduling Policy / P-/E-core max
                // freq: it holds the live value in memory, re-applies it every 3 s, and pushes it to the
                // widget via the property BatchSync (on open) and a per-property push (in-session switch).
                // Driving the combos from `profile.*` here used to clobber the just-synced helper value
                // with the widget's stale stored value (typically 0) on every Game Bar reopen — that's
                // exactly why the P/E freq dropdowns "snapped back to unlimited" the instant the widget
                // opened, even though the helper still held the real cap. Same fix as the Intel Display
                // path above: UI follows the helper. We only refresh the enable state here.
                UpdateCpuBoostModeEnabled();
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
                try { CpuBoostModeComboBox?.Focus(FocusState.Keyboard); } catch { }
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
                        ? (Windows.UI.Xaml.Controls.Control)CpuBoostModeComboBox
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
            var order = new ComboBox[] { CpuBoostModeComboBox, SchedulingPolicyComboBox, MaxPCoreFreqComboBox, MaxECoreFreqComboBox };
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

        /// <summary>CPU card header. Up → Overlay combo (card above). Down → the CPU content (Boost Mode
        /// combo) when the section is expanded, otherwise the next card (Saved Profiles header).</summary>
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
                    Control target = (expanded && CpuBoostModeComboBox?.IsEnabled == true)
                        ? (Control)CpuBoostModeComboBox
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
            var order = new ComboBox[] { MaxECoreFreqComboBox, MaxPCoreFreqComboBox, SchedulingPolicyComboBox, CpuBoostModeComboBox };
            foreach (var c in order)
                if (c != null && c.IsEnabled && c.Visibility == Visibility.Visible) return c;
            return null;
        }
    }
}
