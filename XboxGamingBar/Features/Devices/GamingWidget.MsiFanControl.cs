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
        // breakpoints [44,54,64,74,82]; duty is the RAW EC byte 0–150 (MSI scale, no ×1.5; MSI's own
        // presets cap at 75 = half fan, the beyond-MSI toggle unlocks up to 150 = full ~8690 RPM).
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

        // MSI's own curves cap at 75. The ">75" ("beyond MSI") range is opt-in via a toggle. The real EC
        // duty scale is 0–150 (MSI Center M's Custom slider tops out at 150 — verified on-device
        // 2026-07-11; the shown "%" IS the raw EC byte, MSI Auto's 75 = HALF fan / ~4552 RPM, 150 = full
        // ~8690 RPM). Extended unlocks the full 0–150; non-extended stays on the familiar 0–100 axis capped
        // at MSI's own 75.
        private const int MsiDutyCap = 75;
        private bool _msiFanExtended;
        private int MsiDutyMax() => _msiFanExtended ? 150 : MsiDutyCap;
        // Y-axis full-scale for the graph: 0–150 when the beyond-MSI range is on, else the classic 0–100.
        private double MsiAxisMax() => _msiFanExtended ? 150.0 : 100.0;
        // The 5 horizontal gridline / Y-label percentages for the current axis: {0, ¼, ½, ¾, full}
        // → {0,25,50,75,100} at 0–100 or {0,38,75,113,150} at 0–150 (75 stays a gridline in both).
        private int MsiGridPctAt(int g) => (int)Math.Round(MsiAxisMax() * g / 4.0);

        // ── Per-model duty floor ─────────────────────────────────────────────────────────
        // The lowest duty the editor allows. Below this the model's firmware overrides the curve at
        // idle anyway, so a lower curve point would only invert the loudness (idle louder than light
        // load). On-device: the A2VM rests its idle duty at 40 (= its own default-curve bottom, and its
        // physical fan minimum ~2450 RPM); the Claw 8 EX rests at ~58 (~3570 RPM) regardless of the
        // curve. So the floor = each model's idle floor. Verified from EC-tach logs 2026-07-20.
        private bool IsClaw8ExWidget()
        {
            var n = deviceDisplayName?.Value;
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("CG3EM", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("1T91",  StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("8 EX",  StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("8EX",   StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private int MsiDutyFloor() => IsClaw8ExWidget() ? 58 : 40;

        // ── Duty→RPM model (from on-device EC-tach logs, 2026-07-20) ──────────────────────
        // Piecewise-linear anchors; linearly extrapolated above the top anchor (no tach data past
        // ~duty 94, so the very top of the axis is a hard extrapolation, not a measurement). The EX
        // spins ~200 RPM faster than the A2VM below ~duty 50; from ~58 up both agree within ~2%.
        // Used only for the Y-axis "(rpm)" labels — never for the EC write.
        private static readonly int[] MsiRpmDutyA2VM = { 0,   20,   40,   45,   49,   58,   67,   75 };
        private static readonly int[] MsiRpmValA2VM  = { 0, 2445, 2465, 2862, 2994, 3549, 4064, 4580 };
        private static readonly int[] MsiRpmDutyEx   = { 0,   20,   39,   45,   51,   58,   62,   70,   75,   80,   84,   94 };
        private static readonly int[] MsiRpmValEx    = { 0, 2633, 2673, 3112, 3175, 3571, 3839, 4466, 4684, 4938, 5220, 5413 };

        /// <summary>Estimated fan RPM for a duty value on the current model, rounded to the nearest 10.</summary>
        private int MsiDutyToRpm(double duty)
        {
            bool ex = IsClaw8ExWidget();
            int[] dx = ex ? MsiRpmDutyEx : MsiRpmDutyA2VM;
            int[] ry = ex ? MsiRpmValEx  : MsiRpmValA2VM;
            int n = dx.Length;
            double rpm;
            if (duty <= dx[0]) rpm = ry[0];
            else
            {
                rpm = ry[n - 1] + (ry[n - 1] - ry[n - 2]) / (double)(dx[n - 1] - dx[n - 2]) * (duty - dx[n - 1]);
                for (int i = 1; i < n; i++)
                    if (duty <= dx[i])
                    {
                        double f = (duty - dx[i - 1]) / (double)(dx[i] - dx[i - 1]);
                        rpm = ry[i - 1] + f * (ry[i] - ry[i - 1]);
                        break;
                    }
            }
            return (int)(Math.Round(rpm / 10.0) * 10);
        }

        private readonly int[] _msiFanTemps = (int[])MsiTempsDefault.Clone();
        private readonly int[] _msiFanDuties = (int[])MsiDutyDefault.Clone();
        // MSI-style fixed evenly-spaced BARS (not a positional temperature axis). Bar height = fan %
        // (vertical edit via the circle on top). The temperature is shown as a label UNDER each bar
        // (horizontal edit). Horizontal %-gridlines + Y labels give the scale.
        // Gridline %-values are computed per axis via MsiGridPctAt(g) (dynamic 0–100 / 0–150).
        private static readonly uint[] MsiGridColor = { 0x6FB7FF, 0x8FD06A, 0xE6C84A, 0xF0A030, 0xF0603C };
        private readonly Windows.UI.Xaml.Shapes.Rectangle[] _msiFanBars = new Windows.UI.Xaml.Shapes.Rectangle[MsiFanPoints];
        private readonly Ellipse[] _msiFanPoints = new Ellipse[MsiFanPoints];        // duty circle (top of bar)
        private readonly TextBlock[] _msiFanValueLabels = new TextBlock[MsiFanPoints]; // "%" above the bar
        private readonly TextBlock[] _msiFanTempLabels = new TextBlock[MsiFanPoints];  // "44°C" under the bar
        // Temp focus markers: a left/right double-arrow (◄ ►) under each temp label signalling the handle
        // moves horizontally. This is the controller-reachable temp handle. Two separate triangles with a
        // gap in the middle (a Path) so it reads as arrows, not a solid diamond.
        private readonly Windows.UI.Xaml.Shapes.Path[] _msiFanTempHandles = new Windows.UI.Xaml.Shapes.Path[MsiFanPoints];
        private readonly Line[] _msiFanGridLines = new Line[5];
        private readonly TextBlock[] _msiFanGridLabels = new TextBlock[5];
        private bool _msiFanPointsBuilt;
        private bool _msiFanInitializing;
        private int _msiFanDragIndex = -1;
        private bool _msiFanDragIsTemp;  // mouse drag target is a temp label (horizontal), not a duty bar

        // Device firmware defaults pushed by the helper (OnMsiFanState): the live temperature axis and the
        // per-model "MSI Default" duty curve. Used so presets reflect the REAL device (correct on the EX,
        // whose factory curve differs from the A2VM constants). Null until the helper pushes them.
        private int[] _msiModelTemps;
        private int[] _msiModelDuty;

        // Pending manual edits: a curve/temp edit no longer writes the EC on release. Instead it sets this
        // flag and lights the Apply button; the EC is written only when the user clicks Apply. Protects the
        // fan/EC from a write on every drag tick. Preset changes + the enable toggle still apply immediately.
        private bool _msiFanDirty;

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

        // Custom fan curve is a per-model capability (helper-driven). It is OFF on some Claw generations
        // — e.g. the Claw 8 EX, where MSI's own custom curves still have issues. Must be an MSI Claw AND
        // report the fan-control capability.
        private bool IsMsiFanControlSupported()
            => IsMsiClawDevice() && (deviceSupportsFanControl?.Value ?? false);

        /// <summary>Show the fan card on fan-capable MSI Claw models and restore the saved state. Idempotent.</summary>
        private void InitializeMsiFanCard()
        {
            if (MsiFanCard == null) return;

            // Fan tab is gated on the per-model fan-control capability (like the Display tab).
            if (FanNavItem != null)
                FanNavItem.Visibility = IsMsiFanControlSupported() ? Visibility.Visible : Visibility.Collapsed;

            if (!IsMsiFanControlSupported())
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
            ClearFanDirty();
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
            // parts[2] = live firmware temp axis, parts[3] = per-model "MSI Default" duty (both 5 CSV ints).
            if (parts.Length > 2) _msiModelTemps = ParseFiveInts(parts[2], 0, 120) ?? _msiModelTemps;
            if (parts.Length > 3) _msiModelDuty  = ParseFiveInts(parts[3], 0, 150) ?? _msiModelDuty;

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
            ClearFanDirty(); // helper-pushed state is authoritative → no pending edits
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
                    FontSize = 11,
                    LineHeight = 12,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c)),
                    IsHitTestVisible = false
                    // Text is set per-render (RenderMsiFanCurve): "%\n(rpm)", tracking the 0–100 / 0–150 axis.
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

                // Temp focus marker: a left/right double-arrow (◄ ►) under the temp label — two triangles
                // with a gap in the middle — reading as "moves horizontally". This is the temp handle.
                // Geometry is defined in a 0..1 box and scaled via Stretch=Fill, so the position/highlight
                // code can keep driving size through Width/Height.
                var arrow = new Windows.UI.Xaml.Shapes.Path
                {
                    Data = BuildTempArrowGeometry(),
                    Stretch = Windows.UI.Xaml.Media.Stretch.Fill,
                    Width = 16,
                    Height = 14,
                    Fill = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 255, 150, 40)),
                    Stroke = new SolidColorBrush(Windows.UI.Colors.White),
                    StrokeThickness = 1.0,
                    IsHitTestVisible = false,
                    Tag = i,
                    // Hidden while the temp axis is read-only: the arrows advertise "drag me sideways",
                    // which is no longer true. Kept in the tree so the layout/highlight code is unchanged.
                    Visibility = MsiFanTempAxisEditable
                        ? Windows.UI.Xaml.Visibility.Visible
                        : Windows.UI.Xaml.Visibility.Collapsed
                };
                Canvas.SetZIndex(arrow, 11);
                _msiFanTempHandles[i] = arrow;
                MsiFanCurveCanvas.Children.Add(arrow);
            }
            _msiFanPointsBuilt = true;
        }

        /// <summary>Builds the temp-handle "◄ ►" geometry in a normalized 0..1 box: a left-pointing and a
        /// right-pointing triangle with a gap in the middle (scaled to the element size via Stretch=Fill).</summary>
        private static Windows.UI.Xaml.Media.Geometry BuildTempArrowGeometry()
        {
            const double g = 0.36;          // inner edge of each triangle (gap = 1 - 2g ≈ 28% in the middle)
            var geo = new Windows.UI.Xaml.Media.PathGeometry();
            // ◄ left arrow: apex at x=0, base at x=g
            geo.Figures.Add(TempArrowTriangle(
                new Windows.Foundation.Point(0.0, 0.5),
                new Windows.Foundation.Point(g,   0.0),
                new Windows.Foundation.Point(g,   1.0)));
            // ► right arrow: apex at x=1, base at x=1-g
            geo.Figures.Add(TempArrowTriangle(
                new Windows.Foundation.Point(1.0,     0.5),
                new Windows.Foundation.Point(1.0 - g, 0.0),
                new Windows.Foundation.Point(1.0 - g, 1.0)));
            return geo;
        }

        private static Windows.UI.Xaml.Media.PathFigure TempArrowTriangle(
            Windows.Foundation.Point a, Windows.Foundation.Point b, Windows.Foundation.Point c)
        {
            var fig = new Windows.UI.Xaml.Media.PathFigure { StartPoint = a, IsClosed = true, IsFilled = true };
            fig.Segments.Add(new Windows.UI.Xaml.Media.LineSegment { Point = b });
            fig.Segments.Add(new Windows.UI.Xaml.Media.LineSegment { Point = c });
            return fig;
        }

        // The device's real firmware axis / "MSI Default" duty (helper-pushed) if available, else the
        // A2VM constants. Keeps the widget graph + verify in sync with what the helper actually writes.
        private int[] ModelTemps() => _msiModelTemps ?? MsiTempsDefault;
        private int[] ModelDefaultDuty() => _msiModelDuty ?? MsiDutyDefault;
        // Cooling axis = the model axis shifted −10 °C (earlier ramp).
        private int[] ModelCoolingTemps()
        {
            var baseT = ModelTemps();
            var c = new int[MsiFanPoints];
            for (int i = 0; i < MsiFanPoints; i++) c[i] = Math.Max(0, baseT[i] - 10);
            return c;
        }

        /// <summary>Load the 5-point (temp,duty) curve for a preset into the model arrays.</summary>
        private void LoadCurveForPreset(int preset)
        {
            int[] temps, duties;
            switch (preset)
            {
                case 1: temps = ModelTemps();        duties = MsiDutyQuietIdle;     break;
                case 2: temps = ModelCoolingTemps(); duties = MsiDutyCooling;       break;
                case 3:
                    if (LoadCustomCurveFromStorage(out int[] ct, out int[] cd))
                    {
                        Array.Copy(ct, _msiFanTemps, MsiFanPoints);
                        Array.Copy(cd, _msiFanDuties, MsiFanPoints);
                        EnforceDutyFloor();
                        return;
                    }
                    temps = ModelTemps(); duties = ModelDefaultDuty(); break;
                case 4: temps = ModelTemps(); duties = MsiDutyEcSport; break; // debug: EC Sport default (display)
                default: temps = ModelTemps(); duties = ModelDefaultDuty(); break; // 0 = MSI Default
            }
            Array.Copy(temps, _msiFanTemps, MsiFanPoints);
            Array.Copy(duties, _msiFanDuties, MsiFanPoints);
            EnforceDutyFloor();
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
                                d[i] = Math.Max(0, Math.Min(150, int.Parse(dp[i], CultureInfo.InvariantCulture)));
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

        /// <summary>
        /// Copies the fan editor's current state (preset + exact curve) into a performance profile.
        /// CAPTURE ONLY — nothing here reaches the hardware, and there is deliberately no counterpart
        /// that loads a profile's fan settings back into the editor or applies them on profile switch.
        /// The fan write path is still being validated against the global profile; per-game apply is a
        /// separate, explicit step. Storing both the preset and the raw duties (not just the preset)
        /// is what lets the editor later offer a profile's exact values instead of re-deriving them
        /// from a preset name — Custom (3) has no meaning at all without its curve.
        /// No-op on non-Claw devices, where the fan card does not exist.
        /// </summary>
        private void CaptureMsiFanIntoProfile(PerformanceProfile profile)
        {
            if (profile == null) return;
            try
            {
                if (MsiFanPresetComboBox == null) return;   // fan card absent (non-Claw / not built yet)

                int preset = MsiFanPresetComboBox.SelectedIndex;
                if (preset < 0) return;                     // nothing selected yet — leave the profile untouched

                profile.MsiFanPreset = preset;
                profile.MsiFanCurve = CurveToCsv();
            }
            catch (Exception ex)
            {
                // Never let fan capture break a profile save — the rest of the profile matters more.
                Logger.Debug($"CaptureMsiFanIntoProfile skipped: {ex.Message}");
            }
        }

        /// <summary>Serialize the model as "t1,..,t5;d1,..,d5" — the wire + storage format.</summary>
        private string CurveToCsv()
            => string.Join(",", _msiFanTemps.Select(v => v.ToString(CultureInfo.InvariantCulture)))
               + ";" + string.Join(",", _msiFanDuties.Select(v => v.ToString(CultureInfo.InvariantCulture)));

        /// <summary>Parse exactly 5 comma-separated ints, each clamped to [lo,hi]. Returns null if the
        /// string isn't 5 valid ints (so the caller keeps its previous value / constant fallback).</summary>
        private static int[] ParseFiveInts(string csv, int lo, int hi)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;
            var p = csv.Split(',');
            if (p.Length != MsiFanPoints) return null;
            var r = new int[MsiFanPoints];
            for (int i = 0; i < MsiFanPoints; i++)
                if (!int.TryParse(p[i], NumberStyles.Any, CultureInfo.InvariantCulture, out int v)) return null;
                else r[i] = Math.Max(lo, Math.Min(hi, v));
            return r;
        }

        /// <summary>Keep the duty curve a monotonic "staircase" after a point at <paramref name="changed"/>
        /// was edited: every higher point must sit at least 1 % above its left neighbour, every lower point
        /// at least 1 % below its right neighbour. When the user raises a point, the ones to its right are
        /// pulled UP to follow; when they lower it, the ones to its left are pulled DOWN. Clamped to the
        /// current duty range (a run into the ceiling/floor flattens rather than inverting).</summary>
        private void EnforceDutyStaircase(int changed)
        {
            int max = MsiDutyMax();
            int floor = MsiDutyFloor();
            for (int i = changed + 1; i < MsiFanPoints; i++)
                if (_msiFanDuties[i] < _msiFanDuties[i - 1] + 1)
                    _msiFanDuties[i] = Math.Min(max, _msiFanDuties[i - 1] + 1);
            for (int i = changed - 1; i >= 0; i--)
                if (_msiFanDuties[i] > _msiFanDuties[i + 1] - 1)
                    _msiFanDuties[i] = Math.Max(floor, _msiFanDuties[i + 1] - 1);
        }

        /// <summary>Raise any curve point below the per-model duty floor up to it, then re-stair so the
        /// curve stays strictly increasing. Called whenever a preset/stored curve is (re)loaded so a
        /// sub-floor curve can never reach the EC (prevents the "idle louder than light load" inversion).</summary>
        private void EnforceDutyFloor()
        {
            int floor = MsiDutyFloor();
            int max = MsiDutyMax();
            for (int i = 0; i < MsiFanPoints; i++)
                if (_msiFanDuties[i] < floor) _msiFanDuties[i] = floor;
            // Keep strictly increasing after clamping (a flat run at the floor becomes floor, floor+1, …).
            for (int i = 1; i < MsiFanPoints; i++)
                if (_msiFanDuties[i] <= _msiFanDuties[i - 1])
                    _msiFanDuties[i] = Math.Min(max, _msiFanDuties[i - 1] + 1);
        }

        // ── Pending-change (Apply button) state ─────────────────────────────────────
        // A manual curve/temp edit marks the state dirty and lights the Apply button instead of writing
        // the EC immediately. Preset changes + the enable toggle apply at once and clear the flag.
        private void MarkFanDirty()
        {
            _msiFanDirty = true;
            UpdateApplyButtonState();
        }

        private void ClearFanDirty()
        {
            _msiFanDirty = false;
            UpdateApplyButtonState();
        }

        /// <summary>Enable + highlight the Apply button only when there are pending edits. When clean it is
        /// disabled AND removed from the tab order (IsTabStop=false) so the D-Pad focus chain skips it — a
        /// disabled-but-focusable button is a notorious controller focus trap.</summary>
        private void UpdateApplyButtonState()
        {
            if (MsiFanApplyButton == null) return;
            bool on = _msiFanDirty;
            MsiFanApplyButton.IsEnabled = on;
            MsiFanApplyButton.IsTabStop = on;

            // Pulse while pending so the button — which only lights up on a change — is easy to spot.
            // Stop first, then set Opacity: a running storyboard holds the animated value otherwise.
            try
            {
                MsiFanApplyBlink?.Stop();
                MsiFanApplyButton.Opacity = on ? 1.0 : 0.45;
                if (on) MsiFanApplyBlink?.Begin();
            }
            catch { MsiFanApplyButton.Opacity = on ? 1.0 : 0.45; }
        }

        private void MsiFanApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_msiFanDirty) return;
            SendMsiFanCurveToHelper();
            ClearFanDirty();
            // Focus back onto the curve so the controller flow continues naturally.
            MsiFanCurveFocus?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
        }

        private void MsiFanApplyButton_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                MsiFanCurveFocus?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                MsiFanCheckButton?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        /// <summary>Live CPU-fan RPM below the curve, fed by the 1 Hz Quick Metrics push (fanRpm, from the
        /// EC tach — the same source the RTSS OSD uses). Shows "0 RPM" when the fan is idle/off.</summary>
        internal void UpdateMsiFanRpm(double rpm)
        {
            if (MsiFanRpmLabel == null) return;
            int r = rpm > 0 ? (int)Math.Round(rpm) : 0;
            MsiFanRpmLabel.Text = $"{r} RPM";
        }

        // Plot padding: room above the bars for the % labels, below for the temp labels, left for the
        // Y-axis % labels.
        private const double MsiPlotTop = 26;
        private const double MsiPlotBottomPad = 48;   // room for the temp label + diamond focus marker
        private const double MsiPlotLeft = 54;   // widened to fit the "% (rpm)" Y-axis labels

        private double MsiDutyToY(double duty, double plotTop, double plotBottom)
            => plotBottom - (duty / MsiAxisMax()) * (plotBottom - plotTop);

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

            // Horizontal %-gridlines + Y labels (dynamic: 0–100 or 0–150 depending on the extended range).
            for (int g = 0; g < 5; g++)
            {
                int pct = MsiGridPctAt(g);
                double gy = MsiDutyToY(pct, plotTop, plotBottom);
                if (_msiFanGridLines[g] != null)
                {
                    _msiFanGridLines[g].X1 = plotLeft; _msiFanGridLines[g].X2 = width;
                    _msiFanGridLines[g].Y1 = gy; _msiFanGridLines[g].Y2 = gy;
                }
                if (_msiFanGridLabels[g] != null)
                {
                    // "% (rpm)" like MSI Center M — the estimated RPM for this duty on the current model,
                    // on a second line. 0% shows no RPM (would collide with the temp labels underneath).
                    _msiFanGridLabels[g].Text = pct <= 0 ? "0%" : $"{pct}%\n({MsiDutyToRpm(pct)})";
                    Canvas.SetLeft(_msiFanGridLabels[g], 2);
                    Canvas.SetTop(_msiFanGridLabels[g], gy - (pct <= 0 ? 9 : 16));
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
                    double dh = _msiFanTempHandles[i].Height;
                    Canvas.SetLeft(_msiFanTempHandles[i], cx - dw / 2);
                    Canvas.SetTop(_msiFanTempHandles[i], plotBottom + 26 - dh / 2);
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
            // Bottom strip (under the bars) used to be the temperature edit; the bar area = fan-% edit.
            // The temp axis is read-only now, so a press anywhere always edits the duty bar.
            bool isTemp = MsiFanTempAxisEditable && point.Y >= plotBottom - 2;

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
                double duty = plotH > 0 ? (1.0 - (point.Y - plotTop) / plotH) * MsiAxisMax() : 0;
                _msiFanDuties[_msiFanDragIndex] = (int)Math.Max(MsiDutyFloor(), Math.Min(MsiDutyMax(), Math.Round(duty)));
                EnforceDutyStaircase(_msiFanDragIndex);
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

                // Manual edit: don't write the EC now — light the Apply button (protects the fan/EC from a
                // write on every drag). The curve is applied when the user clicks Apply.
                if (MsiFanEnableToggle?.IsOn == true)
                    MarkFanDirty();
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
            ClearFanDirty();
        }

        /// <summary>Toggle the ">75" "beyond MSI" range. Off caps duty at 75 (clamping any higher
        /// custom points) and shows the 0–100 axis; on unlocks the full raw EC range up to 150
        /// (~8690 RPM) and switches the graph to the 0–150 axis.</summary>
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

            // If clamping changed the active custom curve, persist it and light Apply (a curve edit — not an
            // immediate EC write, consistent with manual point edits).
            if (changed)
            {
                if ((MsiFanPresetComboBox?.SelectedIndex ?? 0) == 3)
                    ApplicationData.Current.LocalSettings.Values[MsiFanCurveKey] = CurveToCsv();
                if (MsiFanEnableToggle?.IsOn == true)
                    MarkFanDirty();
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
            // Preset selection is a single, deliberate change → apply immediately and drop any pending edits.
            SendMsiFanStateToHelper();
            ClearFanDirty();
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

        /// <summary>The temperature axis is EC-owned and is no longer written (MSI Center M never writes
        /// it either — see MsiClawFanController.ApplyMsiCurve). The breakpoints stay on screen as labels so
        /// the curve is readable, but they are not editable: an editable control that cannot reach the
        /// hardware is worse than none. Only the fan duties are ours to set.</summary>
        private const bool MsiFanTempAxisEditable = false;

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
                    // Duty circle: vertical only, finest 1-step (capped by the extended-range toggle and
                    // floored at the per-model duty floor).
                    if (up || down)
                    {
                        _msiFanDuties[idx] = Math.Max(MsiDutyFloor(), Math.Min(MsiDutyMax(), _msiFanDuties[idx] + (up ? 1 : -1)));
                        EnforceDutyStaircase(idx);
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
                // With the temp axis read-only there is no second handle row to step into, so down always
                // leaves the graph.
                if (MsiFanTempAxisEditable && !_msiFanSelectingTemp) { _msiFanSelectingTemp = true; HighlightMsiFanPoints(); }  // duty → temp handle
                // Leave down: to Apply when there are pending edits (it's in the tab order only then), else Check.
                else if (_msiFanDirty && MsiFanApplyButton != null) MsiFanApplyButton.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else if (MsiFanCheckButton != null) MsiFanCheckButton.Focus(Windows.UI.Xaml.FocusState.Keyboard);
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
                // Up: to Apply when pending (it sits between the graph and Check), else back to the graph.
                if (_msiFanDirty && MsiFanApplyButton != null) MsiFanApplyButton.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else MsiFanCurveFocus?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
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
        /// <summary>
        /// The 8-byte Set_Fan duty table we expect to see in the EC. Must mirror
        /// MsiClawFanController.BuildFanTable byte for byte, or the check lies.
        /// Only indices 1..6 are ours — see MsiWrittenDutyFirst/Last.
        /// </summary>
        private byte[] MsiExpectedTable()
        {
            byte D(int i) => (byte)Math.Max(0, Math.Min(150, _msiFanDuties[i]));
            return new byte[8] { 0, 0, D(0), D(1), D(2), D(3), D(4), D(4) };
        }

        // The slots the helper actually writes. SetFanTable patches payload index 1..6 only and leaves
        // the surrounding bytes as it read them, exactly like MSI's Adv_Fan — index 0 and index 7 are
        // EC state, not curve points, and comparing them produces a false "Mismatch" on hardware whose
        // boundary bytes happen to be non-zero (the Claw 8 EX ships index 7 = 94).
        private const int MsiWrittenDutyFirst = 1;
        private const int MsiWrittenDutyLast = 6;

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
                // Compare ONLY the slots the helper writes (1..6). Bytes 0 and 7 are the EC's own
                // boundary state which SetFanTable deliberately preserves, so they will differ from any
                // model we build and must never count as a mismatch.
                bool match = true;
                for (int i = MsiWrittenDutyFirst; i <= MsiWrittenDutyLast; i++)
                    if (ec[i] != expected[i]) { match = false; break; }

                // The temperature axis is NOT compared: we stopped writing it entirely (it is the
                // firmware's own, and writing it is what zeroed the EX's thermal ceiling). It is still
                // read and displayed below, as information — never as a pass/fail criterion.

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
                else if (match && controlOn)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 120, 210, 120));
                    MsiFanCheckStatus.Text = $"✓ Applied correctly — EC matches the graph and control is active.\nEC: [{string.Join(",", ec)}]{axisLine}";
                }
                else
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 240, 180, 80));
                    string why = !controlOn ? "control bit is OFF" : "duty values differ from the graph";
                    // Report only the slots we own, so the numbers shown are the ones actually compared.
                    string wrote = string.Join(",", new ArraySegment<byte>(expected, MsiWrittenDutyFirst,
                                                       MsiWrittenDutyLast - MsiWrittenDutyFirst + 1));
                    string got = string.Join(",", new ArraySegment<byte>(ec, MsiWrittenDutyFirst,
                                                     MsiWrittenDutyLast - MsiWrittenDutyFirst + 1));
                    MsiFanCheckStatus.Text = $"⚠ Mismatch ({why}).\nEC: [{string.Join(",", ec)}]{axisLine}"
                                           + $"\nDuty slots [1..6] — expected [{wrote}], got [{got}]";
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

        // ── Gyro source selector (Debug) ──────────────────────────────────────────
        // 0 = Auto (per-device default), 1 = Windows sensor, 2 = controller vendor HID.
        private const string ClawGyroSourceKey = "ClawGyroSource";
        private bool _clawGyroSourceLoading;

        /// <summary>Restore the stored choice and push it to the helper, which keeps no persistence of
        /// its own for this (it is a Debug-only override, so the widget owns the value).</summary>
        private async void RestoreClawGyroSource()
        {
            try
            {
                int mode = 0;
                var stored = Windows.Storage.ApplicationData.Current.LocalSettings.Values[ClawGyroSourceKey];
                if (stored is int i) mode = i;

                _clawGyroSourceLoading = true;
                if (ClawGyroSourceComboBox != null) ClawGyroSourceComboBox.SelectedIndex = ClampComboIndex(ClawGyroSourceComboBox, mode);
                _clawGyroSourceLoading = false;

                if (App.IsConnected)
                    await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "ClawGyroSource", mode } });
            }
            catch (Exception ex) { _clawGyroSourceLoading = false; Logger.Error($"RestoreClawGyroSource: {ex.Message}"); }
        }

        private async void ClawGyroSourceComboBox_SelectionChanged(object sender, Windows.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (_clawGyroSourceLoading) return;
            try
            {
                int mode = ClawGyroSourceComboBox?.SelectedIndex ?? 0;
                if (mode < 0) mode = 0;
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[ClawGyroSourceKey] = mode;
                if (!App.IsConnected) return;
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "ClawGyroSource", mode } });
                Logger.Info($"ClawGyroSource -> {mode}");
            }
            catch (Exception ex) { Logger.Error($"ClawGyroSourceChanged: {ex.Message}"); }
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

                // Temp label + left/right double-arrow focus marker (kept wider than tall so it reads as arrows).
                if (_msiFanTempLabels[i] != null)
                    _msiFanTempLabels[i].Foreground = new SolidColorBrush(tempSel ? (_msiFanGrabbed ? orangeG : yellow) : tempIdle);
                if (_msiFanTempHandles[i] != null)
                {
                    _msiFanTempHandles[i].Fill = new SolidColorBrush(tempSel ? (_msiFanGrabbed ? orangeG : yellow)
                                                                             : Windows.UI.ColorHelper.FromArgb(255, 255, 150, 40));
                    _msiFanTempHandles[i].Width  = tempSel ? 22 : 16;
                    _msiFanTempHandles[i].Height = tempSel ? 16 : 14;
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
            // Manual edit → light Apply instead of writing the EC now (see PointerReleased).
            if (MsiFanEnableToggle?.IsOn == true)
                MarkFanDirty();
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
