using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using XboxGamingBar.Data;

namespace XboxGamingBar
{
    /// <summary>
    /// Display tab (Intel IGCL): colour saturation + adaptive sharpness. These are NOT a separate
    /// profile — they are stored in the existing per-game / global performance profile (renamed
    /// "Performance & Display"), so they follow the running game exactly like TDP/CPU do. The
    /// Display tab itself is only shown on MSI Claw (Intel) devices.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private CpuIntComboProperty intelSaturation;   // percent, 100 = neutral
        private CpuIntComboProperty intelSharpness;    // 0 = off, 1..100 intensity

        private void InitializeDisplayTab()
        {
            try
            {
                // Only surface the Display tab on MSI Claw (Intel) — the IGCL features are
                // Intel-only; on other handhelds the helper no-ops anyway.
                if (DisplayNavItem != null)
                    DisplayNavItem.Visibility = IsMsiClawDevice() ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Debug($"InitializeDisplayTab: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore the Display combo selections from a profile and push them to the helper.
        /// Mirrors ApplyCpuAdvancedFromProfile: UI-only sync while a helper-driven switch is in
        /// flight, otherwise update UI + send.
        /// </summary>
        private void ApplyDisplayFromProfile(PerformanceProfile profile)
        {
            try
            {
                // Saturation: -1 = unset → leave UI at neutral, don't push.
                if (profile.IntelColorSaturation >= 0)
                {
                    SelectComboByTag(DisplaySaturationComboBox, profile.IntelColorSaturation);
                    if (!isApplyingHelperUpdate) intelSaturation?.SetValue(profile.IntelColorSaturation);
                }
                if (profile.IntelAdaptiveSharpness >= 0)
                {
                    SelectComboByTag(DisplaySharpnessComboBox, profile.IntelAdaptiveSharpness);
                    if (!isApplyingHelperUpdate) intelSharpness?.SetValue(profile.IntelAdaptiveSharpness);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"ApplyDisplayFromProfile: {ex.Message}");
            }
        }

        /// <summary>Compact one-line summary for the profile cards (null when nothing set).</summary>
        private string BuildDisplaySummary(PerformanceProfile p)
        {
            if (p == null) return null;
            var parts = new System.Collections.Generic.List<string>();
            if (p.IntelColorSaturation >= 0 && p.IntelColorSaturation != 100)
                parts.Add($"Sat {p.IntelColorSaturation}%");
            if (p.IntelAdaptiveSharpness > 0)
                parts.Add($"Sharp {p.IntelAdaptiveSharpness}");
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }
}
