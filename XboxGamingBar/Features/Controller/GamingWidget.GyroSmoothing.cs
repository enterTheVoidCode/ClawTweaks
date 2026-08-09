using System;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    /// <summary>
    /// Per-engine-mode gyro "Smoothing" slider (One-Euro min-cutoff amount, 0-100, higher = smoother).
    /// The Claw gyro card shows ONE slider but stores a separate value per engine mode (Adaptive/MA);
    /// Direct/HC has no software low-pass so the slider is disabled there. Values live in LocalSettings
    /// (a feel preference, global — not per game), and the active-mode value flows to the helper through
    /// the legionGyroSmoothing property → ClawButtonMonitor.SetGyroSmoothing / ActiveMinCutoff.
    /// </summary>
    public sealed partial class GamingWidget
    {
        // Engine-mode indices (match LegionGyroMappingTypeComboBox / _gyroEngineMode):
        //   0 = Adaptive, 1 = Linear, 2 = Precision (MA inspired, default)
        private const int GyroModeAdaptive = 0;
        private const int GyroModeDirect   = 1;   // "Linear" in the UI
        private const int GyroModeMa       = 2;   // "Precision (MA inspired)" in the UI

        // Defaults chosen so the helper's geometric smoothing→min-cutoff map lands on the known-good
        // cutoffs: Adaptive 28 → ~6.0 Hz (historic CTW default), MA 73 → ~0.07 Hz (MA default).
        private const int GyroSmoothingDefaultAdaptive = 28;
        private const int GyroSmoothingDefaultMa       = 73;

        private static string GyroSmoothingKey(int mode) => $"LegionGyroSmoothing_Mode{mode}";

        private static int GyroSmoothingDefault(int mode)
            => mode == GyroModeMa ? GyroSmoothingDefaultMa : GyroSmoothingDefaultAdaptive;

        private int GetStoredGyroSmoothing(int mode)
        {
            try
            {
                var v = ApplicationData.Current.LocalSettings.Values;
                if (v.TryGetValue(GyroSmoothingKey(mode), out var o) && o is int i)
                    return Math.Max(0, Math.Min(100, i));
            }
            catch { }
            return GyroSmoothingDefault(mode);
        }

        private void SetStoredGyroSmoothing(int mode, int val)
        {
            try { ApplicationData.Current.LocalSettings.Values[GyroSmoothingKey(mode)] = Math.Max(0, Math.Min(100, val)); }
            catch { }
        }

        /// <summary>Current engine mode from the dropdown (defaults to Adaptive).</summary>
        private int CurrentGyroEngineMode()
            => LegionGyroMappingTypeComboBox?.SelectedIndex is int i && i >= 0 ? i : GyroModeAdaptive;

        /// <summary>Load the stored smoothing for a mode into the slider + label and gate the control
        /// (disabled for Direct/HC, which does no filtering). Setting the slider value makes the
        /// legionGyroSmoothing property push it to the helper through its normal guarded path.</summary>
        private void LoadGyroSmoothingForMode(int mode)
        {
            if (LegionGyroSmoothingSlider == null) return;

            bool filtered = mode != GyroModeDirect;
            int val = GetStoredGyroSmoothing(mode);

            LegionGyroSmoothingSlider.Value = val;
            if (LegionGyroSmoothingValue != null)
                LegionGyroSmoothingValue.Text = filtered ? val.ToString() : "—";
            LegionGyroSmoothingSlider.IsEnabled = filtered;
            if (LegionGyroSmoothingPanel != null)
                LegionGyroSmoothingPanel.Opacity = filtered ? 1.0 : 0.4;
        }

        /// <summary>Engine-mode dropdown changed → swap the smoothing slider to that mode's value.
        /// (The legionGyroMappingType property already sends the engine mode itself.)</summary>
        private void LegionGyroMappingTypeComboBox_EngineModeChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadGyroSmoothingForMode(CurrentGyroEngineMode());
        }

        /// <summary>Smoothing slider moved → persist under the current mode + update the label.
        /// (The legionGyroSmoothing property pushes the value to the helper.)</summary>
        private void LegionGyroSmoothingSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            int mode = CurrentGyroEngineMode();
            if (mode == GyroModeDirect) return; // disabled — no persistence for HC
            int val = (int)Math.Round(LegionGyroSmoothingSlider?.Value ?? 0);
            SetStoredGyroSmoothing(mode, val);
            if (LegionGyroSmoothingValue != null)
                LegionGyroSmoothingValue.Text = val.ToString();
        }
    }
}
