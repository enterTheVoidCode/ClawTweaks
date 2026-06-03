using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using XboxGamingBar.Data;

namespace XboxGamingBar
{
    /// <summary>
    /// Display tab (Intel IGCL) — the full TnC "Color Remaster" set: Saturation, Hue, Contrast,
    /// Brightness, Gamma + Adaptive Sharpness, as sliders. Stored in the existing per-game / global
    /// performance profile (renamed "Performance &amp; Display"), so they follow the running game.
    /// Tab only shown on MSI Claw (Intel). Units match TnC: hue -180..180 (0); sat/contrast/bright
    /// 0..100 (50); gamma ×100 30..280 (100=1.0); sharpness 0..100 (0).
    /// </summary>
    public sealed partial class GamingWidget
    {
        private WidgetSliderProperty intelSaturation;
        private WidgetSliderProperty intelHue;
        private WidgetSliderProperty intelContrast;
        private WidgetSliderProperty intelBrightness;
        private WidgetSliderProperty intelGamma;       // ×100
        private WidgetSliderProperty intelSharpness;

        // Reference-image carousel (gallery logic ported from TnC ColorRemasterMainPage):
        // an array of packaged images + an index; tapping advances and wraps. Currently one
        // image; add more URIs here (and Content-include the assets) to grow the gallery.
        private readonly string[] _displayRefImages = new[]
        {
            "ms-appx:///Assets/ColorReference.jpg",
        };
        private int _displayRefIndex = 0;

        private void InitializeDisplayTab()
        {
            try
            {
                if (DisplayNavItem != null)
                    DisplayNavItem.Visibility = IsMsiClawDevice() ? Visibility.Visible : Visibility.Collapsed;

                UpdateDisplayReferenceImage();
            }
            catch (Exception ex)
            {
                Logger.Debug($"InitializeDisplayTab: {ex.Message}");
            }
        }

        private void UpdateDisplayReferenceImage()
        {
            if (DisplayReferenceImage == null || _displayRefImages.Length == 0) return;
            try
            {
                if (_displayRefIndex < 0 || _displayRefIndex >= _displayRefImages.Length) _displayRefIndex = 0;
                DisplayReferenceImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri(_displayRefImages[_displayRefIndex]));
            }
            catch (Exception ex)
            {
                Logger.Debug($"UpdateDisplayReferenceImage: {ex.Message}");
            }
        }

        private void DisplayReferenceImage_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (_displayRefImages.Length <= 1) return; // single image — nothing to cycle
            _displayRefIndex = (_displayRefIndex + 1) % _displayRefImages.Length;
            UpdateDisplayReferenceImage();
        }

        /// <summary>Updates the value label next to each slider (gamma shown as x.xx).</summary>
        private void DisplaySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            try
            {
                if (sender == DisplaySaturationSlider && DisplaySaturationValueText != null)
                    DisplaySaturationValueText.Text = ((int)e.NewValue).ToString();
                else if (sender == DisplayHueSlider && DisplayHueValueText != null)
                    DisplayHueValueText.Text = ((int)e.NewValue).ToString();
                else if (sender == DisplayContrastSlider && DisplayContrastValueText != null)
                    DisplayContrastValueText.Text = ((int)e.NewValue).ToString();
                else if (sender == DisplayBrightnessSlider && DisplayBrightnessValueText != null)
                    DisplayBrightnessValueText.Text = ((int)e.NewValue).ToString();
                else if (sender == DisplayGammaSlider && DisplayGammaValueText != null)
                    DisplayGammaValueText.Text = (e.NewValue / 100.0).ToString("0.00");
                else if (sender == DisplaySharpnessSlider && DisplaySharpnessValueText != null)
                    DisplaySharpnessValueText.Text = ((int)e.NewValue) <= 0 ? "Off" : ((int)e.NewValue).ToString();
            }
            catch (Exception ex)
            {
                Logger.Debug($"DisplaySlider_ValueChanged: {ex.Message}");
            }
        }

        private void DisplayResetButton_Click(object sender, RoutedEventArgs e)
        {
            // Neutral defaults — sliders' own ValueChanged sends them to the helper.
            if (DisplaySaturationSlider != null) DisplaySaturationSlider.Value = 50;
            if (DisplayHueSlider != null) DisplayHueSlider.Value = 0;
            if (DisplayContrastSlider != null) DisplayContrastSlider.Value = 50;
            if (DisplayBrightnessSlider != null) DisplayBrightnessSlider.Value = 50;
            if (DisplayGammaSlider != null) DisplayGammaSlider.Value = 100;
            if (DisplaySharpnessSlider != null) DisplaySharpnessSlider.Value = 0;
        }

        /// <summary>Restore the Display sliders from a profile and push to the helper.</summary>
        private void ApplyDisplayFromProfile(PerformanceProfile profile)
        {
            try
            {
                SetDisplaySlider(intelSaturation, DisplaySaturationSlider, profile.IntelColorSaturation);
                SetDisplaySlider(intelHue,        DisplayHueSlider,        profile.IntelColorHue);
                SetDisplaySlider(intelContrast,   DisplayContrastSlider,   profile.IntelDisplayContrast);
                SetDisplaySlider(intelBrightness, DisplayBrightnessSlider, profile.IntelDisplayBrightness);
                SetDisplaySlider(intelGamma,      DisplayGammaSlider,      profile.IntelDisplayGammaX100);
                SetDisplaySlider(intelSharpness,  DisplaySharpnessSlider,  profile.IntelAdaptiveSharpness);
            }
            catch (Exception ex)
            {
                Logger.Debug($"ApplyDisplayFromProfile: {ex.Message}");
            }
        }

        private void SetDisplaySlider(WidgetSliderProperty prop, Slider slider, int value)
        {
            if (slider == null) return;
            // Move the UI without triggering a debounced send, then push explicitly (unless the
            // switch was helper-driven — then the helper already has the value).
            if (prop != null) prop.IsUpdatingUI = true;
            try { slider.Value = value; } finally { if (prop != null) prop.IsUpdatingUI = false; }
            if (!isApplyingHelperUpdate) prop?.SetValue(value);
        }

        /// <summary>Compact one-line summary for the profile cards (null when all neutral).</summary>
        private string BuildDisplaySummary(PerformanceProfile p)
        {
            if (p == null) return null;
            var parts = new System.Collections.Generic.List<string>();
            if (p.IntelColorSaturation != 50) parts.Add($"Sat {p.IntelColorSaturation}");
            if (p.IntelColorHue != 0) parts.Add($"Hue {p.IntelColorHue}");
            if (p.IntelDisplayContrast != 50) parts.Add($"Con {p.IntelDisplayContrast}");
            if (p.IntelDisplayBrightness != 50) parts.Add($"Bri {p.IntelDisplayBrightness}");
            if (p.IntelDisplayGammaX100 != 100) parts.Add($"Gam {(p.IntelDisplayGammaX100 / 100.0):0.00}");
            if (p.IntelAdaptiveSharpness > 0) parts.Add($"Sharp {p.IntelAdaptiveSharpness}");
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }
}
