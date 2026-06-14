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

        /// <summary>
        /// When the CPU Boost toggle flips, drive the explicit boost mode so the dropdown
        /// selection wins over the legacy on/off write: ON => send selected mode, OFF => send 0.
        /// </summary>
        private void CpuBoostToggle_AdvancedToggled(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateCpuBoostModeEnabled();

                // Don't echo a mode write while we're applying a helper-driven update
                // (e.g. profile sync flipping the toggle programmatically).
                if (isApplyingHelperUpdate) return;
                if (cpuBoostMode == null) return;

                if (CPUBoostToggle != null && CPUBoostToggle.IsOn)
                {
                    int mode = GetSelectedTagInt(CpuBoostModeComboBox, 1);
                    cpuBoostMode.SetValue(mode);
                }
                else
                {
                    cpuBoostMode.SetValue(0);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"CpuBoostToggle_AdvancedToggled: {ex.Message}");
            }
        }

        private void UpdateCpuBoostModeEnabled()
        {
            bool on = CPUBoostToggle != null && CPUBoostToggle.IsOn && CPUBoostToggle.IsEnabled;
            if (CpuBoostModeComboBox != null)
            {
                CpuBoostModeComboBox.IsEnabled = on;
                CpuBoostModeComboBox.Opacity = on ? 1.0 : 0.5;
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
        /// Restores the CPU advanced combo selections from a profile and pushes them to the
        /// helper. When the switch was helper-driven (isApplyingHelperUpdate) we only sync the
        /// UI and let the helper's own BatchSync carry the values, mirroring the CPU Boost path.
        /// </summary>
        private void ApplyCpuAdvancedFromProfile(PerformanceProfile profile)
        {
            try
            {
                if (isApplyingHelperUpdate)
                {
                    // boost mode 0 = off; keep the combo on a valid 1-6 entry for when it re-enables
                    if (profile.CpuBoostMode > 0) SelectComboByTag(CpuBoostModeComboBox, profile.CpuBoostMode);
                    if (profile.ProcessorSchedulingPolicy >= 0) SelectComboByTag(SchedulingPolicyComboBox, profile.ProcessorSchedulingPolicy);
                    SelectComboByTag(MaxPCoreFreqComboBox, profile.MaxPCoreFreqMHz);
                    SelectComboByTag(MaxECoreFreqComboBox, profile.MaxECoreFreqMHz);
                }
                else
                {
                    if (profile.CpuBoostMode > 0) SelectComboByTag(CpuBoostModeComboBox, profile.CpuBoostMode);
                    cpuBoostMode?.SetValue(profile.CpuBoostMode);
                    if (profile.ProcessorSchedulingPolicy >= 0)
                    {
                        SelectComboByTag(SchedulingPolicyComboBox, profile.ProcessorSchedulingPolicy);
                        schedulingPolicy?.SetValue(profile.ProcessorSchedulingPolicy);
                    }
                    SelectComboByTag(MaxPCoreFreqComboBox, profile.MaxPCoreFreqMHz);
                    maxPCoreFreq?.SetValue(profile.MaxPCoreFreqMHz);
                    SelectComboByTag(MaxECoreFreqComboBox, profile.MaxECoreFreqMHz);
                    maxECoreFreq?.SetValue(profile.MaxECoreFreqMHz);
                }
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
                    if (MoveCpuComboFocus(combo, +1)) e.Handled = true;
                    break;
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadLeftThumbstickUp:
                case VirtualKey.Up:
                    if (MoveCpuComboFocus(combo, -1)) e.Handled = true;
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.Left:
                    try { CpuSectionExpandButton?.Focus(FocusState.Keyboard); e.Handled = true; } catch { }
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
    }
}
