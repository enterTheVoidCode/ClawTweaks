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
        // Intel gaming 3D features (IGCL) — combobox int properties (helper-authoritative, like CPU advanced).
        private XboxGamingBar.Data.CpuIntComboProperty intelLowLatency; // 0=Off,1=On,2=On+Boost
        private XboxGamingBar.Data.CpuIntComboProperty intelFrameSync;  // 0=App default,1=VSync off,2=VSync on,3=Smooth,4=Speed
        // Reached through the helper's direct ControlLib binding rather than IGCL_Wrapper.dll, but from
        // the widget's side they behave exactly like the two above: the helper owns the value.
        // Which renderer draws the OSD: 0 = RTSS, 1 = the built-in overlay (Doku/PLAN_Native_OSD.md).
        // Lives on the OSD card, not here, but shares this file's property block.
        private XboxGamingBar.Data.CpuIntComboProperty osdRenderer;
        // Where the built-in overlay anchors: MSI's six positions, 1..6. Built-in only - RTSS keeps
        // its own position setting.
        private XboxGamingBar.Data.CpuIntComboProperty osdPosition;
        private XboxGamingBar.Data.CpuIntComboProperty intelFrameGeneration; // 0=App choice,1=2X,2=3X,3=4X
        private XboxGamingBar.Data.CpuIntComboProperty intelVrr;             // 0=Off,1=On
        private XboxGamingBar.Data.CpuIntComboProperty intelVrrMode;         // 0=Auto,1=Windowed+Fullscreen,2=Fullscreen
        private XboxGamingBar.Data.CpuIntComboProperty intelScalingMode;     // 0=Display,1=GPU,2=Retro
        private XboxGamingBar.Data.CpuIntComboProperty intelScalingMethod;   // index within the mode's list

        // Reference-image carousel (gallery logic ported from TnC ColorRemasterMainPage):
        // an array of packaged images + an index; tapping advances and wraps. Currently one
        // image; add more URIs here (and Content-include the assets) to grow the gallery.
        // Exact order: FF16, Dark (and Darker), Stardew Valley.
        private readonly string[] _displayRefImages = new[]
        {
            "ms-appx:///Assets/ColorReference1.png",
            "ms-appx:///Assets/ColorReference2.png",
            "ms-appx:///Assets/ColorReference3.png",
        };
        private int _displayRefIndex = 0;

        private void InitializeDisplayTab()
        {
            try
            {
                if (DisplayNavItem != null)
                    DisplayNavItem.Visibility = IsMsiClawDevice() ? Visibility.Visible : Visibility.Collapsed;

                // Hide the carousel controls when there's nothing to cycle.
                var multi = _displayRefImages.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
                if (DisplayRefPrevButton != null) DisplayRefPrevButton.Visibility = multi;
                if (DisplayRefNextButton != null) DisplayRefNextButton.Visibility = multi;
                if (DisplayRefDots != null) DisplayRefDots.Visibility = multi;

                UpdateDisplayReferenceImage();
                UpdateDisplayProfileBadge();
                // Re-apply what the helper last reported — NOT a hardcoded "hidden".
                //
                // This method runs more than once: from the constructor, and again from the
                // deviceDisplayName PropertyChanged handler. Resetting to false here threw the helper's
                // answer away. Measured 2026-08-03: the caps arrived at 14:20:29.67 and the card was
                // visible, then a device-name sync at 14:20:33.81 re-ran this and hid it again — the
                // controls existed for four seconds and then vanished for good.
                ApplyIntelDirectFeatureVisibility();
                // Ask the helper for the capability answer if we do not have one yet. The connect-time
                // push only reaches whichever instance existed then; the Game Bar rebuilds this one
                // routinely, and a rebuilt instance would otherwise sit with the controls hidden.
                if (!_intelCapsFrameGen.HasValue) RequestWidgetStateFromHelper();
            }
            catch (Exception ex)
            {
                Logger.Debug($"InitializeDisplayTab: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows or hides the Frame Generation / VRR controls according to what the helper's capability
        /// probe reported, and re-points the D-pad chain accordingly.
        ///
        /// The chain has to move with them. A hidden control is not a focus candidate, so leaving
        /// "Low Latency ↓ Frame Generation" wired while the card is collapsed strands the user at the
        /// bottom of the Gaming card with nothing below — the same failure that made the driver list and
        /// the power-split toggle unreachable. Rewiring in code is the only option here because which
        /// controls exist is not known until the helper answers.
        ///
        /// Each column is hidden independently: frame generation is Arc-only in practice while VRR is
        /// not, so a device with one and not the other is the normal case, not an edge case.
        /// </summary>
        // What the helper last reported. Null = never heard from it, which is the only state in which
        // hiding the controls is a guess rather than an answer.
        private bool? _intelCapsFrameGen;
        private bool? _intelCapsVrr;
        private bool? _intelCapsVrrMode;
        private bool? _intelCapsScaling;

        /// <summary>
        /// Rebuilds the method list when the group changes. Keeps the current index where the new group
        /// still has one (FillScalingMethods clamps), so a helper sync that delivers mode and method as
        /// two separate messages cannot land on 0 just because the mode arrived first.
        /// </summary>
        private void IntelScalingMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                FillScalingMethods(SelectedScalingMode(), IntelScalingMethodComboBox?.SelectedIndex ?? 0);
            }
            catch (Exception ex) { Logger.Warn($"[Scaling] rebuilding the method list failed: {ex.Message}"); }
        }

        /// <summary>
        /// The driver's mode VALUE behind the selected item, not its index. Those stopped being the same
        /// thing when "Display Scaling" left the list: GPU keeps value 1 and Retro keeps 2, so that a
        /// profile written before the change still resolves to the same mode.
        /// </summary>
        private int SelectedScalingMode()
        {
            if (IntelScalingModeComboBox?.SelectedItem is ComboBoxItem item
                && item.Tag is string tag && int.TryParse(tag, out int value))
                return value;
            return GpuScalingMode;
        }

        /// <summary>Driver mode values, kept as names because they are no longer list indices.</summary>
        private const int GpuScalingMode = 1;
        private const int RetroScalingMode = 2;

        /// <summary>
        /// The method entries belonging to a scaling group, index = the stored IntelScalingMethod.
        ///
        /// Shared with the profile cards and the game-start notification, which name a saved scaling
        /// setting without a ComboBox to read it off. They used to have no way to name it at all, so
        /// scaling was the one Intel setting a profile could carry and never show.
        /// </summary>
        private static string[] ScalingMethodNames(int mode)
            => mode == RetroScalingMode
                ? new[] { "Integer", "Nearest Neighbour" }
                : new[] { "Centered", "Stretch", "Preserve Aspect Ratio" };

        /// <summary>
        /// Rebuilds the Scaling Method list for the selected Scaling Mode. The entries belong to their
        /// group and are NOT interchangeable — "Integer" is a retro-scaling type reached through a
        /// different API than "Stretch", so one flat list would let the user pick a combination that
        /// cannot be written.
        /// </summary>
        private void FillScalingMethods(int mode, int selectIndex)
        {
            if (IntelScalingMethodComboBox == null) return;

            string[] entries = ScalingMethodNames(mode);

            IntelScalingMethodComboBox.Items.Clear();
            for (int i = 0; i < entries.Length; i++)
            {
                IntelScalingMethodComboBox.Items.Add(new ComboBoxItem
                {
                    Content = entries[i],
                    Tag = i.ToString(),
                });
            }
            // A method index from another group can be out of range here — clamp rather than throw.
            // Same class of bug as the ungated SelectedIndex assignments in the profile load path.
            IntelScalingMethodComboBox.SelectedIndex =
                selectIndex >= 0 && selectIndex < entries.Length ? selectIndex : 0;
        }

        /// <summary>
        /// Keeps the position dropdown tied to the renderer: it drives the built-in overlay only.
        ///
        /// RTSS has no alignment control we could set - it keeps its position in its own configuration,
        /// and offering a dropdown that silently does nothing under RTSS is worse than not offering it.
        /// Disabled rather than hidden, so the row does not reflow when the renderer changes, and
        /// because the panel's focus walker skips a disabled control on its own.
        /// </summary>
        private void OsdRendererComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                bool builtIn = OsdRendererComboBox?.SelectedItem is ComboBoxItem item
                               && item.Tag is string tag
                               && tag == "1";

                if (OsdPositionComboBox != null) OsdPositionComboBox.IsEnabled = builtIn;

                if (OsdRendererCaption != null)
                {
                    OsdRendererCaption.Text = builtIn
                        ? "RTSS draws inside games only. The built-in overlay also shows on the desktop."
                        : "RTSS draws inside games only, and keeps its own position - alignment applies to the built-in overlay.";
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"OsdRendererComboBox_SelectionChanged failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetch button: ask the helper to read the Intel driver back and report what is actually set.
        ///
        /// This exists because Intel Graphics Software turned out to be an unreliable witness — it does
        /// not re-read settings changed by another process, so it kept displaying old scaling and VRR
        /// values long after ours had taken effect. Several of these settings also answer SUCCESS while
        /// storing something else, so "we wrote it" is not evidence either.
        /// </summary>
        private async void IntelFetchState_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IntelDriverStateText != null)
                {
                    IntelDriverStateText.Text = App.IsConnected ? "Reading…" : "Helper not connected.";
                    IntelDriverStateText.Visibility = Visibility.Visible;
                }
                if (!App.IsConnected) return;

                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "RequestIntelDriverState", "1" },
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"[IntelDirect] fetch request failed: {ex.Message}");
                if (IntelDriverStateText != null) IntelDriverStateText.Text = $"Request failed: {ex.Message}";
            }
        }

        /// <summary>Shows the helper's readout. Called on the UI thread.</summary>
        private void OnIntelDriverState(string report)
        {
            if (IntelDriverStateText == null) return;
            IntelDriverStateText.Text = string.IsNullOrWhiteSpace(report) ? "No answer from the driver." : report;
            IntelDriverStateText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Asks the helper to re-send the state a fresh widget instance cannot have: the Intel
        /// capability answer and which fan curve is running. Fire and forget — the answers arrive as
        /// normal pushes.
        /// </summary>
        private async void RequestWidgetStateFromHelper()
        {
            try
            {
                if (!App.IsConnected) return;
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "RequestWidgetState", "1" } });
                Logger.Info("[IntelDirect] asked the helper for capabilities + fan scope");
            }
            catch (Exception ex) { Logger.Info($"RequestWidgetStateFromHelper: {ex.Message}"); }
        }

        /// <summary>Records what the helper reported and applies it. The only caller is the pipe.</summary>
        internal void SetIntelDirectFeatureVisibility(bool frameGenSupported, bool vrrSupported,
                                                      bool vrrModeSupported = false,
                                                      bool scalingSupported = false,
                                                      bool hasActiveOutput = true,
                                                      bool atNativeResolution = true)
        {
            _intelCapsFrameGen = frameGenSupported;
            // Sticky once true, for VRR and scaling only.
            //
            // Both capability probes ask the driver for an eligible DISPLAY OUTPUT, so they go false
            // whenever the current display state makes the feature unusable - VRR support disappears at
            // a non-native resolution, because no output reports Arc Sync there. Taken at face value
            // that hides the control entirely, while scaling right beside it merely greys out: the same
            // situation, two different answers, and the user cannot tell "your device cannot do this"
            // from "not right now".
            //
            // A property that comes and goes with the resolution is not a capability. Presence is
            // therefore latched for the session and the CURRENT state is expressed by the gate, which
            // greys the control and says why. Frame generation and the VRR sub-mode are adapter-global
            // and do not flap, so they stay as reported.
            _intelCapsVrr = (_intelCapsVrr ?? false) || vrrSupported;
            _intelCapsVrrMode = vrrModeSupported;
            _intelCapsScaling = (_intelCapsScaling ?? false) || scalingSupported;
            _intelHasActiveOutput = hasActiveOutput;
            _intelAtNativeResolution = atNativeResolution;
            // Gate FIRST, then the chain. A disabled control is skipped by XY focus, so the D-pad links
            // have to know which controls are gated off - pointing at one is a dead end, and that is
            // exactly where the navigation got stuck.
            ApplyDisplayGate();
            ApplyIntelDirectFeatureVisibility();
        }

        // Whether the driver can act at all right now, as opposed to whether it supports the feature.
        // Defaults are permissive: a helper that predates these two fields must not grey out controls
        // that work.
        private bool _intelHasActiveOutput = true;
        private bool _intelAtNativeResolution = true;

        // Result of the gate, kept because the D-pad chain needs it: a control that is disabled is not
        // focusable, so for navigation purposes it counts as absent.
        private bool _scalingUsable = true;
        private bool _vrrSwitchUsable = true;

        /// <summary>
        /// Greys out the settings that cannot do anything in the current display state, and says why.
        ///
        /// The two conditions are OPPOSITE, which is the part worth remembering: scaling only acts when
        /// the source resolution is BELOW the panel's, VRR only when it is exactly the panel's. So at
        /// most one of the two blocks is ever live. With no active display output at all, neither is.
        ///
        /// The reason replaces the caption rather than adding a line, so nothing moves when the state
        /// changes and the controls stay lined up across the columns.
        /// </summary>
        private void ApplyDisplayGate()
        {
            try
            {
                bool scalingUsable = _intelHasActiveOutput && !_intelAtNativeResolution;
                bool vrrUsable = _intelHasActiveOutput && _intelAtNativeResolution;
                _scalingUsable = scalingUsable;
                _vrrSwitchUsable = vrrUsable;

                // Kept short deliberately. These sit in the three-column VRR row, where a caption of more
                // than about three words wraps and pushes its combo out of line with the others.
                string noOutput = "No active display";
                string scalingWhy = !_intelHasActiveOutput ? noOutput : "Only below native res.";
                string vrrWhy = !_intelHasActiveOutput ? noOutput : "Only at native res.";

                if (IntelScalingModeComboBox != null) IntelScalingModeComboBox.IsEnabled = scalingUsable;
                if (IntelScalingMethodComboBox != null) IntelScalingMethodComboBox.IsEnabled = scalingUsable;
                if (IntelScalingModeCaption != null)
                    IntelScalingModeCaption.Text = scalingUsable ? "Which device scales" : scalingWhy;
                if (IntelScalingMethodCaption != null)
                    IntelScalingMethodCaption.Text = scalingUsable ? "How it fits the panel" : scalingWhy;

                // The sub-mode is locked together with the switch, even though it would still write:
                // it is an adapter-global registry value and applies with no eligible display at all
                // (measured 2026-08-05, where it took effect while the switch beside it had nothing to
                // act on). Leaving it live was the technically accurate choice and the confusing one -
                // one half of a pair greyed out and the other not. It only means anything while VRR is
                // actually running, which needs the native resolution anyway, so nothing is lost.
                if (IntelVrrComboBox != null) IntelVrrComboBox.IsEnabled = vrrUsable;
                if (IntelVrrModeComboBox != null) IntelVrrModeComboBox.IsEnabled = vrrUsable;
                if (IntelVrrCaption != null)
                    IntelVrrCaption.Text = vrrUsable ? "Intel Arc Sync" : vrrWhy;
                if (IntelVrrModeCaption != null)
                    IntelVrrModeCaption.Text = vrrUsable ? "Where VRR applies" : vrrWhy;

                Logger.Info($"[IntelDirect] display gate: activeOutput={_intelHasActiveOutput}, " +
                            $"atNative={_intelAtNativeResolution} => scaling={scalingUsable}, vrr={vrrUsable}");
            }
            catch (Exception ex) { Logger.Warn($"ApplyDisplayGate: {ex.Message}"); }
        }

        /// <summary>Applies the cached capability state to the controls and the D-pad chain.</summary>
        private void ApplyIntelDirectFeatureVisibility()
        {
            bool frameGenSupported = _intelCapsFrameGen ?? false;
            bool vrrSupported = _intelCapsVrr ?? false;
            bool vrrModeSupported = _intelCapsVrrMode ?? false;
            try
            {
                if (IntelFrameGenerationPanel != null)
                    IntelFrameGenerationPanel.Visibility = frameGenSupported ? Visibility.Visible : Visibility.Collapsed;
                if (IntelVrrPanel != null)
                    IntelVrrPanel.Visibility = vrrSupported ? Visibility.Visible : Visibility.Collapsed;
                if (IntelVrrModePanel != null)
                    IntelVrrModePanel.Visibility = vrrModeSupported ? Visibility.Visible : Visibility.Collapsed;

                if (IntelScalingCard != null)
                    IntelScalingCard.Visibility = (_intelCapsScaling ?? false) ? Visibility.Visible : Visibility.Collapsed;

                bool anyVisible = frameGenSupported || vrrSupported || vrrModeSupported;
                if (IntelDirectFeaturesCard != null)
                    IntelDirectFeaturesCard.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;

                // D-pad chain. The Intel-direct row (Frame Gen | VRR | VRR Mode) now sits ABOVE Low
                // Latency / Frame Sync, so the links run the other way than they used to. Every one of
                // the three cells is capability-gated on its own, which is why this is computed rather
                // than left to the XAML: a hidden cell must not be a dead end, or the row below becomes
                // unreachable with a controller. That is what happened to the VRR controls before.
                // "Reachable" is supported AND not gated off. Both matter for navigation and for the same
                // reason: XY focus skips a disabled control, so a link that points at one goes nowhere.
                // Treating gated-off exactly like hidden keeps one rule instead of two.
                bool scalingCard = (_intelCapsScaling ?? false) && _scalingUsable;
                bool vrrReachable = vrrSupported && _vrrSwitchUsable;
                bool vrrModeReachable = vrrModeSupported && _vrrSwitchUsable;
                DependencyObject belowIntelRow = scalingCard
                    ? (DependencyObject)IntelScalingModeComboBox : ResolutionComboBox;

                // Sideways within the Intel row, skipping whichever cells cannot take focus.
                DependencyObject firstOfRow = frameGenSupported ? (DependencyObject)IntelFrameGenerationComboBox
                                            : vrrReachable ? (DependencyObject)IntelVrrComboBox
                                            : vrrModeReachable ? (DependencyObject)IntelVrrModeComboBox : null;
                if (IntelFrameGenerationComboBox != null)
                    IntelFrameGenerationComboBox.XYFocusRight = vrrReachable
                        ? (DependencyObject)IntelVrrComboBox
                        : (vrrModeReachable ? (DependencyObject)IntelVrrModeComboBox : IntelFrameGenerationComboBox);
                if (IntelVrrComboBox != null)
                {
                    IntelVrrComboBox.XYFocusLeft = frameGenSupported
                        ? (DependencyObject)IntelFrameGenerationComboBox : IntelVrrComboBox;
                    IntelVrrComboBox.XYFocusRight = vrrModeReachable
                        ? (DependencyObject)IntelVrrModeComboBox : IntelVrrComboBox;
                }
                if (IntelVrrModeComboBox != null)
                    IntelVrrModeComboBox.XYFocusLeft = vrrReachable
                        ? (DependencyObject)IntelVrrComboBox
                        : (frameGenSupported ? (DependencyObject)IntelFrameGenerationComboBox : IntelVrrModeComboBox);

                // Down out of the Intel row into Low Latency / Frame Sync.
                if (IntelFrameGenerationComboBox != null) IntelFrameGenerationComboBox.XYFocusDown = IntelLowLatencyComboBox;
                if (IntelVrrComboBox != null) IntelVrrComboBox.XYFocusDown = IntelFrameSyncComboBox;
                if (IntelVrrModeComboBox != null) IntelVrrModeComboBox.XYFocusDown = IntelFrameSyncComboBox;

                // Up out of Low Latency / Frame Sync — to the Intel row when it is there, otherwise past
                // it to the colour card's reset button, which is what sits above.
                if (IntelLowLatencyComboBox != null)
                    IntelLowLatencyComboBox.XYFocusUp = firstOfRow ?? DisplayResetButton;
                if (IntelFrameSyncComboBox != null)
                    IntelFrameSyncComboBox.XYFocusUp = vrrReachable
                        ? (DependencyObject)IntelVrrComboBox
                        : (vrrModeReachable ? (DependencyObject)IntelVrrModeComboBox
                                            : (firstOfRow ?? DisplayResetButton));

                // Down out of Low Latency / Frame Sync — through the scaling card when it exists.
                if (IntelLowLatencyComboBox != null) IntelLowLatencyComboBox.XYFocusDown = belowIntelRow;
                if (IntelFrameSyncComboBox != null)
                    IntelFrameSyncComboBox.XYFocusDown = scalingCard
                        ? (DependencyObject)IntelScalingMethodComboBox : RefreshRatesComboBox;

                // ...and the upward links from the Display card, mirrored.
                if (ResolutionComboBox != null)
                    ResolutionComboBox.XYFocusUp = scalingCard
                        ? (DependencyObject)IntelScalingModeComboBox : IntelLowLatencyComboBox;
                if (RefreshRatesComboBox != null)
                    RefreshRatesComboBox.XYFocusUp = scalingCard
                        ? (DependencyObject)IntelScalingMethodComboBox : IntelFrameSyncComboBox;

                Logger.Info($"[IntelDirect] feature visibility: frameGen={frameGenSupported}, vrr={vrrSupported}" +
                            $"{(_intelCapsFrameGen.HasValue ? "" : " (helper has not reported yet)")}");
            }
            catch (Exception ex)
            {
                Logger.Debug($"ApplyIntelDirectFeatureVisibility: {ex.Message}");
            }
        }

        /// <summary>
        /// Green "Per-Game: <name>" when a per-game profile is active, otherwise orange
        /// "Editing Global profile" — same colour scheme as the controller/performance badges.
        /// Display settings live in the performance profile, so the per-game condition matches.
        /// </summary>
        private void UpdateDisplayProfileBadge()
        {
            if (DisplayProfileModeBadge == null || DisplayProfileModeText == null) return;
            bool isPerGame = (PerGameProfileToggle?.IsOn ?? false) && HasValidGame(currentGameName);
            ApplyProfileStatusBadge(DisplayProfileModeBadge, DisplayProfileModeText, isPerGame,
                isPerGame ? "Game Profile" : "Global Profile");
        }

        private void UpdateDisplayReferenceImage()
        {
            if (DisplayReferenceImage == null || _displayRefImages.Length == 0) return;
            try
            {
                if (_displayRefIndex < 0 || _displayRefIndex >= _displayRefImages.Length) _displayRefIndex = 0;
                DisplayReferenceImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri(_displayRefImages[_displayRefIndex]));
                UpdateDisplayRefDots();
            }
            catch (Exception ex)
            {
                Logger.Debug($"UpdateDisplayReferenceImage: {ex.Message}");
            }
        }

        /// <summary>Rebuilds the dot indicator so the user can see how many images exist
        /// and which one is shown — the active dot is solid white, the rest dim.</summary>
        private void UpdateDisplayRefDots()
        {
            if (DisplayRefDots == null) return;
            try
            {
                DisplayRefDots.Children.Clear();
                if (_displayRefImages.Length <= 1) return;
                for (int i = 0; i < _displayRefImages.Length; i++)
                {
                    bool active = i == _displayRefIndex;
                    DisplayRefDots.Children.Add(new Windows.UI.Xaml.Shapes.Ellipse
                    {
                        Width = active ? 8 : 6,
                        Height = active ? 8 : 6,
                        VerticalAlignment = VerticalAlignment.Center,
                        Fill = new Windows.UI.Xaml.Media.SolidColorBrush(
                            active ? Windows.UI.Colors.White
                                   : Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"UpdateDisplayRefDots: {ex.Message}");
            }
        }

        private void CycleDisplayReference(int delta)
        {
            if (_displayRefImages.Length <= 1) return;
            int n = _displayRefImages.Length;
            _displayRefIndex = ((_displayRefIndex + delta) % n + n) % n; // wrap both directions
            UpdateDisplayReferenceImage();
        }

        // Tapping the image and the on-screen arrows all cycle the gallery. The arrows are real
        // Buttons so they're reachable + clickable with the controller (A), not just the mouse.
        private void DisplayReferenceImage_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
            => CycleDisplayReference(+1);

        private void DisplayRefNextButton_Click(object sender, RoutedEventArgs e) => CycleDisplayReference(+1);

        private void DisplayRefPrevButton_Click(object sender, RoutedEventArgs e) => CycleDisplayReference(-1);

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

        // ApplyDisplayFromProfile is GONE (plan §5.4). It restored the six Intel sliders from the
        // WIDGET's profile copy and pushed them to the helper — the exact shape this rebuild removes.
        // It had no callers left: the helper applies its own IntelDisplay block per profile
        // (ApplyIntelDisplayFromProfile) and syncs the sliders down. SetDisplaySlider below stays; it
        // is what the helper-driven sync and the reset button use.

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
        /// <summary>
        /// Intel display summary, read from the HELPER's profile (plan §5.3). These are group-B fields:
        /// the helper owns and applies them, the widget only displays them.
        ///
        /// The fields are nullable here and were not in the widget's copy — null means "never captured
        /// for this profile", which resolves to the neutral value and therefore prints nothing. That is
        /// the same outcome the old code produced for a default profile, just without inventing a value.
        ///
        /// Note IntelDisplayGamma: the helper's element is named without the X100 suffix the widget's
        /// copy used, but the encoding is the same ×100 (verified against real profile files: 100 = 1.0).
        /// </summary>
        private string BuildDisplaySummary(Shared.Data.GameProfile p)
        {
            if (p == null) return null;
            var parts = new System.Collections.Generic.List<string>();
            int saturation = p.IntelColorSaturation ?? 50;
            int hue        = p.IntelColorHue ?? 0;
            int contrast   = p.IntelDisplayContrast ?? 50;
            int brightness = p.IntelDisplayBrightness ?? 50;
            int gammaX100  = p.IntelDisplayGamma ?? 100;
            int sharpness  = p.IntelAdaptiveSharpness ?? 0;

            if (saturation != 50) parts.Add($"Sat {saturation}");
            if (hue != 0) parts.Add($"Hue {hue}");
            if (contrast != 50) parts.Add($"Con {contrast}");
            if (brightness != 50) parts.Add($"Bri {brightness}");
            if (gammaX100 != 100) parts.Add($"Gam {(gammaX100 / 100.0):0.00}");
            if (sharpness > 0) parts.Add($"Sharp {sharpness}");
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }
}
