using System;
using System.Globalization;
using System.Linq;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace XboxGamingBar
{
    /// <summary>
    /// MSI Claw (Lunar Lake / A2VM) fan control card on the Performance tab.
    ///
    /// The widget chooses an enable state, a preset, and (for "Custom") an 11-point curve via
    /// a drag editor. The actual EC fan-table write happens helper-side via MsiClawFanController
    /// (ported from the HC fork); the EC then drives the fan smoothly from our table.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private const string MsiFanEnabledKey = "MsiFan_Enabled";
        private const string MsiFanPresetKey  = "MsiFan_Preset";   // 0/1/2/3 (3 = custom)
        private const string MsiFanCurveKey   = "MsiFan_CurveCsv"; // 11 ints when custom

        // Preset curves — MUST match MsiClawFanController on the helper side.
        // Quiet at idle/low load, but the 80-100 °C end now cools at firmware level: under-cooling at
        // the top during sustained full load let the CPU climb until the EC seized the fan and latched
        // it loud (only a firmware hand-back cleared it). Helper FanTableScale = 150 maps these to the
        // full EC 0-150 range.
        //                                                 0  10  20  30  40  50  60  70  80  90  100 °C
        private static readonly double[] MsiCurveQuiet      = { 0, 0, 0,  0,  0,  0,  8, 22, 40, 63,  87 };
        private static readonly double[] MsiCurveDefault    = { 0, 0, 0,  0,  2,  4, 14, 30, 50, 75,  97 };
        private static readonly double[] MsiCurveAggressive = { 0, 0, 0,  3,  6, 10, 22, 40, 63, 85, 100 };
        // "Cooling" (early-ramp): front-loads the 50-70 °C band so the fan spins before a game heats
        // past 80 °C, avoiding the EC ~90 °C thermal-protection latch. Must match Curve_Cooling in the
        // helper (MsiClawFanController). Silent at <=40 °C.
        private static readonly double[] MsiCurveCooling    = { 0, 0, 0,  0,  3, 22, 38, 50, 65, 85, 100 };

        private readonly double[] _msiFanCurve = (double[])MsiCurveDefault.Clone();
        private readonly Ellipse[] _msiFanPoints = new Ellipse[11];
        private readonly TextBlock[] _msiFanValueLabels = new TextBlock[11];
        private bool _msiFanPointsBuilt;
        private bool _msiFanInitializing;
        private int _msiFanDragIndex = -1;

        private bool IsMsiClawDevice()
            => deviceDisplayName?.Value?.IndexOf("Claw", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Show the fan card on MSI Claw and restore the saved state. Idempotent.</summary>
        private void InitializeMsiFanCard()
        {
            if (MsiFanCard == null) return;

            // Fan tab is MSI-Claw-only; gate its nav item like the Display tab.
            if (FanNavItem != null)
                FanNavItem.Visibility = IsMsiClawDevice() ? Visibility.Visible : Visibility.Collapsed;

            if (!IsMsiClawDevice())
            {
                MsiFanCard.Visibility = Visibility.Collapsed;
                return;
            }

            MsiFanCard.Visibility = Visibility.Visible;
            if (LegionFanCurveCard != null)
                LegionFanCurveCard.Visibility = Visibility.Collapsed;

            BuildMsiFanPoints();

            _msiFanInitializing = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                bool enabled = settings.Values.TryGetValue(MsiFanEnabledKey, out var enObj) && enObj is bool b && b;
                int preset = (settings.Values.TryGetValue(MsiFanPresetKey, out var pObj) && pObj is int p) ? p : 1;
                if (preset < 0 || preset > 5) preset = 1;

                // Restore the curve for the selected preset (custom from storage; presets from constants).
                LoadCurveForPreset(preset);

                if (MsiFanEnableToggle != null) MsiFanEnableToggle.IsOn = enabled;
                if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.SelectedIndex = preset;
                if (MsiFanContent != null) MsiFanContent.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _msiFanInitializing = false;
            }

            RenderMsiFanCurve();
            // NOTE: deliberately NO SendMsiFanStateToHelper() here. The helper owns the fan state:
            // it restores MsiFan_Value at boot and pushes it to us via OnMsiFanState on connect.
            // Pushing on open previously overrode the helper's value (e.g. snapping back to Default).
        }

        /// <summary>
        /// Applies the fan state the helper pushed on connect (authoritative). Updates the UI +
        /// the widget's cached keys without echoing back to the helper.
        /// Payload: "&lt;value&gt;|&lt;curveCsv&gt;" — value -1=disabled, 0=Quiet, 1=Default, 2=Aggressive,
        /// 3=Cooling(early ramp), 4=Custom, 5=Cooling(EC Sport).
        /// </summary>
        internal void OnMsiFanState(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            var parts = payload.Split('|');
            if (!int.TryParse(parts[0], out int value)) return;
            string curve = parts.Length > 1 ? parts[1] : "";

            bool enabled = value >= 0;
            int preset = (value >= 0 && value <= 5) ? value : 1;

            _msiFanInitializing = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[MsiFanEnabledKey] = enabled;
                if (value >= 0) settings.Values[MsiFanPresetKey] = preset;
                if (value == 4 && !string.IsNullOrEmpty(curve))
                    settings.Values[MsiFanCurveKey] = curve;

                LoadCurveForPreset(preset);
                if (MsiFanEnableToggle != null) MsiFanEnableToggle.IsOn = enabled;
                if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.SelectedIndex = preset;
                if (MsiFanContent != null) MsiFanContent.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _msiFanInitializing = false;
            }
            RenderMsiFanCurve();
            Logger.Info($"OnMsiFanState applied: value={value} enabled={enabled} preset={preset}");
        }

        private void BuildMsiFanPoints()
        {
            if (_msiFanPointsBuilt || MsiFanCurveCanvas == null) return;
            for (int i = 0; i < 11; i++)
            {
                var ellipse = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 0, 170, 255)),
                    Stroke = new SolidColorBrush(Windows.UI.Colors.White),
                    StrokeThickness = 2,
                    Tag = i
                };
                _msiFanPoints[i] = ellipse;
                MsiFanCurveCanvas.Children.Add(ellipse);

                // Value label shown above each control point.
                var label = new TextBlock
                {
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    IsHitTestVisible = false
                };
                _msiFanValueLabels[i] = label;
                MsiFanCurveCanvas.Children.Add(label);
            }
            _msiFanPointsBuilt = true;
        }

        /// <summary>Load the 11-point curve for a preset index into <see cref="_msiFanCurve"/>.</summary>
        private void LoadCurveForPreset(int preset)
        {
            double[] src;
            switch (preset)
            {
                case 0: src = MsiCurveQuiet; break;
                case 2: src = MsiCurveAggressive; break;
                case 3: src = MsiCurveCooling; break;
                case 4: src = LoadCustomCurveFromStorage() ?? MsiCurveDefault; break;
                case 5: src = MsiCurveAggressive; break; // EC Sport: graph is a placeholder (the EC drives the fan)
                default: src = MsiCurveDefault; break;
            }
            Array.Copy(src, _msiFanCurve, 11);
        }

        private double[] LoadCustomCurveFromStorage()
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(MsiFanCurveKey, out var o)
                    && o is string csv)
                {
                    var parts = csv.Split(',');
                    if (parts.Length == 11)
                    {
                        var r = new double[11];
                        for (int i = 0; i < 11; i++)
                            r[i] = Math.Max(0, Math.Min(100, double.Parse(parts[i], CultureInfo.InvariantCulture)));
                        return r;
                    }
                }
            }
            catch (Exception ex) { Logger.Debug($"LoadCustomCurveFromStorage: {ex.Message}"); }
            return null;
        }

        private string CurveToCsv()
            => string.Join(",", _msiFanCurve.Select(v => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture)));

        private void RenderMsiFanCurve()
        {
            if (MsiFanCurveCanvas == null || MsiFanCurvePolyline == null || MsiFanCurveFill == null) return;
            double width = MsiFanCurveCanvas.ActualWidth;
            double height = MsiFanCurveCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            var pts = new PointCollection();
            var fill = new PointCollection();
            for (int i = 0; i < 11; i++)
            {
                double x = (i / 10.0) * width;                       // 0…100 °C across the width
                double y = height - (_msiFanCurve[i] / 100.0 * height);
                pts.Add(new Windows.Foundation.Point(x, y));
                fill.Add(new Windows.Foundation.Point(x, y));
                if (_msiFanPoints[i] != null)
                {
                    double r = _msiFanPoints[i].Width / 2.0;
                    Canvas.SetLeft(_msiFanPoints[i], x - r);
                    Canvas.SetTop(_msiFanPoints[i], y - r);
                }

                // Value label above the point (kept inside the canvas at the top edge).
                if (_msiFanValueLabels[i] != null)
                {
                    _msiFanValueLabels[i].Text = ((int)Math.Round(_msiFanCurve[i])).ToString();
                    double ly = y - 20;
                    if (ly < 0) ly = y + 8;               // flip below the point if too high
                    Canvas.SetLeft(_msiFanValueLabels[i], x - 8);
                    Canvas.SetTop(_msiFanValueLabels[i], ly);
                }
            }
            MsiFanCurvePolyline.Points = pts;
            fill.Add(new Windows.Foundation.Point(width, height));
            fill.Add(new Windows.Foundation.Point(0, height));
            MsiFanCurveFill.Points = fill;
        }

        private void MsiFanCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderMsiFanCurve();
            UpdateMsiFanGraphTemp(_msiFanLastTemp);
        }

        private double _msiFanLastTemp;

        /// <summary>
        /// Draws the live CPU package-temperature indicator (vertical line + label) on the fan
        /// graph, mapping 0…100 °C across the canvas width. Fed by the Quick Metrics cpuTemp feed.
        /// </summary>
        private void UpdateMsiFanGraphTemp(double tempC)
        {
            _msiFanLastTemp = tempC;
            if (MsiFanTempLabel != null)
                MsiFanTempLabel.Text = tempC > 0 ? $"{tempC:F0}°C" : "--°C";

            if (MsiFanTempIndicatorLine == null || MsiFanCurveCanvas == null) return;
            double width = MsiFanCurveCanvas.ActualWidth;
            double height = MsiFanCurveCanvas.ActualHeight;
            if (width <= 0 || height <= 0 || tempC <= 0)
            {
                MsiFanTempIndicatorLine.Visibility = Visibility.Collapsed;
                return;
            }
            double clamped = Math.Max(0, Math.Min(100, tempC));
            double x = (clamped / 100.0) * width;
            MsiFanTempIndicatorLine.X1 = x;
            MsiFanTempIndicatorLine.X2 = x;
            MsiFanTempIndicatorLine.Y1 = 0;
            MsiFanTempIndicatorLine.Y2 = height;
            MsiFanTempIndicatorLine.Visibility = Visibility.Visible;
        }

        private void MsiFanCurveCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (MsiFanCurveCanvas == null) return;
            var point = e.GetCurrentPoint(MsiFanCurveCanvas).Position;
            double minDist = double.MaxValue;
            int closest = -1;
            for (int i = 0; i < 11; i++)
            {
                if (_msiFanPoints[i] == null) continue;
                double px = Canvas.GetLeft(_msiFanPoints[i]) + 7;
                double py = Canvas.GetTop(_msiFanPoints[i]) + 7;
                double d = Math.Sqrt((point.X - px) * (point.X - px) + (point.Y - py) * (point.Y - py));
                if (d < minDist && d < 30) { minDist = d; closest = i; }
            }
            if (closest >= 0)
            {
                _msiFanDragIndex = closest;
                MsiFanCurveCanvas.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void MsiFanCurveCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_msiFanDragIndex < 0 || MsiFanCurveCanvas == null) return;
            var point = e.GetCurrentPoint(MsiFanCurveCanvas).Position;
            double height = MsiFanCurveCanvas.ActualHeight;
            if (height <= 0) return;
            double fanSpeed = (1.0 - point.Y / height) * 100.0;
            _msiFanCurve[_msiFanDragIndex] = Math.Max(0, Math.Min(100, Math.Round(fanSpeed)));
            RenderMsiFanCurve();
            e.Handled = true;
        }

        private void MsiFanCurveCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_msiFanDragIndex >= 0 && MsiFanCurveCanvas != null)
            {
                MsiFanCurveCanvas.ReleasePointerCapture(e.Pointer);
                _msiFanDragIndex = -1;

                // A manual edit means the curve is now "Custom".
                _msiFanInitializing = true;
                try { if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.SelectedIndex = 4; }
                finally { _msiFanInitializing = false; }

                ApplicationData.Current.LocalSettings.Values[MsiFanPresetKey] = 4;
                ApplicationData.Current.LocalSettings.Values[MsiFanCurveKey] = CurveToCsv();

                if (MsiFanEnableToggle?.IsOn == true)
                    SendMsiFanCurveToHelper();
            }
            e.Handled = true;
        }

        private void MsiFanEnableToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiFanInitializing) return;
            bool on = MsiFanEnableToggle?.IsOn ?? false;
            if (MsiFanContent != null)
                MsiFanContent.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ApplicationData.Current.LocalSettings.Values[MsiFanEnabledKey] = on;
            if (on) RenderMsiFanCurve();
            SendMsiFanStateToHelper();
        }

        private void MsiFanPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_msiFanInitializing) return;
            int idx = MsiFanPresetComboBox?.SelectedIndex ?? 1;
            if (idx < 0) idx = 1;
            ApplicationData.Current.LocalSettings.Values[MsiFanPresetKey] = idx;

            LoadCurveForPreset(idx);
            RenderMsiFanCurve();
            SendMsiFanStateToHelper();
        }

        /// <summary>
        /// Sends the current fan state to the helper. For a built-in preset (0/1/2) sends the
        /// preset index; for "Custom" (4) sends the full curve; disabled sends -1 (firmware).
        /// </summary>
        private async void SendMsiFanStateToHelper()
        {
            try
            {
                if (!App.IsConnected || !IsMsiClawDevice()) return;

                bool enabled = MsiFanEnableToggle?.IsOn ?? false;
                if (!enabled)
                {
                    await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanControl", -1 } });
                    Logger.Info("SendMsiFanStateToHelper: disabled -> firmware control (-1)");
                    AutoVerifyAfterApply();
                    return;
                }

                int preset = MsiFanPresetComboBox?.SelectedIndex ?? 1;
                if (preset == 4)
                {
                    SendMsiFanCurveToHelper();
                    return;
                }

                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanControl", preset } });
                Logger.Info($"SendMsiFanStateToHelper: preset={preset}");
                AutoVerifyAfterApply();
            }
            catch (Exception ex)
            {
                Logger.Error($"SendMsiFanStateToHelper: {ex.Message}");
            }
        }

        // ── D-Pad navigation: hook the fan card into the Performance-tab spine ──────
        // Up from the enable toggle → overlay combo (previous spine element).
        // Down → preset combo when the card is expanded, else loop to top.
        private void MsiFanEnableToggle_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                var target = (Windows.UI.Xaml.Controls.Control)PerformanceOverlayComboBox ?? CPUBoostToggle;
                target?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                if ((MsiFanEnableToggle?.IsOn ?? false) && MsiFanPresetComboBox != null)
                    MsiFanPresetComboBox.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else
                    (PerGameProfileToggle ?? (Windows.UI.Xaml.Controls.Control)FPSLimitToggle)?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        private void MsiFanPresetComboBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (MsiFanPresetComboBox?.IsDropDownOpen == true) return; // let the open dropdown handle keys

            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                MsiFanEnableToggle?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                // Down → into the curve graph so the points can be edited with the controller.
                if (MsiFanCurveFocus != null)
                    MsiFanCurveFocus.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else
                    (PerGameProfileToggle ?? (Windows.UI.Xaml.Controls.Control)FPSLimitToggle)?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        // ── Controller editing of the curve graph ──────────────────────────────────
        // Focus the canvas → a point is selected (left/right to move between points).
        // A / Enter grabs the selected point → up/down change its value; A / B releases.
        private int _msiFanSelectedPoint = -1;
        private bool _msiFanGrabbed;

        private void MsiFanCurveCanvas_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_msiFanSelectedPoint < 0) _msiFanSelectedPoint = 0;
            _msiFanGrabbed = false;
            HighlightMsiFanPoints();
            // Make sure the whole graph (and the temp label below it) is scrolled into view.
            ScrollMsiFanCardIntoView();
        }

        private void MsiFanCurveCanvas_LostFocus(object sender, RoutedEventArgs e)
        {
            _msiFanGrabbed = false;
            // De-emphasise points when focus leaves the graph.
            for (int i = 0; i < 11; i++)
                if (_msiFanPoints[i] != null)
                {
                    _msiFanPoints[i].Fill = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 0, 170, 255));
                    _msiFanPoints[i].Width = _msiFanPoints[i].Height = 14;
                }
        }

        private void MsiFanCurveCanvas_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var k = e.Key;
            bool isA = k == Windows.System.VirtualKey.GamepadA || k == Windows.System.VirtualKey.Enter || k == Windows.System.VirtualKey.Space;
            bool isB = k == Windows.System.VirtualKey.GamepadB || k == Windows.System.VirtualKey.Escape;
            bool up = k == Windows.System.VirtualKey.Up || k == Windows.System.VirtualKey.GamepadDPadUp;
            bool down = k == Windows.System.VirtualKey.Down || k == Windows.System.VirtualKey.GamepadDPadDown;
            bool left = k == Windows.System.VirtualKey.Left || k == Windows.System.VirtualKey.GamepadDPadLeft;
            bool right = k == Windows.System.VirtualKey.Right || k == Windows.System.VirtualKey.GamepadDPadRight;

            if (_msiFanGrabbed)
            {
                if (up || down)
                {
                    int idx = Math.Max(0, Math.Min(10, _msiFanSelectedPoint));
                    double step = up ? 5 : -5;
                    _msiFanCurve[idx] = Math.Max(0, Math.Min(100, _msiFanCurve[idx] + step));
                    RenderMsiFanCurve();
                    HighlightMsiFanPoints();
                    e.Handled = true;
                }
                else if (isA || isB)
                {
                    // Release → commit as a Custom curve (same as a pointer drag release).
                    _msiFanGrabbed = false;
                    CommitMsiFanCustomEdit();
                    HighlightMsiFanPoints();
                    e.Handled = true;
                }
                return;
            }

            // Navigating points (not grabbed)
            if (left)  { _msiFanSelectedPoint = Math.Max(0, _msiFanSelectedPoint - 1); HighlightMsiFanPoints(); e.Handled = true; }
            else if (right) { _msiFanSelectedPoint = Math.Min(10, _msiFanSelectedPoint + 1); HighlightMsiFanPoints(); e.Handled = true; }
            else if (isA) { _msiFanGrabbed = true; HighlightMsiFanPoints(); e.Handled = true; }
            else if (up)
            {
                // Leave the graph upward to the preset dropdown.
                MsiFanPresetComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (down)
            {
                // Down → the Check button below the graph.
                if (MsiFanCheckButton != null)
                    MsiFanCheckButton.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else
                    (PerGameProfileToggle ?? (Windows.UI.Xaml.Controls.Control)FPSLimitToggle)?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        // ── Check / verify applied EC values against the graph ──────────────────────
        private void MsiFanCheckButton_Click(object sender, RoutedEventArgs e) => VerifyMsiFan();

        private void MsiFanCheckButton_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                MsiFanCurveFocus?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                (PerGameProfileToggle ?? (Windows.UI.Xaml.Controls.Control)FPSLimitToggle)?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        // Must match MsiClawFanController.FanTableScale: the 0–100 % UI curve maps onto the full
        // MSI EC range 0–150, so each EC byte = percent × 1.5. Keep these in sync.
        private const double MsiFanTableScale = 150.0;

        /// <summary>Maps the current 11-point graph curve to the 8-byte EC table — byte-for-byte the
        /// same mapping the helper uses when writing (MsiClawFanController.CurveToTable): scale by
        /// 150/100 and TRUNCATE via the (byte) cast (NOT Math.Round). Rounding mismatched the top
        /// samples (e.g. 97 % → 145.5: truncates to 145 in the EC, rounding gave 146) and made the
        /// "Check applied values" report a false mismatch after the fan curve was scaled to 0–150.</summary>
        private byte[] MsiExpectedTable()
        {
            double scale = MsiFanTableScale / 100.0d;
            byte[] t = new byte[8];
            t[0] = (byte)(_msiFanCurve[4]  * scale);
            t[1] = (byte)(_msiFanCurve[0]  * scale);
            t[2] = (byte)(_msiFanCurve[2]  * scale);
            t[3] = (byte)(_msiFanCurve[5]  * scale);
            t[4] = (byte)(_msiFanCurve[6]  * scale);
            t[5] = (byte)(_msiFanCurve[8]  * scale);
            t[6] = (byte)(_msiFanCurve[9]  * scale);
            t[7] = (byte)(_msiFanCurve[10] * scale);
            return t;
        }

        private async void VerifyMsiFan()
        {
            try
            {
                if (MsiFanCheckStatus != null)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 160, 160, 160));
                    MsiFanCheckStatus.Text = "Checking applied values…";
                }
                if (!App.IsConnected)
                {
                    if (MsiFanCheckStatus != null)
                    {
                        MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 230, 120, 120));
                        MsiFanCheckStatus.Text = "Helper not connected.";
                    }
                    return;
                }
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanVerify", true } });
                Logger.Info("VerifyMsiFan: requested EC read-back");
            }
            catch (Exception ex)
            {
                Logger.Error($"VerifyMsiFan: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the helper's "MsiFanStatus" push: "b0,..,b7|controlBit|readOk".
        /// Compares the read-back EC table against the graph and shows a status.
        /// </summary>
        internal void OnMsiFanStatus(string payload)
        {
            try
            {
                if (MsiFanCheckStatus == null || string.IsNullOrEmpty(payload)) return;

                var sections = payload.Split('|');
                var ecParts = sections[0].Split(',');
                bool controlOn = sections.Length > 1 && sections[1] == "1";
                bool readOk = sections.Length > 2 && sections[2] == "1";
                bool fullSpeed = sections.Length > 3 && sections[3] == "1";
                int rpm = -1;
                if (sections.Length > 4) int.TryParse(sections[4], out rpm);

                // Measurement line for the scaling test: actual fan RPM + whether the EC full-speed
                // override is engaged. Lets us compare our table max (100 % = EC 150) against true max.
                string measure = (rpm >= 0 ? $"Fan: {rpm} RPM" : "Fan: n/a")
                                 + $" · full-speed override: {(fullSpeed ? "ON" : "off")}";
                if (FanFullBlastStatusText != null) FanFullBlastStatusText.Text = measure;

                byte[] ec = new byte[8];
                for (int i = 0; i < 8 && i < ecParts.Length; i++)
                    byte.TryParse(ecParts[i], out ec[i]);

                byte[] expected = MsiExpectedTable();
                // Compare bytes 1..7 only. Byte 0 is the EC-managed low-temp/idle "backup"
                // sample (≈40 °C) that the MSI firmware nudges on its own (e.g. 0↔12), which
                // produced spurious "mismatch" warnings even though the curve is applied
                // correctly. It has no meaningful effect at idle, so we don't flag it.
                bool match = true;
                for (int i = 1; i < 8; i++) if (ec[i] != expected[i]) { match = false; break; }

                bool enabled = MsiFanEnableToggle?.IsOn ?? false;
                int preset = MsiFanPresetComboBox?.SelectedIndex ?? -1;

                if (!readOk)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 230, 120, 120));
                    MsiFanCheckStatus.Text = "✗ Could not read fan values from the EC.";
                }
                else if (!enabled)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 200, 200, 200));
                    MsiFanCheckStatus.Text = $"Custom fan curve is OFF (firmware control). EC table: [{string.Join(",", ec)}], control bit: {(controlOn ? "on" : "off")}.";
                }
                else if (preset == 5)
                {
                    // "Cooling (EC Sport)" is a hardware-mode preset: the helper writes the
                    // BestPerformance EC table and hands fan control BACK to the firmware
                    // (SetFanControl(false)). The control bit being OFF is therefore correct —
                    // not a mismatch. The EC table comparison against the software-curve
                    // placeholder would always fail here, so we skip it entirely.
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 120, 210, 120));
                    MsiFanCheckStatus.Text = $"✓ EC Sport applied — firmware controls the fan (hardware mode, control bit off is correct).\nEC table: [{string.Join(",", ec)}]";
                }
                else if (match && controlOn)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 120, 210, 120));
                    MsiFanCheckStatus.Text = $"✓ Applied correctly — EC matches the graph and control is active.\nEC: [{string.Join(",", ec)}]";
                }
                else
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 240, 180, 80));
                    string why = !controlOn ? "control bit is OFF" : "EC values differ from the graph";
                    MsiFanCheckStatus.Text = $"⚠ Mismatch ({why}).\nEC: [{string.Join(",", ec)}]\nExpected: [{string.Join(",", expected)}]";
                }

                // Always show the live measurement so the EC check doubles as an RPM read-out.
                MsiFanCheckStatus.Text += "\n" + measure;
            }
            catch (Exception ex)
            {
                Logger.Error($"OnMsiFanStatus: {ex.Message}");
            }
        }

        // ── Experimental: Intel thermal stack (IPF/DTT) control ─────────────────────
        // On Lunar Lake the Intel Innovation Platform Framework owns a fan participant above the EC
        // and can latch the fan at max under sustained load. These let a tester stop the Intel
        // thermal tasks (so the EC table is the sole fan owner) and start them again, with a status.

        private void IntelThermalStopButton_Click(object sender, RoutedEventArgs e) => SendIntelThermalCmd("stop");
        private void IntelThermalStartButton_Click(object sender, RoutedEventArgs e) => SendIntelThermalCmd("start");
        private void IntelThermalRefreshButton_Click(object sender, RoutedEventArgs e) => RequestIntelThermalStatus();

        /// <summary>Ask the helper for the current Intel thermal stack status (no state change).</summary>
        internal void RequestIntelThermalStatus() => SendIntelThermalCmd("status");

        private async void SendIntelThermalCmd(string cmd)
        {
            try
            {
                if (!App.IsConnected)
                {
                    if (IntelThermalStatusText != null)
                    {
                        IntelThermalStatusText.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 230, 120, 120));
                        IntelThermalStatusText.Text = "Helper not connected.";
                    }
                    return;
                }
                if (cmd != "status" && IntelThermalStatusText != null)
                {
                    IntelThermalStatusText.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 160, 160, 160));
                    IntelThermalStatusText.Text = cmd == "stop" ? "Stopping Intel thermal tasks…" : "Starting Intel thermal tasks…";
                }
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "IntelThermalCmd", cmd } });
                Logger.Info($"SendIntelThermalCmd: '{cmd}'");
            }
            catch (Exception ex)
            {
                Logger.Error($"SendIntelThermalCmd('{cmd}'): {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the helper's "IntelThermalStatus" push: "&lt;state&gt;|&lt;detail&gt;" where state is
        /// running / stopped / partial / error. Running = normal (Intel owns the fan); stopped =
        /// test mode (EC is the sole fan owner).
        /// </summary>
        internal void OnIntelThermalStatus(string payload)
        {
            try
            {
                if (IntelThermalStatusText == null || string.IsNullOrEmpty(payload)) return;

                var sections = payload.Split(new[] { '|' }, 2);
                string state = sections[0];
                string detail = sections.Length > 1 ? sections[1] : "";

                Windows.UI.Color color;
                string label;
                switch (state)
                {
                    case "running":
                        color = Windows.UI.ColorHelper.FromArgb(255, 120, 190, 240); // blue: Intel active (normal)
                        label = "Intel thermal tasks RUNNING (normal).";
                        break;
                    case "stopped":
                        color = Windows.UI.ColorHelper.FromArgb(255, 240, 180, 80); // orange: test mode
                        label = "Intel thermal tasks STOPPED — EC is the sole fan owner (test mode).";
                        break;
                    case "partial":
                        color = Windows.UI.ColorHelper.FromArgb(255, 240, 180, 80);
                        label = "Intel thermal tasks PARTIALLY running.";
                        break;
                    default:
                        color = Windows.UI.ColorHelper.FromArgb(255, 230, 120, 120); // red
                        label = "Could not read Intel thermal status.";
                        break;
                }

                IntelThermalStatusText.Foreground = new SolidColorBrush(color);
                IntelThermalStatusText.Text = string.IsNullOrEmpty(detail) ? label : $"{label}\n{detail}";

                if (IntelThermalStopButton != null)  IntelThermalStopButton.IsEnabled  = state != "stopped";
                if (IntelThermalStartButton != null) IntelThermalStartButton.IsEnabled = state != "running";
            }
            catch (Exception ex)
            {
                Logger.Error($"OnIntelThermalStatus: {ex.Message}");
            }
        }

        // ── Diagnostic: fan max test (full-speed override) + RPM read ───────────────
        // Compares our table max (100 % = EC byte 150) against the EC's true full-speed ceiling
        // (block 152.7). If Full Blast is audibly/RPM-wise louder than Aggressive@100 %, then 150 is
        // NOT the absolute max and our 0-100 % scaling tops out below the hardware ceiling.

        private void FanFullBlastOnButton_Click(object sender, RoutedEventArgs e) => SendFanFullBlast("on");
        private void FanFullBlastOffButton_Click(object sender, RoutedEventArgs e) => SendFanFullBlast("off");

        /// <summary>Re-read EC fan status incl. live RPM (reuses the EC verify path).</summary>
        private async void FanReadRpmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!App.IsConnected) { if (FanFullBlastStatusText != null) FanFullBlastStatusText.Text = "Helper not connected."; return; }
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanVerify", true } });
            }
            catch (Exception ex) { Logger.Error($"FanReadRpm: {ex.Message}"); }
        }

        private async void SendFanFullBlast(string cmd)
        {
            try
            {
                if (!App.IsConnected) { if (FanFullBlastStatusText != null) FanFullBlastStatusText.Text = "Helper not connected."; return; }
                if (FanFullBlastStatusText != null)
                    FanFullBlastStatusText.Text = cmd == "on"
                        ? "Full Blast ON — wait a few seconds, then Read RPM."
                        : "Full Blast off — wait a few seconds, then Read RPM.";
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanFullBlast", cmd } });
                Logger.Info($"SendFanFullBlast: '{cmd}'");
            }
            catch (Exception ex) { Logger.Error($"SendFanFullBlast('{cmd}'): {ex.Message}"); }
        }

        // ── Diagnostic: fan-override register probe ─────────────────────────────────
        // Hunts for a PROPORTIONAL fan-duty register. The full-speed bit (152.7) proves a direct
        // override exists; this writes raw bytes to a chosen EC block and reads them back so we can
        // listen for a level response (between firmware-quiet and Full-Blast).

        private void FanProbeValueSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (FanProbeValueLabel != null)
            {
                int v = (int)Math.Round(e.NewValue);
                FanProbeValueLabel.Text = $"{v} (0x{v:X2})";
            }
        }

        private void FanProbeWriteButton_Click(object sender, RoutedEventArgs e)
        {
            int block = 152;
            if (FanProbeBlockText != null) int.TryParse(FanProbeBlockText.Text?.Trim(), out block);
            int value = (int)Math.Round(FanProbeValueSlider?.Value ?? 0);
            SendFanRegProbe(block, value);
        }

        // Quick presets on block 152 covering the key hypotheses (raw level vs. enable-bit+low7).
        private void FanProbeP0_Click(object sender, RoutedEventArgs e)   => SendFanRegProbe(152, 0);
        private void FanProbeP50_Click(object sender, RoutedEventArgs e)  => SendFanRegProbe(152, 50);
        private void FanProbeP100_Click(object sender, RoutedEventArgs e) => SendFanRegProbe(152, 100);
        private void FanProbeP150_Click(object sender, RoutedEventArgs e) => SendFanRegProbe(152, 150);
        private void FanProbeEn40_Click(object sender, RoutedEventArgs e) => SendFanRegProbe(152, 0x80 | 40);
        private void FanProbeEn80_Click(object sender, RoutedEventArgs e) => SendFanRegProbe(152, 0x80 | 80);

        private async void SendFanRegProbe(int block, int value)
        {
            try
            {
                if (!App.IsConnected) { if (FanProbeStatusText != null) FanProbeStatusText.Text = "Helper not connected."; return; }
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanRegProbe", $"{block},{value}" } });
                Logger.Info($"SendFanRegProbe: block={block} value={value}");
            }
            catch (Exception ex) { Logger.Error($"SendFanRegProbe({block},{value}): {ex.Message}"); }
        }

        /// <summary>Handles "MsiFanRegStatus":"block|wrote|readback|rpm" — shows what landed in the EC.</summary>
        internal void OnFanRegStatus(string payload)
        {
            try
            {
                if (FanProbeStatusText == null || string.IsNullOrEmpty(payload)) return;
                var p = payload.Split('|');
                string block = p.Length > 0 ? p[0] : "?";
                string wrote = p.Length > 1 ? p[1] : "?";
                string readback = p.Length > 2 ? p[2] : "?";
                int rpm = -1; if (p.Length > 3) int.TryParse(p[3], out rpm);
                int.TryParse(wrote, out int w);
                int.TryParse(readback, out int r);
                string rpmStr = rpm >= 0 ? $"{rpm} RPM" : "RPM n/a";
                FanProbeStatusText.Text = $"block {block}: wrote {w} (0x{w:X2}), read back {r} (0x{r:X2}) · {rpmStr}";
            }
            catch (Exception ex) { Logger.Error($"OnFanRegStatus: {ex.Message}"); }
        }

        /// <summary>Color the selected point (yellow = selected, orange = grabbed); others blue.</summary>
        private void HighlightMsiFanPoints()
        {
            for (int i = 0; i < 11; i++)
            {
                if (_msiFanPoints[i] == null) continue;
                bool sel = i == _msiFanSelectedPoint;
                Windows.UI.Color c = sel
                    ? (_msiFanGrabbed ? Windows.UI.ColorHelper.FromArgb(255, 255, 140, 0)   // orange (grabbed)
                                      : Windows.UI.ColorHelper.FromArgb(255, 255, 215, 0))   // yellow (selected)
                    : Windows.UI.ColorHelper.FromArgb(255, 0, 170, 255);                     // blue
                _msiFanPoints[i].Fill = new SolidColorBrush(c);
                _msiFanPoints[i].Width = _msiFanPoints[i].Height = sel ? 18 : 14;
                if (_msiFanValueLabels[i] != null)
                {
                    _msiFanValueLabels[i].Foreground = new SolidColorBrush(sel ? c : Windows.UI.Colors.White);
                    _msiFanValueLabels[i].FontWeight = sel ? Windows.UI.Text.FontWeights.SemiBold : Windows.UI.Text.FontWeights.Normal;
                }
            }
            RenderMsiFanCurve(); // re-center the (now larger) selected ellipse
        }

        /// <summary>Persist + push a controller/touch edit as a Custom curve.</summary>
        private void CommitMsiFanCustomEdit()
        {
            _msiFanInitializing = true;
            try { if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.SelectedIndex = 4; }
            finally { _msiFanInitializing = false; }

            ApplicationData.Current.LocalSettings.Values[MsiFanPresetKey] = 4;
            ApplicationData.Current.LocalSettings.Values[MsiFanCurveKey] = CurveToCsv();
            if (MsiFanEnableToggle?.IsOn == true)
                SendMsiFanCurveToHelper();
        }

        /// <summary>Scroll the Fan tab so the entire fan graph + temp label are visible.</summary>
        private void ScrollMsiFanCardIntoView()
        {
            try
            {
                if (FanScrollViewer == null) return;
                // The fan card is the last content — scroll fully to the bottom.
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                {
                    FanScrollViewer.UpdateLayout();
                    FanScrollViewer.ChangeView(null, FanScrollViewer.ScrollableHeight, null);
                });
            }
            catch (Exception ex) { Logger.Debug($"ScrollMsiFanCardIntoView: {ex.Message}"); }
        }

        private async void SendMsiFanCurveToHelper()
        {
            try
            {
                if (!App.IsConnected || !IsMsiClawDevice()) return;
                string csv = CurveToCsv();
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanCurve", csv } });
                Logger.Info($"SendMsiFanCurveToHelper: '{csv}'");
                AutoVerifyAfterApply();
            }
            catch (Exception ex)
            {
                Logger.Error($"SendMsiFanCurveToHelper: {ex.Message}");
            }
        }

        /// <summary>
        /// After an apply, wait briefly for the helper to write the EC, then auto-run the
        /// verification so the status reflects reality without the user clicking Check.
        /// </summary>
        private async void AutoVerifyAfterApply()
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(500);
                VerifyMsiFan();
            }
            catch (Exception ex)
            {
                Logger.Debug($"AutoVerifyAfterApply: {ex.Message}");
            }
        }
    }
}
