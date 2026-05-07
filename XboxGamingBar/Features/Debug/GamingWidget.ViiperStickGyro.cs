using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    /// <summary>
    /// Wires the Gyro → Right Stick mini-section in the VIIPER panel.
    ///
    /// Strategy: each VIIPER control mirrors the legacy ControllerEmulation
    /// equivalent. When the user changes a VIIPER control we copy the value to
    /// the legacy control, whose existing property class fires the SetValue
    /// path that pushes the value over the pipe. When the legacy control
    /// changes (helper sync, or user editing legacy panel before swapping
    /// backends), we mirror back to the VIIPER control. <see cref="isMirroringStickGyro"/>
    /// guards the recursion.
    ///
    /// This avoids creating a second WidgetProperty bound to the same
    /// <c>Function.ControllerEmulation*</c> enum (which would collide in
    /// <c>WidgetProperties</c>'s Function-keyed dictionary).
    /// </summary>
    public sealed partial class GamingWidget
    {
        private bool isMirroringStickGyro;
        private bool stickGyroMirrorWired;

        private void WireViiperStickGyroMirror()
        {
            if (stickGyroMirrorWired) return;
            stickGyroMirrorWired = true;

            // Legacy → VIIPER (helper sync arrives via legacy property class first).
            // Each lambda mirrors the legacy control's new value into the VIIPER twin.
            HookLegacyToViiperCombo(ControllerEmulationGyroActivationModeComboBox, ViiperStickGyroActivationModeComboBox);
            HookLegacyToViiperCombo(ControllerEmulationGyroActivationButtonComboBox, ViiperStickGyroActivationButtonComboBox);
            HookLegacyToViiperCombo(ControllerEmulationStickSelectComboBox, ViiperStickGyroSelectComboBox);
            HookLegacyToViiperCombo(StickConversionComboBox, ViiperStickGyroConversionComboBox);
            HookLegacyToViiperCombo(StickOrientationV2ComboBox, ViiperStickGyroOrientationComboBox);

            HookLegacyToViiperToggle(ControllerEmulationStickInvertXToggle, ViiperStickGyroInvertXToggle);
            HookLegacyToViiperToggle(ControllerEmulationStickInvertYToggle, ViiperStickGyroInvertYToggle);

            HookLegacyToViiperSlider(StickSensitivityV2Slider, ViiperStickGyroSensitivitySlider, ViiperStickGyroSensitivityValueText, FormatSensitivity);
            HookLegacyToViiperSlider(StickDeadzoneSlider, ViiperStickGyroDeadzoneSlider, ViiperStickGyroDeadzoneValueText, FormatDegPerSec);
            HookLegacyToViiperSlider(StickMinGyroSpeedSlider, ViiperStickGyroMinGyroSpeedSlider, ViiperStickGyroMinGyroSpeedValueText, FormatDegPerSec);
            HookLegacyToViiperSlider(StickMaxGyroSpeedSlider, ViiperStickGyroMaxGyroSpeedSlider, ViiperStickGyroMaxGyroSpeedValueText, FormatDegPerSec);
            HookLegacyToViiperSlider(StickMinOutputSlider, ViiperStickGyroMinOutputSlider, ViiperStickGyroMinOutputValueText, FormatPercent);
            HookLegacyToViiperSlider(StickMaxOutputSlider, ViiperStickGyroMaxOutputSlider, ViiperStickGyroMaxOutputValueText, FormatPercent);
            HookLegacyToViiperSlider(StickPowerCurveSlider, ViiperStickGyroPowerCurveSlider, ViiperStickGyroPowerCurveValueText, FormatPowerCurve);
            HookLegacyToViiperSlider(StickPrecisionSpeedSlider, ViiperStickGyroPrecisionSpeedSlider, ViiperStickGyroPrecisionSpeedValueText, FormatPrecisionSpeed);
            HookLegacyToViiperSlider(StickOutputMixSlider, ViiperStickGyroOutputMixSlider, ViiperStickGyroOutputMixValueText, FormatOutputMix);

            // Initial sync from legacy (which already holds the synced helper value).
            MirrorComboBoxIndex(ControllerEmulationGyroActivationModeComboBox, ViiperStickGyroActivationModeComboBox);
            MirrorComboBoxIndex(ControllerEmulationGyroActivationButtonComboBox, ViiperStickGyroActivationButtonComboBox);
            MirrorComboBoxIndex(ControllerEmulationStickSelectComboBox, ViiperStickGyroSelectComboBox);
            MirrorComboBoxIndex(StickConversionComboBox, ViiperStickGyroConversionComboBox);
            MirrorComboBoxIndex(StickOrientationV2ComboBox, ViiperStickGyroOrientationComboBox);
            MirrorToggle(ControllerEmulationStickInvertXToggle, ViiperStickGyroInvertXToggle);
            MirrorToggle(ControllerEmulationStickInvertYToggle, ViiperStickGyroInvertYToggle);
            MirrorSliderValue(StickSensitivityV2Slider, ViiperStickGyroSensitivitySlider, ViiperStickGyroSensitivityValueText, FormatSensitivity);
            MirrorSliderValue(StickDeadzoneSlider, ViiperStickGyroDeadzoneSlider, ViiperStickGyroDeadzoneValueText, FormatDegPerSec);
            MirrorSliderValue(StickMinGyroSpeedSlider, ViiperStickGyroMinGyroSpeedSlider, ViiperStickGyroMinGyroSpeedValueText, FormatDegPerSec);
            MirrorSliderValue(StickMaxGyroSpeedSlider, ViiperStickGyroMaxGyroSpeedSlider, ViiperStickGyroMaxGyroSpeedValueText, FormatDegPerSec);
            MirrorSliderValue(StickMinOutputSlider, ViiperStickGyroMinOutputSlider, ViiperStickGyroMinOutputValueText, FormatPercent);
            MirrorSliderValue(StickMaxOutputSlider, ViiperStickGyroMaxOutputSlider, ViiperStickGyroMaxOutputValueText, FormatPercent);
            MirrorSliderValue(StickPowerCurveSlider, ViiperStickGyroPowerCurveSlider, ViiperStickGyroPowerCurveValueText, FormatPowerCurve);
            MirrorSliderValue(StickPrecisionSpeedSlider, ViiperStickGyroPrecisionSpeedSlider, ViiperStickGyroPrecisionSpeedValueText, FormatPrecisionSpeed);
            MirrorSliderValue(StickOutputMixSlider, ViiperStickGyroOutputMixSlider, ViiperStickGyroOutputMixValueText, FormatOutputMix);

            // Master-enable toggle drives the badge + cascading enable state. The
            // toggle itself is bound to a real WidgetProperty so its value pushes
            // through the pipe without help; this handler is purely for UI.
            if (ViiperStickGyroEnabledToggle != null)
            {
                ViiperStickGyroEnabledToggle.Toggled += (s, e) =>
                {
                    UpdateStickGyroSectionEnabledCascade();
                    UpdateStickGyroStatusBadge();
                };
            }

            // Initial computed-state pass so the header / badge / cascade reflect
            // whatever the helper sync just landed.
            UpdateStickGyroSectionHeader();
            UpdateActivationButtonEnabledState();
            UpdateStickGyroSectionEnabledCascade();
            UpdateStickGyroStatusBadge();
        }

        // ----- Computed UI state (header, badge, enabled cascade) -----

        /// <summary>
        /// Header reflects the current Send-to-joystick choice so the title
        /// doesn't lie when the user routes to the left stick. Driven from the
        /// VIIPER select ComboBox; the legacy → VIIPER mirror keeps it in sync
        /// after helper-side updates.
        /// </summary>
        private void UpdateStickGyroSectionHeader()
        {
            if (ViiperStickGyroSectionHeader == null || ViiperStickGyroSelectComboBox == null) return;
            int idx = ViiperStickGyroSelectComboBox.SelectedIndex;
            string side = idx == 0 ? "Left" : "Right";
            ViiperStickGyroSectionHeader.Text = $"Gyro → {side} Stick";
        }

        /// <summary>
        /// In Always-On mode (index 0) the activation button doesn't matter — gyro
        /// is on whenever the master enable is true. Mirror legacy CE's behavior at
        /// GpdTabCallbacks.cs:247-249 by disabling the button picker so the user
        /// isn't fooled into thinking it's wired up.
        /// </summary>
        private void UpdateActivationButtonEnabledState()
        {
            if (ViiperStickGyroActivationModeComboBox == null || ViiperStickGyroActivationButtonComboBox == null) return;
            bool buttonRelevant = ViiperStickGyroActivationModeComboBox.SelectedIndex != 0;
            ViiperStickGyroActivationButtonComboBox.IsEnabled = buttonRelevant;
        }

        /// <summary>
        /// When the master enable is off, grey out the activation/sensitivity/deadzone
        /// controls AND the advanced expander so the user can see at a glance that
        /// nothing below is doing anything. The toggle itself stays interactive so
        /// they can flip it back on.
        /// </summary>
        private void UpdateStickGyroSectionEnabledCascade()
        {
            bool on = ViiperStickGyroEnabledToggle?.IsOn ?? true;
            if (ViiperStickGyroActivationModeComboBox != null) ViiperStickGyroActivationModeComboBox.IsEnabled = on;
            // Activation Button respects BOTH master enable AND mode != Always-On.
            if (ViiperStickGyroActivationButtonComboBox != null)
                ViiperStickGyroActivationButtonComboBox.IsEnabled = on && (ViiperStickGyroActivationModeComboBox?.SelectedIndex != 0);
            if (ViiperStickGyroSensitivitySlider != null) ViiperStickGyroSensitivitySlider.IsEnabled = on;
            if (ViiperStickGyroDeadzoneSlider != null) ViiperStickGyroDeadzoneSlider.IsEnabled = on;
            if (ViiperStickGyroAdvancedToggle != null) ViiperStickGyroAdvancedToggle.IsEnabled = on;
            if (ViiperStickGyroResetButton != null) ViiperStickGyroResetButton.IsEnabled = on;
            // Collapse advanced when disabled so the user doesn't see live-but-inert sliders.
            if (!on && ViiperStickGyroAdvancedContent != null && ViiperStickGyroAdvancedToggle != null)
            {
                ViiperStickGyroAdvancedContent.Visibility = Visibility.Collapsed;
                ViiperStickGyroAdvancedToggle.IsChecked = false;
                if (ViiperStickGyroAdvancedIcon != null) ViiperStickGyroAdvancedIcon.Glyph = "";
            }
        }

        /// <summary>
        /// Concise badge summarizing what the user has configured: "OFF" when master
        /// enable is off, "ON: Always" when always-on, "ON: Hold &lt;button&gt;" /
        /// "ON: Toggle &lt;button&gt;" otherwise. Pure widget computation; no helper
        /// round-trip needed for what is effectively local UI state.
        /// </summary>
        private void UpdateStickGyroStatusBadge()
        {
            if (ViiperStickGyroStatusBadge == null || ViiperStickGyroStatusBadgeText == null) return;

            bool on = ViiperStickGyroEnabledToggle?.IsOn ?? true;
            if (!on)
            {
                ViiperStickGyroStatusBadgeText.Text = "OFF";
                ViiperStickGyroStatusBadgeText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA));
                ViiperStickGyroStatusBadge.BorderBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x55, 0x55, 0x55));
                return;
            }

            int modeIdx = ViiperStickGyroActivationModeComboBox?.SelectedIndex ?? 0;
            string modeText;
            switch (modeIdx)
            {
                case 0: modeText = "Always"; break;
                case 1: modeText = "Hold"; break;
                case 2: modeText = "Toggle"; break;
                default: modeText = "?"; break;
            }
            string buttonText = string.Empty;
            if (modeIdx != 0)
            {
                // Use the compact name (RT / Back / M3) instead of the verbose
                // ComboBoxItem.Content ("M3 (back R lower)"). Full names blow
                // out the badge and bury the actionable info — issue #79 round-3
                // testing showed vvalente set Back as activation, then tried
                // pressing RT in-game and reported "Hold doesn't work" because
                // the badge was lost in noise.
                int btnTag = ParseSelectedTagInt(ViiperStickGyroActivationButtonComboBox);
                buttonText = ShortNameForActivationButton(btnTag);
            }
            ViiperStickGyroStatusBadgeText.Text = string.IsNullOrEmpty(buttonText)
                ? $"ON: {modeText}"
                : $"ON: {modeText} {buttonText}";
            ViiperStickGyroStatusBadgeText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x66, 0xCC, 0x66));
            ViiperStickGyroStatusBadge.BorderBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x88, 0x4A));
        }

        private void HookLegacyToViiperCombo(ComboBox legacy, ComboBox viiper)
        {
            if (legacy == null || viiper == null) return;
            legacy.SelectionChanged += (s, e) => MirrorComboBoxIndex(legacy, viiper);
        }

        private void HookLegacyToViiperToggle(ToggleSwitch legacy, ToggleSwitch viiper)
        {
            if (legacy == null || viiper == null) return;
            legacy.Toggled += (s, e) => MirrorToggle(legacy, viiper);
        }

        private void HookLegacyToViiperSlider(Slider legacy, Slider viiper, TextBlock viiperValueText, Func<double, string> formatter)
        {
            if (legacy == null || viiper == null) return;
            legacy.ValueChanged += (s, e) => MirrorSliderValue(legacy, viiper, viiperValueText, formatter);
        }

        private void ViiperStickGyroActivationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActivationButtonEnabledState();
            UpdateStickGyroStatusBadge();
            if (isMirroringStickGyro) return;
            MirrorComboBoxIndex(ViiperStickGyroActivationModeComboBox, ControllerEmulationGyroActivationModeComboBox);
        }

        private void ViiperStickGyroActivationButtonComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStickGyroStatusBadge();
            if (isMirroringStickGyro) return;
            MirrorComboBoxIndex(ViiperStickGyroActivationButtonComboBox, ControllerEmulationGyroActivationButtonComboBox);
        }

        private void ViiperStickGyroSensitivitySlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroSensitivityValueText != null)
            {
                ViiperStickGyroSensitivityValueText.Text = FormatSensitivity(e.NewValue);
            }
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroSensitivitySlider, StickSensitivityV2Slider, null, null);
        }

        private void ViiperStickGyroDeadzoneSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroDeadzoneValueText != null)
            {
                ViiperStickGyroDeadzoneValueText.Text = FormatDegPerSec(e.NewValue);
            }
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroDeadzoneSlider, StickDeadzoneSlider, null, null);
        }

        // ----- Advanced expander -----

        private void ViiperStickGyroAdvancedToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ViiperStickGyroAdvancedContent == null || ViiperStickGyroAdvancedToggle == null) return;
            bool open = ViiperStickGyroAdvancedToggle.IsChecked == true;
            ViiperStickGyroAdvancedContent.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (ViiperStickGyroAdvancedIcon != null)
            {
                ViiperStickGyroAdvancedIcon.Glyph = open ? "\uE70E" : "\uE70D";
            }
        }

        private void ViiperStickGyroSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStickGyroSectionHeader();
            if (isMirroringStickGyro) return;
            MirrorComboBoxIndex(ViiperStickGyroSelectComboBox, ControllerEmulationStickSelectComboBox);
        }

        private void ViiperStickGyroConversionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isMirroringStickGyro) return;
            MirrorComboBoxIndex(ViiperStickGyroConversionComboBox, StickConversionComboBox);
        }

        private void ViiperStickGyroOrientationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isMirroringStickGyro) return;
            MirrorComboBoxIndex(ViiperStickGyroOrientationComboBox, StickOrientationV2ComboBox);
        }

        private void ViiperStickGyroInvertXToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isMirroringStickGyro) return;
            MirrorToggle(ViiperStickGyroInvertXToggle, ControllerEmulationStickInvertXToggle);
        }

        private void ViiperStickGyroInvertYToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isMirroringStickGyro) return;
            MirrorToggle(ViiperStickGyroInvertYToggle, ControllerEmulationStickInvertYToggle);
        }

        private void ViiperStickGyroMinGyroSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroMinGyroSpeedValueText != null)
                ViiperStickGyroMinGyroSpeedValueText.Text = FormatDegPerSec(e.NewValue);
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroMinGyroSpeedSlider, StickMinGyroSpeedSlider, null, null);
        }

        private void ViiperStickGyroMaxGyroSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroMaxGyroSpeedValueText != null)
                ViiperStickGyroMaxGyroSpeedValueText.Text = FormatDegPerSec(e.NewValue);
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroMaxGyroSpeedSlider, StickMaxGyroSpeedSlider, null, null);
        }

        private void ViiperStickGyroMinOutputSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroMinOutputValueText != null)
                ViiperStickGyroMinOutputValueText.Text = FormatPercent(e.NewValue);
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroMinOutputSlider, StickMinOutputSlider, null, null);
        }

        private void ViiperStickGyroMaxOutputSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroMaxOutputValueText != null)
                ViiperStickGyroMaxOutputValueText.Text = FormatPercent(e.NewValue);
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroMaxOutputSlider, StickMaxOutputSlider, null, null);
        }

        private void ViiperStickGyroPowerCurveSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroPowerCurveValueText != null)
                ViiperStickGyroPowerCurveValueText.Text = FormatPowerCurve(e.NewValue);
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroPowerCurveSlider, StickPowerCurveSlider, null, null);
        }

        private void ViiperStickGyroPrecisionSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroPrecisionSpeedValueText != null)
                ViiperStickGyroPrecisionSpeedValueText.Text = FormatPrecisionSpeed(e.NewValue);
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroPrecisionSpeedSlider, StickPrecisionSpeedSlider, null, null);
        }

        private void ViiperStickGyroOutputMixSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ViiperStickGyroOutputMixValueText != null)
                ViiperStickGyroOutputMixValueText.Text = FormatOutputMix(e.NewValue);
            if (isMirroringStickGyro) return;
            MirrorSliderValue(ViiperStickGyroOutputMixSlider, StickOutputMixSlider, null, null);
        }

        // ----- Reset to recommended defaults -----

        /// <summary>
        /// Restore vvalente30's recommended SteamOS-aligned defaults (issue #79).
        /// Drives the legacy controls; the legacy widget property pushes each value
        /// over the pipe, and the existing legacy → VIIPER mirror updates the
        /// VIIPER twin controls. One write path, no duplication.
        /// </summary>
        private void ViiperStickGyroResetButton_Click(object sender, RoutedEventArgs e) => ApplyStickGyroRecommendedDefaults();

        /// <summary>
        /// Twin handler used by the legacy CE panel's reset button (issue #79
        /// round 4: vvalente asked for a reset on the legacy side too after he
        /// got stuck with btn=Back persisted from earlier testing). Same
        /// underlying writes — the legacy and VIIPER UIs share LocalSettings
        /// keys, so a single defaults pass takes care of both.
        /// </summary>
        private void StickGyroLegacyResetButton_Click(object sender, RoutedEventArgs e) => ApplyStickGyroRecommendedDefaults();

        private void ApplyStickGyroRecommendedDefaults()
        {
            // Activation
            SetComboBoxByTag(ControllerEmulationGyroActivationModeComboBox, "0");   // Always On
            SetComboBoxByTag(ControllerEmulationGyroActivationButtonComboBox, "1"); // Right Trigger

            // Routing + axis mapping
            SetComboBoxByTag(ControllerEmulationStickSelectComboBox, "1");          // Right
            SetComboBoxByTag(StickConversionComboBox, "2");                         // Yaw + Roll
            SetComboBoxByTag(StickOrientationV2ComboBox, "0");                      // Parallel

            // Inverts
            if (ControllerEmulationStickInvertXToggle != null) ControllerEmulationStickInvertXToggle.IsOn = false;
            if (ControllerEmulationStickInvertYToggle != null) ControllerEmulationStickInvertYToggle.IsOn = false;

            // Slider values
            if (StickSensitivityV2Slider != null) StickSensitivityV2Slider.Value = 100;   // 1.00x
            if (StickDeadzoneSlider != null) StickDeadzoneSlider.Value = 2;
            if (StickMinGyroSpeedSlider != null) StickMinGyroSpeedSlider.Value = 0;
            if (StickMaxGyroSpeedSlider != null) StickMaxGyroSpeedSlider.Value = 220;
            if (StickMinOutputSlider != null) StickMinOutputSlider.Value = 0;
            if (StickMaxOutputSlider != null) StickMaxOutputSlider.Value = 100;
            if (StickPowerCurveSlider != null) StickPowerCurveSlider.Value = 100;         // 1.0
            if (StickPrecisionSpeedSlider != null) StickPrecisionSpeedSlider.Value = 0;   // Off
            if (StickOutputMixSlider != null) StickOutputMixSlider.Value = 0;             // Balanced

            // Master enable defaults to true (the section's whole reason to exist).
            if (ViiperStickGyroEnabledToggle != null) ViiperStickGyroEnabledToggle.IsOn = true;
        }

        private static void SetComboBoxByTag(ComboBox cb, string tag)
        {
            if (cb == null || string.IsNullOrEmpty(tag)) return;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem item && (item.Tag as string) == tag)
                {
                    if (cb.SelectedIndex != i) cb.SelectedIndex = i;
                    return;
                }
            }
        }

        // ----- Mirror primitives -----

        private void MirrorComboBoxIndex(ComboBox source, ComboBox target)
        {
            if (source == null || target == null) return;
            if (target.Items.Count == 0) return;
            int idx = source.SelectedIndex;
            if (idx < 0 || idx >= target.Items.Count) return;
            if (target.SelectedIndex == idx) return;

            isMirroringStickGyro = true;
            try { target.SelectedIndex = idx; }
            finally { isMirroringStickGyro = false; }
        }

        private void MirrorSliderValue(Slider source, Slider target, TextBlock targetValueText, Func<double, string> formatter)
        {
            if (source == null) return;
            double v = source.Value;
            if (target != null && Math.Abs(target.Value - v) > 0.001)
            {
                isMirroringStickGyro = true;
                try { target.Value = v; }
                finally { isMirroringStickGyro = false; }
            }
            if (targetValueText != null && formatter != null)
            {
                targetValueText.Text = formatter(v);
            }
        }

        private void MirrorToggle(ToggleSwitch source, ToggleSwitch target)
        {
            if (source == null || target == null) return;
            if (target.IsOn == source.IsOn) return;
            isMirroringStickGyro = true;
            try { target.IsOn = source.IsOn; }
            finally { isMirroringStickGyro = false; }
        }

        /// <summary>
        /// Read a ComboBox's currently selected ComboBoxItem.Tag and parse it as
        /// an int. Returns -1 when the tag is missing or not numeric. Used by
        /// the status badge to map an activation-button selection to its short
        /// display name without round-tripping through verbose Content strings.
        /// </summary>
        private static int ParseSelectedTagInt(ComboBox cb)
        {
            if (cb?.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && int.TryParse(tag, out int parsed))
            {
                return parsed;
            }
            return -1;
        }

        /// <summary>
        /// Compact display name (1-4 chars) for each activation-button index.
        /// Mirrors the index list in <see cref="ViiperStickGyroProcessor.IsActivationButtonPressed"/>
        /// and the legacy CE equivalent. Lives on the widget side because the
        /// badge is widget-only.
        /// </summary>
        private static string ShortNameForActivationButton(int idx)
        {
            switch (idx)
            {
                case 1: return "RT";
                case 2: return "LT";
                case 3: return "RB";
                case 4: return "LB";
                case 5: return "A";
                case 6: return "B";
                case 7: return "X";
                case 8: return "Y";
                case 9: return "R3";
                case 10: return "L3";
                case 11: return "Up";
                case 12: return "Down";
                case 13: return "Left";
                case 14: return "Right";
                case 15: return "Start";
                case 16: return "Back";
                case 17: return "M3";
                case 18: return "M1";
                case 19: return "M2";
                case 20: return "Y1";
                case 21: return "Y2";
                case 22: return "Y3";
                default: return "?";
            }
        }

        private static string FormatSensitivity(double v) => $"{v / 100.0:F2}x";
        private static string FormatDegPerSec(double v) => $"{v:F0}°/s";
        private static string FormatPercent(double v) => $"{v:F0}%";
        private static string FormatPowerCurve(double v) => $"{v / 100.0:F1}";
        private static string FormatPrecisionSpeed(double v) => v <= 0 ? "Off" : $"{v:F0}°/s";
        private static string FormatOutputMix(double v)
        {
            if (Math.Abs(v) < 0.5) return "Balanced";
            return v > 0 ? $"Horizontal {v:F0}%" : $"Vertical {-v:F0}%";
        }

        /// <summary>
        /// Show/hide the Gyro → Right Stick section based on the currently
        /// selected VIIPER device type. Mirrors <c>ViiperStickGyroProcessor.IsApplicableForTarget</c>
        /// on the helper side so the UI is visible exactly when the processor
        /// would actually run.
        /// </summary>
        private async void UpdateViiperStickGyroSectionVisibility()
        {
            if (ViiperStickGyroSection == null || viiperDeviceType == null) return;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                bool applicable = IsViiperTargetWithoutNativeMotion(viiperDeviceType.Value);
                ViiperStickGyroSection.Visibility = applicable ? Visibility.Visible : Visibility.Collapsed;
                if (applicable)
                {
                    WireViiperStickGyroMirror();
                }
            });
        }

        private static bool IsViiperTargetWithoutNativeMotion(string targetType)
        {
            if (string.IsNullOrEmpty(targetType)) return false;
            switch (targetType)
            {
                case "xbox360":
                case "xboxelite2":
                case "xbox-one":
                case "xbox-elite":
                case "steamdeck-generic":
                case "steam-generic":
                case "steam-controller":
                case "switchpro":
                case "joycon-left":
                case "joycon-right":
                    return true;
                default:
                    return false;
            }
        }
    }
}
