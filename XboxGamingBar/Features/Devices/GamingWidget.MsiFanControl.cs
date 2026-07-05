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
        // Keys bumped to *_2 for the MSI-axis rework: the old ×1.5 / wrong-axis 11-point settings are
        // ignored → existing user fan settings are wiped clean.
        private const string MsiFanEnabledKey  = "MsiFan_Enabled2";
        private const string MsiFanPresetKey   = "MsiFan_Preset2";   // 0=Default 1=Quiet Idle 2=Cooling 3=Custom
        private const string MsiFanCurveKey    = "MsiFan_CurveCsv2"; // "t1,..,t5;d1,..,d5" when custom
        private const string MsiFanExtendedKey = "MsiFan_Extended2"; // allow duty >75% (beyond MSI)

        // ── MSI fan model: 5 (temp,duty) points on the real firmware axis ────────────────
        // MUST match MsiClawFanController on the helper side. Temps default to the MSI Center M
        // breakpoints [44,54,64,74,82]; duty is the RAW EC byte 0–100 (MSI scale, no ×1.5).
        internal const int MsiFanPoints = 5;
        private static readonly int[] MsiTempsDefault = { 44, 54, 64, 74, 82 };
        private static readonly int[] MsiTempsCooling = { 34, 44, 54, 64, 72 }; // −10 °C early ramp

        private static readonly int[] MsiDutyDefault   = { 40, 49, 58, 67, 75 };
        private static readonly int[] MsiDutyQuietIdle = { 20, 30, 45, 67, 75 };
        private static readonly int[] MsiDutyCooling   = { 40, 49, 58, 67, 75 };
        // DEBUG preset "EC Sport default": the old firmware BestPerformance table [10,0,10,26,46,78,113,
        // 150] (raw EC bytes, i.e. already the new % scale — the old ×1.5 applied only to software
        // curves, not this hardware table) sampled onto the MSI breakpoints for DISPLAY. The helper
        // actually writes the exact raw firmware table + Sport (firmware drives); this is only the graph.
        private static readonly int[] MsiDutyEcSport   = { 23, 34, 52, 68, 85 };

        // Breakpoint temps are freely editable across the whole scale (only bounded globally + kept
        // strictly increasing) so the user can spread the curve over the full 0–100 °C axis.
        private const int MsiTempMin = 10;
        private const int MsiTempMax = 99;

        // MSI's own curves cap at 75 %. The >75 % ("beyond MSI") range is opt-in via a toggle.
        private const int MsiDutyCap = 75;
        private bool _msiFanExtended;
        private int MsiDutyMax() => _msiFanExtended ? 100 : MsiDutyCap;

        private readonly int[] _msiFanTemps = (int[])MsiTempsDefault.Clone();
        private readonly int[] _msiFanDuties = (int[])MsiDutyDefault.Clone();
        // MSI-style fixed evenly-spaced BARS (not a positional temperature axis). Bar height = fan %
        // (vertical edit via the circle on top). The temperature is shown as a label UNDER each bar
        // (horizontal edit). Horizontal %-gridlines + Y labels give the scale.
        private static readonly int[] MsiGridPct = { 0, 25, 50, 75, 100 };
        private static readonly uint[] MsiGridColor = { 0x6FB7FF, 0x8FD06A, 0xE6C84A, 0xF0A030, 0xF0603C };
        private readonly Windows.UI.Xaml.Shapes.Rectangle[] _msiFanBars = new Windows.UI.Xaml.Shapes.Rectangle[MsiFanPoints];
        private readonly Ellipse[] _msiFanPoints = new Ellipse[MsiFanPoints];        // duty circle (top of bar)
        private readonly TextBlock[] _msiFanValueLabels = new TextBlock[MsiFanPoints]; // "%" above the bar
        private readonly TextBlock[] _msiFanTempLabels = new TextBlock[MsiFanPoints];  // "44°C" under the bar
        // Temp focus markers: a rotated square (diamond) under each temp label whose left/right points
        // signal the handle moves horizontally. This is the controller-reachable temp handle.
        private readonly Windows.UI.Xaml.Shapes.Rectangle[] _msiFanTempHandles = new Windows.UI.Xaml.Shapes.Rectangle[MsiFanPoints];
        private readonly Line[] _msiFanGridLines = new Line[5];
        private readonly TextBlock[] _msiFanGridLabels = new TextBlock[5];
        private bool _msiFanPointsBuilt;
        private bool _msiFanInitializing;
        private int _msiFanDragIndex = -1;
        private bool _msiFanDragIsTemp;  // mouse drag target is a temp label (horizontal), not a duty bar

        /// <summary>Clamp a temp to [MsiTempMin, MsiTempMax] and keep it strictly between its neighbours
        /// so the axis stays monotonic. No per-point anchor limit — the whole scale is usable.</summary>
        private int ClampMsiTemp(int idx, int value)
        {
            int lo = MsiTempMin, hi = MsiTempMax;
            if (idx > 0) lo = Math.Max(lo, _msiFanTemps[idx - 1] + 1);
            if (idx < MsiFanPoints - 1) hi = Math.Min(hi, _msiFanTemps[idx + 1] - 1);
            return Math.Max(lo, Math.Min(hi, value));
        }

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
                int preset = (settings.Values.TryGetValue(MsiFanPresetKey, out var pObj) && pObj is int p) ? p : 0;
                if (preset < 0 || preset > 4) preset = 0;
                _msiFanExtended = settings.Values.TryGetValue(MsiFanExtendedKey, out var exObj) && exObj is bool ex && ex;

                // Restore the curve for the selected preset (custom from storage; presets from constants).
                LoadCurveForPreset(preset);

                if (MsiFanEnableToggle != null) MsiFanEnableToggle.IsOn = enabled;
                if (MsiFanExtendedRangeToggle != null) MsiFanExtendedRangeToggle.IsOn = _msiFanExtended;
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
        /// Payload: "&lt;value&gt;|&lt;curveCsv&gt;" — value -1=disabled (firmware), 0=MSI Default,
        /// 1=Quiet Idle, 2=Cooling / early ramp, 3=Custom ("t1,..,t5;d1,..,d5").
        /// </summary>
        internal void OnMsiFanState(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            var parts = payload.Split('|');
            if (!int.TryParse(parts[0], out int value)) return;
            string curve = parts.Length > 1 ? parts[1] : "";

            bool enabled = value >= 0;
            int preset = (value >= 0 && value <= 4) ? value : 0;

            _msiFanInitializing = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[MsiFanEnabledKey] = enabled;
                if (value >= 0) settings.Values[MsiFanPresetKey] = preset;
                if (value == 3 && !string.IsNullOrEmpty(curve))
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

            // Horizontal %-gridlines + Y labels (0/25/50/75/100), drawn behind the bars.
            for (int g = 0; g < 5; g++)
            {
                var gl = new Line
                {
                    Stroke = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(38, 255, 255, 255)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetZIndex(gl, -1);
                _msiFanGridLines[g] = gl;
                MsiFanCurveCanvas.Children.Add(gl);

                uint c = MsiGridColor[g];
                var glab = new TextBlock
                {
                    FontSize = 13,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c)),
                    IsHitTestVisible = false,
                    Text = $"{MsiGridPct[g]}%"
                };
                _msiFanGridLabels[g] = glab;
                MsiFanCurveCanvas.Children.Add(glab);
            }

            for (int i = 0; i < MsiFanPoints; i++)
            {
                // The bar (height = fan %). Vertical gradient blue→cyan.
                var bar = new Windows.UI.Xaml.Shapes.Rectangle
                {
                    RadiusX = 3,
                    RadiusY = 3,
                    IsHitTestVisible = false,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Windows.Foundation.Point(0, 0),
                        EndPoint = new Windows.Foundation.Point(0, 1),
                        GradientStops =
                        {
                            new GradientStop { Color = Windows.UI.ColorHelper.FromArgb(235, 0, 190, 255), Offset = 0 },
                            new GradientStop { Color = Windows.UI.ColorHelper.FromArgb(200, 0, 120, 210), Offset = 1 }
                        }
                    }
                };
                _msiFanBars[i] = bar;
                MsiFanCurveCanvas.Children.Add(bar);

                // Duty circle (grab handle on top of the bar) — vertical edit.
                var ellipse = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 0, 170, 255)),
                    Stroke = new SolidColorBrush(Windows.UI.Colors.White),
                    StrokeThickness = 2,
                    Tag = i
                };
                Canvas.SetZIndex(ellipse, 10);
                _msiFanPoints[i] = ellipse;
                MsiFanCurveCanvas.Children.Add(ellipse);

                // Fan-% label above the bar.
                var label = new TextBlock
                {
                    FontSize = 15,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    IsHitTestVisible = false
                };
                Canvas.SetZIndex(label, 11);
                _msiFanValueLabels[i] = label;
                MsiFanCurveCanvas.Children.Add(label);

                // Temperature label UNDER the bar.
                var tlabel = new TextBlock
                {
                    FontSize = 14,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 220, 180, 90)),
                    IsHitTestVisible = false
                };
                Canvas.SetZIndex(tlabel, 11);
                _msiFanTempLabels[i] = tlabel;
                MsiFanCurveCanvas.Children.Add(tlabel);

                // Temp focus marker: a diamond (rotated square) under the temp label. Its left/right
                // points read as "moves horizontally". This is the temp handle.
                var diamond = new Windows.UI.Xaml.Shapes.Rectangle
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 255, 150, 40)),
                    Stroke = new SolidColorBrush(Windows.UI.Colors.White),
                    StrokeThickness = 1.5,
                    RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                    RenderTransform = new RotateTransform { Angle = 45 },
                    IsHitTestVisible = false,
                    Tag = i
                };
                Canvas.SetZIndex(diamond, 11);
                _msiFanTempHandles[i] = diamond;
                MsiFanCurveCanvas.Children.Add(diamond);
            }
            _msiFanPointsBuilt = true;
        }

        /// <summary>Load the 5-point (temp,duty) curve for a preset into the model arrays.</summary>
        private void LoadCurveForPreset(int preset)
        {
            int[] temps, duties;
            switch (preset)
            {
                case 1: temps = MsiTempsDefault; duties = MsiDutyQuietIdle; break;
                case 2: temps = MsiTempsCooling; duties = MsiDutyCooling;   break;
                case 3:
                    if (LoadCustomCurveFromStorage(out int[] ct, out int[] cd))
                    {
                        Array.Copy(ct, _msiFanTemps, MsiFanPoints);
                        Array.Copy(cd, _msiFanDuties, MsiFanPoints);
                        return;
                    }
                    temps = MsiTempsDefault; duties = MsiDutyDefault; break;
                case 4: temps = MsiTempsDefault; duties = MsiDutyEcSport; break; // debug: EC Sport default (display)
                default: temps = MsiTempsDefault; duties = MsiDutyDefault; break; // 0 = MSI Default
            }
            Array.Copy(temps, _msiFanTemps, MsiFanPoints);
            Array.Copy(duties, _msiFanDuties, MsiFanPoints);
        }

        private bool LoadCustomCurveFromStorage(out int[] temps, out int[] duties)
        {
            temps = null; duties = null;
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(MsiFanCurveKey, out var o)
                    && o is string csv)
                {
                    var halves = csv.Split(';');
                    if (halves.Length == 2)
                    {
                        var tp = halves[0].Split(',');
                        var dp = halves[1].Split(',');
                        if (tp.Length == MsiFanPoints && dp.Length == MsiFanPoints)
                        {
                            var t = new int[MsiFanPoints];
                            var d = new int[MsiFanPoints];
                            for (int i = 0; i < MsiFanPoints; i++)
                            {
                                t[i] = Math.Max(0, Math.Min(120, int.Parse(tp[i], CultureInfo.InvariantCulture)));
                                d[i] = Math.Max(0, Math.Min(100, int.Parse(dp[i], CultureInfo.InvariantCulture)));
                            }
                            temps = t; duties = d;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Debug($"LoadCustomCurveFromStorage: {ex.Message}"); }
            return false;
        }

        /// <summary>Serialize the model as "t1,..,t5;d1,..,d5" — the wire + storage format.</summary>
        private string CurveToCsv()
            => string.Join(",", _msiFanTemps.Select(v => v.ToString(CultureInfo.InvariantCulture)))
               + ";" + string.Join(",", _msiFanDuties.Select(v => v.ToString(CultureInfo.InvariantCulture)));

        // Plot padding: room above the bars for the % labels, below for the temp labels, left for the
        // Y-axis % labels.
        private const double MsiPlotTop = 26;
        private const double MsiPlotBottomPad = 48;   // room for the temp label + diamond focus marker
        private const double MsiPlotLeft = 40;

        private double MsiDutyToY(double duty, double plotTop, double plotBottom)
            => plotBottom - (duty / 100.0) * (plotBottom - plotTop);

        private void RenderMsiFanCurve()
        {
            if (MsiFanCurveCanvas == null) return;
            double width = MsiFanCurveCanvas.ActualWidth;
            double height = MsiFanCurveCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            double plotTop = MsiPlotTop;
            double plotBottom = height - MsiPlotBottomPad;
            double plotLeft = MsiPlotLeft;
            if (plotBottom <= plotTop) return;

            // Unused curve elements from the old line-graph layout.
            if (MsiFanCurvePolyline != null) MsiFanCurvePolyline.Visibility = Visibility.Collapsed;
            if (MsiFanCurveFill != null) MsiFanCurveFill.Visibility = Visibility.Collapsed;

            // Horizontal %-gridlines + Y labels.
            for (int g = 0; g < 5; g++)
            {
                double gy = MsiDutyToY(MsiGridPct[g], plotTop, plotBottom);
                if (_msiFanGridLines[g] != null)
                {
                    _msiFanGridLines[g].X1 = plotLeft; _msiFanGridLines[g].X2 = width;
                    _msiFanGridLines[g].Y1 = gy; _msiFanGridLines[g].Y2 = gy;
                }
                if (_msiFanGridLabels[g] != null)
                {
                    Canvas.SetLeft(_msiFanGridLabels[g], 2);
                    Canvas.SetTop(_msiFanGridLabels[g], gy - 9);
                }
            }

            // "Beyond MSI" zone (only when extended): shade above the 75 % line + dashed reference.
            double yMsiMax = MsiDutyToY(75, plotTop, plotBottom);
            var beyondVis = _msiFanExtended ? Visibility.Visible : Visibility.Collapsed;
            if (MsiFanBeyondBand != null)
            {
                MsiFanBeyondBand.Visibility = beyondVis;
                MsiFanBeyondBand.Width = Math.Max(0, width - plotLeft);
                MsiFanBeyondBand.Height = Math.Max(0, yMsiMax - plotTop);
                Canvas.SetLeft(MsiFanBeyondBand, plotLeft);
                Canvas.SetTop(MsiFanBeyondBand, plotTop);
            }
            if (MsiFanMsiMaxLine != null)
            {
                MsiFanMsiMaxLine.Visibility = beyondVis;
                Canvas.SetZIndex(MsiFanMsiMaxLine, 5);
                MsiFanMsiMaxLine.X1 = plotLeft; MsiFanMsiMaxLine.X2 = width;
                MsiFanMsiMaxLine.Y1 = yMsiMax; MsiFanMsiMaxLine.Y2 = yMsiMax;
            }
            if (MsiFanMsiMaxLabel != null)
            {
                MsiFanMsiMaxLabel.Visibility = beyondVis;
                Canvas.SetZIndex(MsiFanMsiMaxLabel, 5);
                Canvas.SetLeft(MsiFanMsiMaxLabel, plotLeft + 4);
                Canvas.SetTop(MsiFanMsiMaxLabel, Math.Max(plotTop, yMsiMax - 14));
            }

            // Evenly-spaced bars across the plot area (temperature is NOT positional; it's the label
            // under each bar).
            double plotW = width - plotLeft;
            double slot = plotW / MsiFanPoints;
            double barW = slot * 0.46;
            for (int i = 0; i < MsiFanPoints; i++)
            {
                double cx = plotLeft + (i + 0.5) * slot;
                double y = MsiDutyToY(_msiFanDuties[i], plotTop, plotBottom);

                if (_msiFanBars[i] != null)
                {
                    _msiFanBars[i].Width = barW;
                    _msiFanBars[i].Height = Math.Max(0, plotBottom - y);
                    Canvas.SetLeft(_msiFanBars[i], cx - barW / 2);
                    Canvas.SetTop(_msiFanBars[i], y);
                }
                if (_msiFanPoints[i] != null)
                {
                    double r = _msiFanPoints[i].Width / 2.0;
                    Canvas.SetLeft(_msiFanPoints[i], cx - r);
                    Canvas.SetTop(_msiFanPoints[i], y - r);
                }
                if (_msiFanValueLabels[i] != null)
                {
                    _msiFanValueLabels[i].Text = $"{_msiFanDuties[i]}%";
                    Canvas.SetLeft(_msiFanValueLabels[i], cx - 14);
                    Canvas.SetTop(_msiFanValueLabels[i], Math.Max(0, y - 22));
                }
                if (_msiFanTempLabels[i] != null)
                {
                    _msiFanTempLabels[i].Text = $"{_msiFanTemps[i]}°C";
                    Canvas.SetLeft(_msiFanTempLabels[i], cx - 17);
                    Canvas.SetTop(_msiFanTempLabels[i], plotBottom + 3);
                }
                // Diamond focus marker below the temp label.
                if (_msiFanTempHandles[i] != null)
                {
                    double dw = _msiFanTempHandles[i].Width;
                    Canvas.SetLeft(_msiFanTempHandles[i], cx - dw / 2);
                    Canvas.SetTop(_msiFanTempHandles[i], plotBottom + 26 - dw / 2);
                }
            }
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

            // The old positional temp line no longer maps to anything (bars are evenly spaced, not on a
            // temperature axis). Keep it hidden; the live temp is shown as the "CPU package" text and by
            // highlighting the active bracket below.
            if (MsiFanTempIndicatorLine != null) MsiFanTempIndicatorLine.Visibility = Visibility.Collapsed;

            // Highlight the temperature bracket the CPU is currently in (the highest breakpoint ≤ temp):
            // its temp label gets a warm tint so the user sees which point governs cooling right now.
            if (!_msiFanPointsBuilt) return;
            int active = -1;
            if (tempC > 0)
                for (int i = 0; i < MsiFanPoints; i++)
                    if (tempC >= _msiFanTemps[i]) active = i;

            for (int i = 0; i < MsiFanPoints; i++)
            {
                if (_msiFanTempLabels[i] == null) continue;
                bool sel = _msiFanSelectedPoint == i; // don't fight the edit-selection highlight
                if (sel) continue;
                _msiFanTempLabels[i].Foreground = new SolidColorBrush(i == active
                    ? Windows.UI.ColorHelper.FromArgb(255, 255, 150, 60)
                    : Windows.UI.ColorHelper.FromArgb(255, 220, 180, 90));
            }
        }

        /// <summary>Which bar column an X coordinate falls into (0…MsiFanPoints-1).</summary>
        private int MsiFanColumnAtX(double x, double width)
        {
            double plotW = width - MsiPlotLeft;
            if (plotW <= 0) return 0;
            int col = (int)((x - MsiPlotLeft) / (plotW / MsiFanPoints));
            return Math.Max(0, Math.Min(MsiFanPoints - 1, col));
        }

        private void MsiFanCurveCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (MsiFanCurveCanvas == null) return;
            var point = e.GetCurrentPoint(MsiFanCurveCanvas).Position;
            double width = MsiFanCurveCanvas.ActualWidth;
            double height = MsiFanCurveCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            double plotBottom = height - MsiPlotBottomPad;
            int col = MsiFanColumnAtX(point.X, width);
            // Bottom strip (under the bars) = temperature edit; the bar area = fan-% edit.
            bool isTemp = point.Y >= plotBottom - 2;

            _msiFanDragIndex = col;
            _msiFanDragIsTemp = isTemp;
            _msiFanSelectedPoint = col;
            _msiFanSelectingTemp = isTemp;
            // Apply immediately at the press position too.
            MsiFanApplyPointerEdit(point, width, height);
            MsiFanCurveCanvas.CapturePointer(e.Pointer);
            HighlightMsiFanPoints();
            e.Handled = true;
        }

        private void MsiFanCurveCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_msiFanDragIndex < 0 || MsiFanCurveCanvas == null) return;
            var point = e.GetCurrentPoint(MsiFanCurveCanvas).Position;
            double width = MsiFanCurveCanvas.ActualWidth;
            double height = MsiFanCurveCanvas.ActualHeight;
            if (height <= 0 || width <= 0) return;
            MsiFanApplyPointerEdit(point, width, height);
            e.Handled = true;
        }

        /// <summary>Apply the current drag: temp handle → X across the plot maps to [min,max] °C;
        /// duty bar → Y maps to fan % (capped by the extended-range toggle).</summary>
        private void MsiFanApplyPointerEdit(Windows.Foundation.Point point, double width, double height)
        {
            double plotTop = MsiPlotTop;
            double plotBottom = height - MsiPlotBottomPad;
            if (_msiFanDragIsTemp)
            {
                double plotW = width - MsiPlotLeft;
                double frac = plotW > 0 ? (point.X - MsiPlotLeft) / plotW : 0;
                double temp = MsiTempMin + frac * (MsiTempMax - MsiTempMin);
                _msiFanTemps[_msiFanDragIndex] = ClampMsiTemp(_msiFanDragIndex, (int)Math.Round(temp));
            }
            else
            {
                double plotH = plotBottom - plotTop;
                double duty = plotH > 0 ? (1.0 - (point.Y - plotTop) / plotH) * 100.0 : 0;
                _msiFanDuties[_msiFanDragIndex] = (int)Math.Max(0, Math.Min(MsiDutyMax(), Math.Round(duty)));
            }
            RenderMsiFanCurve();
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
            if (on)
            {
                // Enabling fan control activates the MSI Default curve unless a preset is already chosen.
                int preset = MsiFanPresetComboBox?.SelectedIndex ?? 0;
                if (preset < 0 || preset > 4) preset = 0;
                LoadCurveForPreset(preset);
                RenderMsiFanCurve();
            }
            SendMsiFanStateToHelper();
        }

        /// <summary>Toggle the >75% "beyond MSI" range. Off caps duty at 75 % (clamping any higher
        /// custom points) and hides the beyond-zone visuals; on unlocks up to 100 %.</summary>
        private void MsiFanExtendedRangeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiFanInitializing) return;
            _msiFanExtended = MsiFanExtendedRangeToggle?.IsOn ?? false;
            ApplicationData.Current.LocalSettings.Values[MsiFanExtendedKey] = _msiFanExtended;

            bool changed = false;
            if (!_msiFanExtended)
            {
                for (int i = 0; i < MsiFanPoints; i++)
                    if (_msiFanDuties[i] > MsiDutyCap) { _msiFanDuties[i] = MsiDutyCap; changed = true; }
            }
            RenderMsiFanCurve();

            // If clamping changed the active custom curve, persist + push it.
            if (changed)
            {
                if ((MsiFanPresetComboBox?.SelectedIndex ?? 0) == 3)
                    ApplicationData.Current.LocalSettings.Values[MsiFanCurveKey] = CurveToCsv();
                if (MsiFanEnableToggle?.IsOn == true)
                    SendMsiFanCurveToHelper();
            }
        }

        private void MsiFanPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_msiFanInitializing) return;
            int idx = MsiFanPresetComboBox?.SelectedIndex ?? 0;
            if (idx < 0 || idx > 4) idx = 0;
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

                int preset = MsiFanPresetComboBox?.SelectedIndex ?? 0;
                if (preset < 0 || preset > 4) preset = 0;
                if (preset == 3)
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
        // Each column has TWO handles: the duty circle (vertical-only) and the temp handle at the
        // bottom (horizontal-only). Left/Right move between columns; Up/Down switch between the duty
        // handle (top) and the temp handle (bottom) of the current column. A grabs the selected handle:
        //   duty handle grabbed → Up/Down change fan %; temp handle grabbed → Left/Right change temp.
        // A/B releases and commits as a Custom curve.
        private int _msiFanSelectedPoint = -1;
        private bool _msiFanGrabbed;
        private bool _msiFanSelectingTemp;   // false = duty circle, true = temp handle

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
            HighlightMsiFanPoints(false); // de-emphasise all handles when focus leaves the graph
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

            int idx = Math.Max(0, Math.Min(MsiFanPoints - 1, _msiFanSelectedPoint));

            if (_msiFanGrabbed)
            {
                if (_msiFanSelectingTemp)
                {
                    // Temp handle: horizontal only.
                    if (left || right)
                    {
                        _msiFanTemps[idx] = ClampMsiTemp(idx, _msiFanTemps[idx] + (right ? 2 : -2));
                        RenderMsiFanCurve(); HighlightMsiFanPoints(); e.Handled = true;
                    }
                    else if (isA || isB) { _msiFanGrabbed = false; CommitMsiFanCustomEdit(); HighlightMsiFanPoints(); e.Handled = true; }
                }
                else
                {
                    // Duty circle: vertical only (capped by the extended-range toggle).
                    if (up || down)
                    {
                        _msiFanDuties[idx] = Math.Max(0, Math.Min(MsiDutyMax(), _msiFanDuties[idx] + (up ? 5 : -5)));
                        RenderMsiFanCurve(); HighlightMsiFanPoints(); e.Handled = true;
                    }
                    else if (isA || isB) { _msiFanGrabbed = false; CommitMsiFanCustomEdit(); HighlightMsiFanPoints(); e.Handled = true; }
                }
                return;
            }

            // Not grabbed: navigate columns / switch handle / grab / leave the graph.
            if (left)  { _msiFanSelectedPoint = Math.Max(0, _msiFanSelectedPoint - 1); HighlightMsiFanPoints(); e.Handled = true; }
            else if (right) { _msiFanSelectedPoint = Math.Min(MsiFanPoints - 1, _msiFanSelectedPoint + 1); HighlightMsiFanPoints(); e.Handled = true; }
            else if (isA) { _msiFanGrabbed = true; HighlightMsiFanPoints(); e.Handled = true; }
            else if (up)
            {
                if (_msiFanSelectingTemp) { _msiFanSelectingTemp = false; HighlightMsiFanPoints(); } // temp → duty handle
                else MsiFanPresetComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);               // leave up to preset
                e.Handled = true;
            }
            else if (down)
            {
                if (!_msiFanSelectingTemp) { _msiFanSelectingTemp = true; HighlightMsiFanPoints(); }  // duty → temp handle
                else if (MsiFanCheckButton != null) MsiFanCheckButton.Focus(Windows.UI.Xaml.FocusState.Keyboard); // leave down to Check
                else (PerGameProfileToggle ?? (Windows.UI.Xaml.Controls.Control)FPSLimitToggle)?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
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

        /// <summary>Maps the current 5-point graph duty to the 8-byte EC table — byte-for-byte the same
        /// mapping the helper uses when writing (MsiClawFanController.BuildFanTable): duty is the RAW EC
        /// byte (no ×1.5). Layout: [backup=d1, 0, d1, d2, d3, d4, d5, d5(dup)].</summary>
        private byte[] MsiExpectedTable()
        {
            byte D(int i) => (byte)Math.Max(0, Math.Min(100, _msiFanDuties[i]));
            return new byte[8] { D(0), 0, D(0), D(1), D(2), D(3), D(4), D(4) };
        }

        /// <summary>The expected 7-byte thermal (temperature-axis) table: [0, t1..t5, t5(dup)].</summary>
        private byte[] MsiExpectedThermal()
        {
            byte T(int i) => (byte)Math.Max(0, Math.Min(120, _msiFanTemps[i]));
            return new byte[7] { 0, T(0), T(1), T(2), T(3), T(4), T(4) };
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
        /// Handles the helper's "MsiFanStatus" push: "b0,..,b7|controlBit|readOk|fullSpeed|rpm|thermalCsv".
        /// Compares the read-back EC duty table AND temperature axis against the graph and shows a status.
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

                // Measurement line: actual fan RPM + whether the EC full-speed override is engaged.
                string measure = (rpm >= 0 ? $"Fan: {rpm} RPM" : "Fan: n/a")
                                 + $" · full-speed override: {(fullSpeed ? "ON" : "off")}";
                if (FanFullBlastStatusText != null) FanFullBlastStatusText.Text = measure;

                byte[] ec = new byte[8];
                for (int i = 0; i < 8 && i < ecParts.Length; i++)
                    byte.TryParse(ecParts[i], out ec[i]);

                // Temperature axis read-back (Set_Thermal), if the helper included it.
                byte[] th = null;
                if (sections.Length > 5 && !string.IsNullOrEmpty(sections[5]))
                {
                    var thParts = sections[5].Split(',');
                    th = new byte[7];
                    for (int i = 0; i < 7 && i < thParts.Length; i++) byte.TryParse(thParts[i], out th[i]);
                }

                byte[] expected = MsiExpectedTable();
                byte[] expectedTh = MsiExpectedThermal();
                // Compare duty bytes 1..7 only. Byte 0 is the EC-managed idle "backup" sample the MSI
                // firmware nudges on its own, which produced spurious mismatches; it has no meaningful
                // effect at idle, so we don't flag it.
                bool match = true;
                for (int i = 1; i < 8; i++) if (ec[i] != expected[i]) { match = false; break; }
                bool thMatch = th == null; // no axis in payload → don't fail on it
                if (th != null)
                {
                    thMatch = true;
                    for (int i = 1; i < 7; i++) if (th[i] != expectedTh[i]) { thMatch = false; break; }
                }

                bool enabled = MsiFanEnableToggle?.IsOn ?? false;
                int preset = MsiFanPresetComboBox?.SelectedIndex ?? -1;
                string axisLine = th != null ? $"\nTemp axis: [{string.Join(",", th)}]" : "";

                if (!readOk)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 230, 120, 120));
                    MsiFanCheckStatus.Text = "✗ Could not read fan values from the EC.";
                }
                else if (enabled && preset == 4)
                {
                    // DEBUG "EC Sport default": firmware hardware table drives the fan (control OFF is
                    // correct). Don't compare against the software-curve model — just show the raw table.
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 120, 200, 230));
                    MsiFanCheckStatus.Text = $"EC Sport default (debug) — firmware drives the fan (control bit off is correct).\nRaw EC table: [{string.Join(",", ec)}]{axisLine}";
                }
                else if (!enabled)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 200, 200, 200));
                    MsiFanCheckStatus.Text = $"Custom fan curve is OFF (firmware control). EC table: [{string.Join(",", ec)}], control bit: {(controlOn ? "on" : "off")}.";
                }
                else if (match && thMatch && controlOn)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 120, 210, 120));
                    MsiFanCheckStatus.Text = $"✓ Applied correctly — EC matches the graph and control is active.\nEC: [{string.Join(",", ec)}]{axisLine}";
                }
                else
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 240, 180, 80));
                    string why = !controlOn ? "control bit is OFF" : !match ? "duty values differ from the graph" : "temp axis differs from the graph";
                    MsiFanCheckStatus.Text = $"⚠ Mismatch ({why}).\nEC: [{string.Join(",", ec)}]{axisLine}\nExpected: [{string.Join(",", expected)}] axis [{string.Join(",", expectedTh)}]";
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

        // ── EXPERIMENTAL: controller-HID probe + native MSI fan detection (Debug panel) ──────────
        private async void ClawHidProbeSend_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!App.IsConnected) { if (ClawHidProbeResult != null) ClawHidProbeResult.Text = "Helper not connected."; return; }
                string hex = ClawHidProbeInput?.Text?.Trim() ?? "";
                bool read = ClawHidProbeReadCheck?.IsChecked == true;
                if (ClawHidProbeResult != null) ClawHidProbeResult.Text = "Sent, waiting for response…";
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "ClawHidProbe", $"{hex}|{(read ? "1" : "0")}" } });
                Logger.Info($"ClawHidProbe: '{hex}' read={read}");
            }
            catch (Exception ex) { Logger.Error($"ClawHidProbeSend: {ex.Message}"); }
        }

        private void ClawHidProbeReadMode_Click(object sender, RoutedEventArgs e)
        {
            if (ClawHidProbeInput != null) ClawHidProbeInput.Text = "0F 00 00 3C 26";
            if (ClawHidProbeReadCheck != null) ClawHidProbeReadCheck.IsChecked = true;
            ClawHidProbeSend_Click(sender, e);
        }

        private async void MsiFanDetect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!App.IsConnected) { if (MsiFanDetectResult != null) MsiFanDetectResult.Text = "Helper not connected."; return; }
                if (MsiFanDetectResult != null) MsiFanDetectResult.Text = "Reading EC…";
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanDetect", "1" } });
                Logger.Info("MsiFanDetect requested");
            }
            catch (Exception ex) { Logger.Error($"MsiFanDetect: {ex.Message}"); }
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

        /// <summary>Highlight the selected handle: the duty circle OR the temp handle of the selected
        /// column (yellow = selected, orange = grabbed). Others revert to their idle colours.
        /// <paramref name="active"/> = false clears all highlights (focus left the graph).</summary>
        private void HighlightMsiFanPoints(bool active = true)
        {
            var blue    = Windows.UI.ColorHelper.FromArgb(255, 0, 170, 255);   // duty circle idle
            var yellow  = Windows.UI.ColorHelper.FromArgb(255, 255, 215, 0);   // selected
            var orangeG = Windows.UI.ColorHelper.FromArgb(255, 255, 120, 0);   // grabbed
            var tempIdle = Windows.UI.ColorHelper.FromArgb(255, 220, 180, 90); // temp label idle
            for (int i = 0; i < MsiFanPoints; i++)
            {
                bool colSel  = active && i == _msiFanSelectedPoint;
                bool dutySel = colSel && !_msiFanSelectingTemp;
                bool tempSel = colSel && _msiFanSelectingTemp;

                // Duty circle + bar.
                if (_msiFanPoints[i] != null)
                {
                    Windows.UI.Color dc = dutySel ? (_msiFanGrabbed ? orangeG : yellow) : blue;
                    _msiFanPoints[i].Fill = new SolidColorBrush(dc);
                    _msiFanPoints[i].Width = _msiFanPoints[i].Height = dutySel ? 20 : 16;
                }
                if (_msiFanBars[i] != null)
                    _msiFanBars[i].Opacity = dutySel ? 1.0 : 0.85;
                if (_msiFanValueLabels[i] != null)
                    _msiFanValueLabels[i].Foreground = new SolidColorBrush(dutySel ? (_msiFanGrabbed ? orangeG : yellow) : Windows.UI.Colors.White);

                // Temp label + diamond focus marker.
                if (_msiFanTempLabels[i] != null)
                    _msiFanTempLabels[i].Foreground = new SolidColorBrush(tempSel ? (_msiFanGrabbed ? orangeG : yellow) : tempIdle);
                if (_msiFanTempHandles[i] != null)
                {
                    _msiFanTempHandles[i].Fill = new SolidColorBrush(tempSel ? (_msiFanGrabbed ? orangeG : yellow)
                                                                             : Windows.UI.ColorHelper.FromArgb(255, 255, 150, 40));
                    _msiFanTempHandles[i].Width = _msiFanTempHandles[i].Height = tempSel ? 18 : 14;
                }
            }
            RenderMsiFanCurve(); // re-center the (now larger) selected circle
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
