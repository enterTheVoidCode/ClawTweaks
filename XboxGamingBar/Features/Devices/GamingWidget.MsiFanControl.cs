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
        //                                                 0  10  20  30  40  50  60  70  80   90  100 °C
        private static readonly double[] MsiCurveQuiet      = { 0, 0, 0,  0,  0, 12, 22, 38, 58,  80, 100 };
        private static readonly double[] MsiCurveDefault    = { 0, 0, 0,  0,  5, 20, 40, 62, 82, 100, 100 };
        private static readonly double[] MsiCurveAggressive = { 0, 0, 0,  5, 15, 35, 55, 75, 90, 100, 100 };

        private readonly double[] _msiFanCurve = (double[])MsiCurveDefault.Clone();
        private readonly Ellipse[] _msiFanPoints = new Ellipse[11];
        private bool _msiFanPointsBuilt;
        private bool _msiFanInitializing;
        private int _msiFanDragIndex = -1;

        private bool IsMsiClawDevice()
            => deviceDisplayName?.Value?.IndexOf("Claw", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Show the fan card on MSI Claw and restore the saved state. Idempotent.</summary>
        private void InitializeMsiFanCard()
        {
            if (MsiFanCard == null) return;

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
                if (preset < 0 || preset > 3) preset = 1;

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
            SendMsiFanStateToHelper();
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
                case 3: src = LoadCustomCurveFromStorage() ?? MsiCurveDefault; break;
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
                    Canvas.SetLeft(_msiFanPoints[i], x - 7);
                    Canvas.SetTop(_msiFanPoints[i], y - 7);
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
                try { if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.SelectedIndex = 3; }
                finally { _msiFanInitializing = false; }

                ApplicationData.Current.LocalSettings.Values[MsiFanPresetKey] = 3;
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
        /// preset index; for "Custom" (3) sends the full curve; disabled sends -1 (firmware).
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
                    return;
                }

                int preset = MsiFanPresetComboBox?.SelectedIndex ?? 1;
                if (preset == 3)
                {
                    SendMsiFanCurveToHelper();
                    return;
                }

                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanControl", preset } });
                Logger.Info($"SendMsiFanStateToHelper: preset={preset}");
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

        /// <summary>Maps the current 11-point graph curve to the 8-byte EC table (same mapping as the helper).</summary>
        private byte[] MsiExpectedTable()
        {
            byte[] t = new byte[8];
            t[0] = (byte)Math.Round(_msiFanCurve[4]);
            t[1] = (byte)Math.Round(_msiFanCurve[0]);
            t[2] = (byte)Math.Round(_msiFanCurve[2]);
            t[3] = (byte)Math.Round(_msiFanCurve[5]);
            t[4] = (byte)Math.Round(_msiFanCurve[6]);
            t[5] = (byte)Math.Round(_msiFanCurve[8]);
            t[6] = (byte)Math.Round(_msiFanCurve[9]);
            t[7] = (byte)Math.Round(_msiFanCurve[10]);
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

                byte[] ec = new byte[8];
                for (int i = 0; i < 8 && i < ecParts.Length; i++)
                    byte.TryParse(ecParts[i], out ec[i]);

                byte[] expected = MsiExpectedTable();
                bool match = true;
                for (int i = 0; i < 8; i++) if (ec[i] != expected[i]) { match = false; break; }

                bool enabled = MsiFanEnableToggle?.IsOn ?? false;

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
            }
            catch (Exception ex)
            {
                Logger.Error($"OnMsiFanStatus: {ex.Message}");
            }
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
            }
            RenderMsiFanCurve(); // re-center the (now larger) selected ellipse
        }

        /// <summary>Persist + push a controller/touch edit as a Custom curve.</summary>
        private void CommitMsiFanCustomEdit()
        {
            _msiFanInitializing = true;
            try { if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.SelectedIndex = 3; }
            finally { _msiFanInitializing = false; }

            ApplicationData.Current.LocalSettings.Values[MsiFanPresetKey] = 3;
            ApplicationData.Current.LocalSettings.Values[MsiFanCurveKey] = CurveToCsv();
            if (MsiFanEnableToggle?.IsOn == true)
                SendMsiFanCurveToHelper();
        }

        /// <summary>Scroll the Performance tab so the entire fan graph + temp label are visible.</summary>
        private void ScrollMsiFanCardIntoView()
        {
            try
            {
                if (PerformanceScrollViewer == null) return;
                // The fan card is the last content — scroll fully to the bottom.
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                {
                    PerformanceScrollViewer.UpdateLayout();
                    PerformanceScrollViewer.ChangeView(null, PerformanceScrollViewer.ScrollableHeight, null);
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
            }
            catch (Exception ex)
            {
                Logger.Error($"SendMsiFanCurveToHelper: {ex.Message}");
            }
        }
    }
}
