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
        // Keys. *_2 era wiped the old ×1.5 settings; *_3 adds the MSI-exact 6-slider dual-fan model.
        private const string MsiFanEnabledKey  = "MsiFan_Enabled2";
        private const string MsiFanPresetKey   = "MsiFan_Preset2";   // 0=Default 1=Quiet Idle 2=Cooling 3=Custom
        private const string MsiFanCurveKey    = "MsiFan_CurveCsv3"; // "sync|cpu0..5|gpu0..5" when custom (6-slider)
        private const string MsiFanExtendedKey = "MsiFan_Extended2"; // allow duty >75% (beyond MSI)
        // Debug-menu escape hatch: drop the {0} ∪ [floor..max] input constraint and the broken axis that
        // goes with it, so every duty 1..39 becomes settable. See _msiFanNoScaleBreak.
        private const string MsiFanNoScaleBreakKey = "MsiFan_NoScaleBreak";

        // ── MSI 6-slider fan model (matches MSI Center M's Advanced editor EXACTLY) ──────────────
        // MUST match MsiClawFanController on the helper side. SIX duty sliders per fan (Fan 1 = CPU,
        // Fan 2 = GPU, independently settable); slider 1 = idle-zone duty (temp label 0 °C, may be 0 =
        // fan off at idle). The temperature labels are the EC's own per-model axis (helper-pushed),
        // display-only — never edited or written. Duty is the RAW EC byte 0–150.
        internal const int MsiFanPoints = 6;
        // Fallback 6-label temperature axis (A2VM factory) until the helper pushes the live one.
        private static readonly int[] MsiTempAxisDefault = { 0, 50, 60, 70, 80, 88 };

        private static readonly int[] MsiDutyDefault   = { 0, 40, 49, 58, 67, 75 }; // A2VM factory
        // Presets are already floor-consistent ({0}∪[40..]) so display == what the helper writes to the EC.
        // Any running (non-idle) point below the 40 % min-spin floor would spin at the identical minimum but
        // show a snapped-up bar, tripping a false "Check applied values" mismatch — so bake the floor in here.
        private static readonly int[] MsiDutyQuietIdle = { 0, 40, 40, 45, 60, 75 }; // idle off, then gentle ramp from floor
        private static readonly int[] MsiDutyCooling   = { 40, 45, 58, 68, 75, 75 }; // fan on at idle (floor), earlier ramp; capped at the 75% MSI max (no Extended needed)
        // DEBUG preset "EC Sport default": firmware BestPerformance table sampled onto the 6 sliders for
        // DISPLAY. The helper actually writes the raw firmware table + Sport; this is only the graph.
        private static readonly int[] MsiDutyEcSport   = { 0, 23, 34, 52, 68, 85 };

        // Temp labels are display-only (fixed EC axis). Kept as fields only so the render/clamp code that
        // referenced a min/max still compiles; no editing happens (MsiFanTempAxisEditable = false).
        private const int MsiTempMin = 0;
        private const int MsiTempMax = 120;

        // ── Per-device duty scale (A2VM/EX now; A1M/A8 later) ────────────────────────────────────
        // The EC duty is a RAW byte and is DISPLAYED as-is ("%" = the raw byte, exactly like MSI Center M).
        // Two device-specific anchors shape the PLOT geometry (not the numbers) so each device's axis matches
        // what ITS fan can do:
        //   • RefDuty = the duty MSI Center M treats as its Advanced-curve MAX on this model — the top of the
        //               main plot zone. On the A2VM and the Claw 8 EX that is 75 (≈ half fan / MSI's own cap).
        //   • Ceiling = the fan's PHYSICAL max duty (full ~8690 rpm), exposed behind the Extended toggle.
        //               On the A2VM and EX that is 150.
        // TO ADD A1M / A8 (or any new model): return that model's own RefDuty (MSI Center M's Adv_Fan cap for
        // the model) and Ceiling (the fan's capability / spec max RPM mapped back through the duty→RPM anchors)
        // below. Nothing else needs touching — the whole axis re-derives from these two.
        private int MsiRefDuty()        => 75;   // A2VM & EX: MSI's Advanced-curve max = top of the main zone.
        private int MsiFanCeilingDuty() => 150;  // A2VM & EX: physical fan max (~8690 rpm) = Extended ceiling.

        private bool _msiFanExtended;
        private int MsiDutyMax() => _msiFanExtended ? MsiFanCeilingDuty() : MsiRefDuty();

        // The graph's MAIN zone maps floor..RefDuty (the usable band) to most of the plot height. The 0..floor
        // dead zone (all min-spin, and unreachable thanks to the snap) is COMPRESSED into a thin strip at the
        // very bottom with a broken-axis marker, so stepping from 0 (off) to the floor is a small visual jump
        // — not a big leap. The optional Extended range (RefDuty..Ceiling) is a compressed PURPLE strip at the
        // very top; the >RefDuty part of a bar is drawn purple there.
        private double MsiPrimaryTopDuty => MsiRefDuty();
        // Fraction of the (non-purple) height reserved for the compressed 0..floor dead-zone strip at the
        // bottom — small, so the 0→floor threshold reads as a short step across the axis break.
        // With the scale break switched off the strip gets exactly its proportional share (floor/RefDuty),
        // which is what makes MsiDutyToY/MsiYToDuty degenerate into one straight line — no special case
        // needed in either of them.
        private double MsiDeadZoneFrac()
            => _msiFanNoScaleBreak ? MsiFanMinEffectiveDuty() / (double)MsiRefDuty() : 0.10;
        // Fraction of the plot height reserved for the purple (Extended) strip (0 when extended is off, so
        // the main zone then fills everything above the dead-zone strip).
        private double MsiPurpleFrac() => _msiFanExtended ? 0.24 : 0.0;

        // True on the Claw 8 EX (Panther Lake) — used only to pick the duty→RPM anchor table for the
        // Y-axis "(rpm)" labels. NOT a duty floor any more (MSI has none; slider 1 may be 0).
        private bool IsClaw8ExWidget()
        {
            var n = deviceDisplayName?.Value;
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("CG3EM", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("1T91",  StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("8 EX",  StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("8EX",   StringComparison.OrdinalIgnoreCase) >= 0;
        }

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

        // ── Dead-zone skip (min-spin floor) ─────────────────────────────────────────────
        // The fan RPM is FLAT from ~duty 1 up to this floor — every value in that band produces the
        // identical minimum spin (~2450 rpm A2VM / ~2630 rpm EX, measured; see the anchor tables above and
        // the EC-tach logs). So any non-zero duty below the floor is redundant with the floor. The slider
        // therefore only rests at {0 = off} ∪ [floor..max]: dragging up from 0 jumps straight to the floor
        // (quietest running speed), and everything above the floor stays free/granular exactly like MSI.
        // 0 remains selectable (MSI's legit "fan off at idle", proven functional). This is a UI-input
        // constraint ONLY — the helper still writes whatever 6 values we send, byte-faithful to MSI.
        // 40 is the top of the measured min-spin plateau on both the A2VM and the Claw 8 EX.
        private int MsiFanMinEffectiveDuty() => 40;

        // Debug-menu override for everything the paragraph above describes. ON = the dead zone stops being
        // skipped: 1..39 become settable, the axis is drawn straight (no compressed strip, no zigzag) and
        // the D-Pad steps 1 % at a time across the whole range. Nothing about the WRITE changes — the helper
        // always wrote whatever six values we send, so the constraint was only ever an input rule. The
        // measurement it was built on still stands: below ~40 the fan sits on its minimum-spin plateau, so
        // 20 and 40 produce the same RPM. This lets a tester confirm that per model instead of taking it on
        // trust. Off by default; global (not per profile), like the extended-range toggle.
        private bool _msiFanNoScaleBreak;

        /// <summary>Snap a raw duty to the allowed set {0} ∪ [floor..max]: below half the floor → 0 (off),
        /// in the dead zone → the floor, at/above the floor → the value (clamped to max). With the scale
        /// break disabled this degrades to a plain clamp, so every intermediate value survives.</summary>
        private int SnapMsiDuty(double raw)
        {
            int floor = MsiFanMinEffectiveDuty();
            int max = MsiDutyMax();
            int v = (int)Math.Round(raw);
            if (v <= 0) return 0;
            if (_msiFanNoScaleBreak) return Math.Min(max, v);
            if (v >= floor) return Math.Min(max, v);
            return v * 2 < floor ? 0 : floor; // dead zone: snap to whichever end is nearer
        }

        /// <summary>Snap every point of a curve in place to the allowed set (used when loading presets and
        /// stored custom curves, so the displayed bars match what the slider can actually be set to).</summary>
        private void SnapMsiCurveInPlace(int[] duties)
        {
            if (duties == null) return;
            for (int i = 0; i < duties.Length; i++) duties[i] = SnapMsiDuty(duties[i]);
        }

        private readonly int[] _msiFanTemps = (int[])MsiTempAxisDefault.Clone();
        // Dual-fan duty backing store (6 sliders each). The canvas/editor always operates on the ACTIVE
        // fan via the _msiFanDuties property, so all the render/keyboard/drag code stays fan-agnostic.
        // In SYNC mode only CPU is edited and GPU mirrors it on apply; in SEPARATE mode a CPU/GPU
        // selector flips _msiFanEditingGpu to edit each fan's own array.
        private readonly int[] _msiFanDutiesCpu = (int[])MsiDutyDefault.Clone();
        private readonly int[] _msiFanDutiesGpu = (int[])MsiDutyDefault.Clone();
        private bool _msiFanSeparate;    // false = one curve drives both fans (default); true = per-fan
        private bool _msiFanEditingGpu;  // which fan the graph currently edits (only meaningful when separate)
        private int[] _msiFanDuties => _msiFanEditingGpu ? _msiFanDutiesGpu : _msiFanDutiesCpu;
        // MSI-style fixed evenly-spaced BARS (not a positional temperature axis). Bar height = fan %
        // (vertical edit via the circle on top). The temperature is shown as a label UNDER each bar
        // (horizontal edit). Horizontal %-gridlines + Y labels give the scale.
        // Gridline %-values are computed per axis via MsiGridPctAt(g) (dynamic 0–100 / 0–150).
        private static readonly uint[] MsiGridColor = { 0x6FB7FF, 0x8FD06A, 0xE6C84A, 0xF0A030, 0xF0603C };
        // Per-bar temperature colour scale (cold→hot), indexed by the 6 breakpoints: blue→cyan→green→
        // yellow→orange→red. The bar's colour encodes WHERE on the temperature axis it sits, not its duty.
        private static readonly uint[] MsiBarColor = { 0x3FA0FF, 0x24C6D0, 0x54D25A, 0xE8C63E, 0xF29A2E, 0xF04A3C };
        // Extended-range (>75 %) overflow segment, drawn above the red end of the scale.
        // static readonly (not const): a const would be compile-time folded and the (byte) cast would
        // overflow in the checked constant context.
        private static readonly uint MsiBarExtColor = 0xB061F5; // purple
        private readonly Windows.UI.Xaml.Shapes.Rectangle[] _msiFanBars = new Windows.UI.Xaml.Shapes.Rectangle[MsiFanPoints];
        private readonly Windows.UI.Xaml.Shapes.Rectangle[] _msiFanBarsExt = new Windows.UI.Xaml.Shapes.Rectangle[MsiFanPoints]; // purple >75 % overflow
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

        // Device firmware defaults pushed by the helper (OnMsiFanState): the live 6-label temperature axis
        // and the per-model "MSI Default" 6-slider duty. Used so presets reflect the REAL device (correct
        // on the EX, whose factory curve differs from the A2VM constants). Null until the helper pushes them.
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
                // Read straight from storage rather than waiting for RestoreMsiFanNoScaleBreak: LoadCurveForPreset
                // below snaps through SnapMsiDuty, so the flag has to be right BEFORE that runs or a stored
                // sub-floor curve would be rounded away by the very load that is meant to show it.
                _msiFanNoScaleBreak = settings.Values.TryGetValue(MsiFanNoScaleBreakKey, out var nbObj) && nbObj is bool nb && nb;

                // Restore the curve for the selected preset (custom from storage; presets from constants).
                LoadCurveForPreset(preset);

                if (MsiFanEnableToggle != null) MsiFanEnableToggle.IsOn = enabled;
                if (MsiFanExtendedRangeToggle != null) MsiFanExtendedRangeToggle.IsOn = _msiFanExtended;
                if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.SelectedIndex = preset;
                if (MsiFanContent != null) MsiFanContent.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                SyncFanModeUi();
            }
            finally
            {
                _msiFanInitializing = false;
            }

            RenderMsiFanCurve();
            ClearFanDirty();
            // The block above reloaded the GLOBAL curve out of LocalSettings. If the helper has told us
            // a game's curve is what the EC is actually running, put that back on the sliders.
            ReapplyHelperFanCurveAfterReload();
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
            // "value|sync|cpu6|gpu6|axis6|default6". value: -1 firmware, 0 Default, 1 Quiet, 2 Cooling, 3 Custom.
            var parts = payload.Split('|');
            if (!int.TryParse(parts[0], out int value)) return;
            bool sync = parts.Length <= 1 || parts[1].Trim() != "0";
            string cpuCsv = parts.Length > 2 ? parts[2] : "";
            string gpuCsv = parts.Length > 3 ? parts[3] : "";
            if (parts.Length > 4) _msiModelTemps = ParseIntsN(parts[4], MsiFanPoints, 0, 120) ?? _msiModelTemps;
            if (parts.Length > 5) _msiModelDuty  = ParseIntsN(parts[5], MsiFanPoints, 0, 150) ?? _msiModelDuty;

            bool enabled = value >= 0;
            int preset = (value >= 0 && value <= 4) ? value : 0;

            _msiFanInitializing = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[MsiFanEnabledKey] = enabled;
                if (value >= 0) settings.Values[MsiFanPresetKey] = preset;
                // Persist the custom curve locally so a reopen before the next push still shows it.
                if (value == 3 && !string.IsNullOrEmpty(cpuCsv))
                    settings.Values[MsiFanCurveKey] = (sync ? "1" : "0") + "|" + cpuCsv + "|" + (string.IsNullOrEmpty(gpuCsv) ? cpuCsv : gpuCsv);

                LoadCurveForPreset(preset);
                if (MsiFanEnableToggle != null) MsiFanEnableToggle.IsOn = enabled;
                if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.SelectedIndex = preset;
                if (MsiFanContent != null) MsiFanContent.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                SyncFanModeUi();
            }
            finally
            {
                _msiFanInitializing = false;
            }
            RenderMsiFanCurve();
            ClearFanDirty(); // helper-pushed state is authoritative → no pending edits
            Logger.Info($"OnMsiFanState applied: value={value} enabled={enabled} preset={preset} sync={sync}");
            // This push carries the GLOBAL preset/curve. It arrives on connect right before the scope
            // report, but also on its own later — so restore the running scope's curve afterwards.
            ReapplyHelperFanCurveAfterReload();
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
                    LineHeight = 15,
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
                // Thin bar (a "stick"), coloured by its position on the temperature scale (cold→hot).
                uint bc = MsiBarColor[i];
                var bar = new Windows.UI.Xaml.Shapes.Rectangle
                {
                    RadiusX = 2,
                    RadiusY = 2,
                    IsHitTestVisible = false,
                    Fill = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, (byte)(bc >> 16), (byte)(bc >> 8), (byte)bc))
                };
                _msiFanBars[i] = bar;
                MsiFanCurveCanvas.Children.Add(bar);

                // Purple overflow segment: the >75 % part of a bar in Extended mode, stacked above the
                // coloured (temperature) part. Collapsed unless the bar actually exceeds 75 %.
                var barExt = new Windows.UI.Xaml.Shapes.Rectangle
                {
                    RadiusX = 2,
                    RadiusY = 2,
                    IsHitTestVisible = false,
                    Visibility = Windows.UI.Xaml.Visibility.Collapsed,
                    Fill = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(
                        255, (byte)(MsiBarExtColor >> 16), (byte)(MsiBarExtColor >> 8), (byte)MsiBarExtColor))
                };
                _msiFanBarsExt[i] = barExt;
                MsiFanCurveCanvas.Children.Add(barExt);

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

                // Fan-% label above the bar (2 lines: "%" then the estimated RPM — filled in RenderMsiFanCurve).
                // Larger fonts than default: the Game Bar is narrow, the small text was hard to read.
                var label = new TextBlock
                {
                    FontSize = 17,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    LineHeight = 19,
                    TextAlignment = Windows.UI.Xaml.TextAlignment.Center,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    IsHitTestVisible = false
                };
                Canvas.SetZIndex(label, 11);
                _msiFanValueLabels[i] = label;
                MsiFanCurveCanvas.Children.Add(label);

                // Temperature label UNDER the bar (white, per request).
                var tlabel = new TextBlock
                {
                    FontSize = 14,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
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
        private int[] ModelTemps() => (_msiModelTemps != null && _msiModelTemps.Length == MsiFanPoints) ? _msiModelTemps : MsiTempAxisDefault;
        private int[] ModelDefaultDuty() => (_msiModelDuty != null && _msiModelDuty.Length == MsiFanPoints) ? _msiModelDuty : MsiDutyDefault;

        /// <summary>Load the 6-slider curve for a preset into the model arrays. Presets apply the SAME
        /// curve to both fans (synced). The temperature axis is always the fixed EC axis (display-only).</summary>
        private void LoadCurveForPreset(int preset)
        {
            // Temp labels are always the device axis (fixed, not per-preset).
            Array.Copy(ModelTemps(), _msiFanTemps, MsiFanPoints);

            if (preset == 3 && LoadCustomCurveFromStorage(out int[] cpu, out int[] gpu, out bool sep))
            {
                _msiFanSeparate = sep;
                _msiFanEditingGpu = false; // start on CPU when (re)loading
                Array.Copy(cpu, _msiFanDutiesCpu, MsiFanPoints);
                Array.Copy(gpu, _msiFanDutiesGpu, MsiFanPoints);
                // Snap any legacy dead-zone values (curves saved before the min-spin floor existed) onto the
                // allowed set — RPM-neutral (the dead zone all spins at min) and keeps display == settable.
                SnapMsiCurveInPlace(_msiFanDutiesCpu);
                SnapMsiCurveInPlace(_msiFanDutiesGpu);
                return;
            }

            int[] duties;
            switch (preset)
            {
                case 1: duties = MsiDutyQuietIdle; break;
                case 2: duties = MsiDutyCooling;   break;
                case 4: duties = MsiDutyEcSport;   break; // debug: EC Sport default (display)
                default: duties = ModelDefaultDuty(); break; // 0/3-fallback = MSI Default
            }
            // Presets are synced: both fans get the same curve.
            _msiFanSeparate = false;
            _msiFanEditingGpu = false;
            Array.Copy(duties, _msiFanDutiesCpu, MsiFanPoints);
            Array.Copy(duties, _msiFanDutiesGpu, MsiFanPoints);
            // Snap the preset onto the allowed set so a preset never shows a bar the slider can't reproduce.
            // No-op for real MSI factory curves (idle 0 or ≥ floor); only our synthetic quiet/cooling presets
            // with sub-floor points get bumped to the floor — RPM-identical, just honest about the dead zone.
            SnapMsiCurveInPlace(_msiFanDutiesCpu);
            SnapMsiCurveInPlace(_msiFanDutiesGpu);
        }

        /// <summary>Load the stored 6-slider custom curve "sync|cpu0..5|gpu0..5". Returns false if absent
        /// or malformed (the caller then falls back to the MSI Default).</summary>
        private bool LoadCustomCurveFromStorage(out int[] cpu, out int[] gpu, out bool separate)
        {
            cpu = null; gpu = null; separate = false;
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(MsiFanCurveKey, out var o)
                    && o is string csv)
                {
                    var parts = csv.Split('|');
                    if (parts.Length == 3)
                    {
                        separate = parts[0].Trim() == "0"; // "1"=sync, "0"=separate
                        cpu = ParseIntsN(parts[1], MsiFanPoints, 0, 150);
                        gpu = ParseIntsN(parts[2], MsiFanPoints, 0, 150);
                        if (cpu != null && gpu != null) return true;
                    }
                }
            }
            catch (Exception ex) { Logger.Debug($"LoadCustomCurveFromStorage: {ex.Message}"); }
            cpu = null; gpu = null;
            return false;
        }

        // CaptureMsiFanIntoProfile was REMOVED on 2026-08-02, together with the widget-side per-game fan
        // store (PerformanceProfile.MsiFanPreset / .MsiFanCurve).
        //
        // It ran from the generic SaveCurrentSettingsToProfile, so ANY unrelated setting being saved
        // stamped whatever the fan editor happened to be showing into whatever profile happened to be
        // active — the same shape as the gyro and LED clobbers: an implicit capture riding along on
        // someone else's save. The per-game curve now lives in the helper's GameProfile like TDP, and the
        // ONLY thing that writes it is this card's Apply button. Do not reintroduce an implicit capture.

        /// <summary>Serialize the dual-fan model as "sync|cpu0..5|gpu0..5" — the wire + storage format.
        /// When synced, GPU is written equal to CPU so a later separate-toggle starts coherent.</summary>
        private string CurveToCsv()
        {
            int[] cpu = _msiFanDutiesCpu;
            int[] gpu = _msiFanSeparate ? _msiFanDutiesGpu : _msiFanDutiesCpu;
            return (_msiFanSeparate ? "0" : "1") + "|"
                   + string.Join(",", cpu.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "|"
                   + string.Join(",", gpu.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>Parse exactly <paramref name="count"/> comma-separated ints, each clamped to [lo,hi].
        /// Returns null if the string isn't that many valid ints.</summary>
        private static int[] ParseIntsN(string csv, int count, int lo, int hi)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;
            var p = csv.Split(',');
            if (p.Length != count) return null;
            var r = new int[count];
            for (int i = 0; i < count; i++)
                if (!int.TryParse(p[i], NumberStyles.Any, CultureInfo.InvariantCulture, out int v)) return null;
                else r[i] = Math.Max(lo, Math.Min(hi, v));
            return r;
        }

        // EnforceDutyStaircase / EnforceDutyFloor removed: MSI enforces NO monotonicity or floor on the
        // custom sliders (e.g. GPU 0;0;0;40;50;69), and the hidden floor was the fan-off bug's root cause.
        // Edits now clamp only to [0, MsiDutyMax()]; each slider is free and independent.

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
            UpdateFanApplyScopeLabel();
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

            // "Unsaved changes" indicator shares the Apply button's dirty state: shown + pulsing while
            // there are pending edits, hidden (and its blink stopped) when clean.
            if (MsiFanUnsavedBadge != null)
            {
                try
                {
                    MsiFanUnsavedBlink?.Stop();
                    MsiFanUnsavedBadge.Opacity = 1.0;
                    MsiFanUnsavedBadge.Visibility = on
                        ? Windows.UI.Xaml.Visibility.Visible
                        : Windows.UI.Xaml.Visibility.Collapsed;
                    if (on) MsiFanUnsavedBlink?.Begin();
                }
                catch
                {
                    MsiFanUnsavedBadge.Visibility = on
                        ? Windows.UI.Xaml.Visibility.Visible
                        : Windows.UI.Xaml.Visibility.Collapsed;
                }
            }
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

        /// <summary>Live fan telemetry line, fed by the 1 Hz Quick Metrics push (fan1/fan2 RPM + each
        /// fan's live commanded duty %, from the EC — the same source the RTSS OSD uses). Fan 1 (CPU)
        /// always shows; Fan 2 (GPU) is revealed only when the EC reports a valid reading for it, so a
        /// firmware that only populates fan 1 keeps a clean single-fan line. Duty shown as "(NN%)" next
        /// to each RPM; omitted when the duty read is unavailable (-1).</summary>
        internal void UpdateMsiFanTelemetry(double rpm1, double rpm2, double duty1, double duty2)
        {
            if (MsiFanRpmLabel != null)
                MsiFanRpmLabel.Text = $"{(rpm1 > 0 ? (int)Math.Round(rpm1) : 0)} RPM";
            if (MsiFan1DutyLabel != null)
                MsiFan1DutyLabel.Text = duty1 >= 0 ? $"({(int)Math.Round(duty1)}%)" : "";

            // Fan 2 (GPU): show only when the EC gives us a real reading (RPM > 0 or a valid duty).
            bool fan2Valid = rpm2 > 0 || duty2 >= 0;
            if (MsiFan2Panel != null)
                MsiFan2Panel.Visibility = fan2Valid ? Visibility.Visible : Visibility.Collapsed;
            if (fan2Valid)
            {
                if (MsiFan2RpmLabel != null)
                    MsiFan2RpmLabel.Text = $"{(rpm2 > 0 ? (int)Math.Round(rpm2) : 0)} RPM";
                if (MsiFan2DutyLabel != null)
                    MsiFan2DutyLabel.Text = duty2 >= 0 ? $"({(int)Math.Round(duty2)}%)" : "";
            }
        }

        // Plot padding: room above the bars for the % labels, below for the temp labels, left for the
        // Y-axis % labels.
        private const double MsiPlotTop = 46;   // room above the tallest bar for its 2-line "% / rpm" label
        private const double MsiPlotBottomPad = 48;   // room for the temp label + diamond focus marker
        private const double MsiPlotLeft = 14;   // Y-axis scale labels removed → only a small left margin

        /// <summary>Duty→canvas Y. Piecewise with a broken axis: 0..floor is squeezed into a thin bottom
        /// strip (dead zone), floor..RefDuty fills the main zone, RefDuty..Ceiling fills the compressed purple
        /// strip at the very top (only when extended). Inverse: <see cref="MsiYToDuty"/>.</summary>
        private double MsiDutyToY(double duty, double plotTop, double plotBottom)
        {
            double h = plotBottom - plotTop;
            double purpleH = h * MsiPurpleFrac();
            double belowPurple = h - purpleH;                   // 0..RefDuty region
            double deadH = belowPurple * MsiDeadZoneFrac();     // thin 0..floor strip at the bottom
            double mainH = belowPurple - deadH;                 // floor..RefDuty (main usable zone)
            double floor = MsiFanMinEffectiveDuty();
            double refDuty = MsiPrimaryTopDuty;
            double breakY = plotBottom - deadH;                 // top of the compressed dead strip
            double boundaryY = plotTop + purpleH;               // y at duty = RefDuty (top of main zone)

            if (duty <= floor)
            {
                double f = floor > 0 ? duty / floor : 0;
                return plotBottom - f * deadH;
            }
            if (duty <= refDuty)
            {
                double f = refDuty > floor ? (duty - floor) / (refDuty - floor) : 0;
                return breakY - f * mainH;
            }
            double fe = Math.Min(1.0, (duty - refDuty) / (MsiFanCeilingDuty() - refDuty));
            return boundaryY - fe * purpleH;
        }

        /// <summary>Canvas Y→duty, the inverse of <see cref="MsiDutyToY"/> (three-segment broken axis).</summary>
        private double MsiYToDuty(double y, double plotTop, double plotBottom)
        {
            double h = plotBottom - plotTop;
            double purpleH = h * MsiPurpleFrac();
            double belowPurple = h - purpleH;
            double deadH = belowPurple * MsiDeadZoneFrac();
            double mainH = belowPurple - deadH;
            double floor = MsiFanMinEffectiveDuty();
            double refDuty = MsiPrimaryTopDuty;
            double breakY = plotBottom - deadH;
            double boundaryY = plotTop + purpleH;

            if (y >= breakY)
            {
                double f = deadH > 0 ? (plotBottom - y) / deadH : 0;
                return Math.Max(0, Math.Min(floor, f * floor));
            }
            if (y >= boundaryY)
            {
                double f = mainH > 0 ? (breakY - y) / mainH : 0;
                return Math.Max(floor, Math.Min(refDuty, floor + f * (refDuty - floor)));
            }
            if (!_msiFanExtended) return refDuty;
            double ceiling = MsiFanCeilingDuty();
            double fe = purpleH > 0 ? (boundaryY - y) / purpleH : 0;
            return Math.Max(refDuty, Math.Min(ceiling, refDuty + fe * (ceiling - refDuty)));
        }

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

            double boundaryY = MsiDutyToY(MsiPrimaryTopDuty, plotTop, plotBottom); // y at the 75 % line

            // Faint horizontal gridlines for reference only — NO numeric Y-axis labels (removed so the
            // vertical scale is free: the per-bar % / rpm and the live readout carry the numbers, and 75 %
            // now fills the plot). The 75 % line is drawn a touch stronger as the primary/purple boundary.
            // Gridlines span the USABLE band (floor..RefDuty), so none fall inside the compressed dead strip.
            // Extended adds the purple RefDuty..Ceiling range. (Cosmetic — the lines carry no numeric labels.)
            // With the scale break off there is no dead strip to stay clear of, so the gridlines start at 0
            // and divide the whole range evenly — otherwise the bottom third of a straight axis would be bare.
            double floorD = _msiFanNoScaleBreak ? 0 : MsiFanMinEffectiveDuty();
            double refD = MsiRefDuty(), ceilD = MsiFanCeilingDuty();
            double[] gridDuties = _msiFanExtended
                ? new double[] { floorD, (floorD + refD) / 2.0, refD, (refD + ceilD) / 2.0, ceilD }
                : new double[] { floorD, floorD + (refD - floorD) * 0.25, floorD + (refD - floorD) * 0.5, floorD + (refD - floorD) * 0.75, refD };
            for (int g = 0; g < 5; g++)
            {
                double gd = gridDuties[g];
                double gy = MsiDutyToY(gd, plotTop, plotBottom);
                if (_msiFanGridLines[g] != null)
                {
                    bool boundary = Math.Abs(gd - MsiPrimaryTopDuty) < 0.5;
                    _msiFanGridLines[g].X1 = plotLeft; _msiFanGridLines[g].X2 = width;
                    _msiFanGridLines[g].Y1 = gy; _msiFanGridLines[g].Y2 = gy;
                    _msiFanGridLines[g].Stroke = new SolidColorBrush(
                        Windows.UI.ColorHelper.FromArgb((byte)(boundary ? 70 : 30), 255, 255, 255));
                }
                if (_msiFanGridLabels[g] != null)
                    _msiFanGridLabels[g].Visibility = Visibility.Collapsed; // Y-axis scale values removed
            }

            // Broken-axis marker: a small zigzag straight across the plot at the top of the compressed
            // 0..floor dead strip. Signals the axis is cut there, so 0 (off) sits just below the floor
            // instead of a full 0..floor's worth of empty scale — the 0→floor step reads as a short hop.
            if (MsiFanAxisBreak != null && _msiFanNoScaleBreak)
            {
                // Straight axis: nothing is cut, so the marker would be a lie.
                MsiFanAxisBreak.Visibility = Visibility.Collapsed;
            }
            else if (MsiFanAxisBreak != null)
            {
                double breakY = MsiDutyToY(MsiFanMinEffectiveDuty(), plotTop, plotBottom);
                var pts = new Windows.UI.Xaml.Media.PointCollection();
                const double amp = 3.0, step = 8.0;
                bool up = true;
                for (double x = plotLeft; x <= width + 0.1; x += step, up = !up)
                    pts.Add(new Windows.Foundation.Point(x, breakY + (up ? -amp : amp)));
                MsiFanAxisBreak.Points = pts;
                MsiFanAxisBreak.Visibility = Visibility.Visible;
                Canvas.SetZIndex(MsiFanAxisBreak, 4);
            }

            // Extended (>75 %) purple zone: a compressed strip at the very top, with the 75 % boundary line.
            var beyondVis = _msiFanExtended ? Visibility.Visible : Visibility.Collapsed;
            if (MsiFanBeyondBand != null)
            {
                MsiFanBeyondBand.Visibility = beyondVis;
                MsiFanBeyondBand.Fill = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(30, 176, 97, 245));
                MsiFanBeyondBand.Width = Math.Max(0, width - plotLeft);
                MsiFanBeyondBand.Height = Math.Max(0, boundaryY - plotTop);
                Canvas.SetLeft(MsiFanBeyondBand, plotLeft);
                Canvas.SetTop(MsiFanBeyondBand, plotTop);
            }
            if (MsiFanMsiMaxLine != null)
            {
                MsiFanMsiMaxLine.Visibility = beyondVis;
                Canvas.SetZIndex(MsiFanMsiMaxLine, 5);
                MsiFanMsiMaxLine.X1 = plotLeft; MsiFanMsiMaxLine.X2 = width;
                MsiFanMsiMaxLine.Y1 = boundaryY; MsiFanMsiMaxLine.Y2 = boundaryY;
            }
            if (MsiFanMsiMaxLabel != null)
            {
                MsiFanMsiMaxLabel.Visibility = beyondVis;
                MsiFanMsiMaxLabel.Text = "MSI max 75%";
                Canvas.SetZIndex(MsiFanMsiMaxLabel, 5);
                Canvas.SetLeft(MsiFanMsiMaxLabel, plotLeft + 4);
                Canvas.SetTop(MsiFanMsiMaxLabel, Math.Max(plotTop, boundaryY + 1));
            }

            // Evenly-spaced THIN bars across the plot area (temperature is NOT positional; it's the label
            // under each bar). Each bar = a temperature-coloured part (0..min(duty,75)) plus, in extended
            // mode, a purple part (75..duty) stacked on top.
            double plotW = width - plotLeft;
            double slot = plotW / MsiFanPoints;
            double barW = Math.Min(10.0, slot * 0.16);
            for (int i = 0; i < MsiFanPoints; i++)
            {
                double cx = plotLeft + (i + 0.5) * slot;
                int duty = _msiFanDuties[i];
                double yTrue = MsiDutyToY(duty, plotTop, plotBottom);                                // circle sits here
                double yPrimary = MsiDutyToY(Math.Min(duty, MsiPrimaryTopDuty), plotTop, plotBottom); // top of coloured part

                if (_msiFanBars[i] != null)
                {
                    _msiFanBars[i].Width = barW;
                    _msiFanBars[i].Height = Math.Max(0, plotBottom - yPrimary);
                    Canvas.SetLeft(_msiFanBars[i], cx - barW / 2);
                    Canvas.SetTop(_msiFanBars[i], yPrimary);
                }
                if (_msiFanBarsExt[i] != null)
                {
                    if (_msiFanExtended && duty > MsiPrimaryTopDuty)
                    {
                        _msiFanBarsExt[i].Visibility = Visibility.Visible;
                        _msiFanBarsExt[i].Width = barW;
                        _msiFanBarsExt[i].Height = Math.Max(0, yPrimary - yTrue);
                        Canvas.SetLeft(_msiFanBarsExt[i], cx - barW / 2);
                        Canvas.SetTop(_msiFanBarsExt[i], yTrue);
                    }
                    else
                    {
                        _msiFanBarsExt[i].Visibility = Visibility.Collapsed;
                    }
                }
                if (_msiFanPoints[i] != null)
                {
                    double r = _msiFanPoints[i].Width / 2.0;
                    Canvas.SetLeft(_msiFanPoints[i], cx - r);
                    Canvas.SetTop(_msiFanPoints[i], yTrue - r);
                }
                if (_msiFanValueLabels[i] != null)
                {
                    // Two lines above the bar: the duty "%" (main) and, a touch smaller under it, the
                    // estimated RPM incl. unit — the "set" RPM the user is dialing in. The % Run carries no
                    // explicit colour so the selection highlight (which sets the TextBlock Foreground) still
                    // tints it; the RPM Run keeps its own dim colour.
                    var lbl = _msiFanValueLabels[i];
                    lbl.Inlines.Clear();
                    lbl.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = $"{duty}%" });
                    lbl.Inlines.Add(new Windows.UI.Xaml.Documents.LineBreak());
                    lbl.Inlines.Add(new Windows.UI.Xaml.Documents.Run
                    {
                        Text = $"{MsiDutyToRpm(duty)} rpm",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 205, 213, 221))
                    });
                    lbl.Width = slot;                         // confine to this bar's column, centered
                    Canvas.SetLeft(lbl, cx - slot / 2);
                    Canvas.SetTop(lbl, Math.Max(0, yTrue - 46));   // room for both (larger) lines above the bar top
                }
                if (_msiFanTempLabels[i] != null)
                {
                    // Slider 1 is the idle zone (label 0 °C in MSI) — show a snowflake instead of "0°C".
                    _msiFanTempLabels[i].Text = (i == 0 && _msiFanTemps[0] <= 0) ? "❄" : $"{_msiFanTemps[i]}°C";
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

            UpdateFanTempIndicator(); // reposition the live-temp line for the new geometry
        }

        private void MsiFanCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderMsiFanCurve();
            UpdateMsiFanGraphTemp(_msiFanLastTemp);
        }

        private double _msiFanLastTemp;

        /// <summary>Positions the dashed vertical "current temperature" line behind the bars: the live CPU
        /// temp is interpolated between the fixed breakpoints (bar centers) to an X. Hidden when there is no
        /// valid temp reading. Drawn behind the bars (ZIndex −1) so it shows through the gaps between them.</summary>
        private void UpdateFanTempIndicator()
        {
            if (MsiFanTempIndicatorLine == null || MsiFanCurveCanvas == null) return;
            double width = MsiFanCurveCanvas.ActualWidth;
            double height = MsiFanCurveCanvas.ActualHeight;
            double tempC = _msiFanLastTemp;
            if (!_msiFanPointsBuilt || width <= 0 || height <= 0 || tempC <= 0)
            {
                MsiFanTempIndicatorLine.Visibility = Visibility.Collapsed;
                return;
            }

            double plotTop = MsiPlotTop;
            double plotBottom = height - MsiPlotBottomPad;
            double plotLeft = MsiPlotLeft;
            if (plotBottom <= plotTop) { MsiFanTempIndicatorLine.Visibility = Visibility.Collapsed; return; }

            double slot = (width - plotLeft) / MsiFanPoints;
            double Cx(int i) => plotLeft + (i + 0.5) * slot;
            int last = MsiFanPoints - 1;

            double x;
            if (tempC <= _msiFanTemps[0]) x = Cx(0);
            else if (tempC >= _msiFanTemps[last]) x = Cx(last);
            else
            {
                x = Cx(last);
                for (int i = 1; i < MsiFanPoints; i++)
                    if (tempC <= _msiFanTemps[i])
                    {
                        double denom = _msiFanTemps[i] - _msiFanTemps[i - 1];
                        double f = denom > 0 ? (tempC - _msiFanTemps[i - 1]) / denom : 0;
                        x = Cx(i - 1) + f * (Cx(i) - Cx(i - 1));
                        break;
                    }
            }

            Canvas.SetZIndex(MsiFanTempIndicatorLine, -1); // behind the bars, shows through the gaps
            MsiFanTempIndicatorLine.Visibility = Visibility.Visible;
            MsiFanTempIndicatorLine.X1 = x; MsiFanTempIndicatorLine.X2 = x;
            MsiFanTempIndicatorLine.Y1 = plotTop; MsiFanTempIndicatorLine.Y2 = plotBottom;
        }

        /// <summary>
        /// Draws the live CPU package-temperature indicator (vertical line + label) on the fan
        /// graph, mapping 0…100 °C across the canvas width. Fed by the Quick Metrics cpuTemp feed.
        /// </summary>
        private void UpdateMsiFanGraphTemp(double tempC)
        {
            _msiFanLastTemp = tempC;
            if (MsiFanTempLabel != null)
                MsiFanTempLabel.Text = tempC > 0 ? $"{tempC:F0}°C" : "--°C";

            // Live CPU-temp indicator: a dashed vertical line drawn behind the bars at the X interpolated
            // between the temperature breakpoints, so the user sees roughly which bracket the CPU sits in.
            UpdateFanTempIndicator();

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
                    ? Windows.UI.ColorHelper.FromArgb(255, 255, 150, 60) // live bracket the CPU is currently in
                    : Windows.UI.Colors.White);                          // idle temp label (white, per request)
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
                // Inverse of the piecewise MsiDutyToY (primary 0..75 + purple 75..150 when extended).
                double duty = MsiYToDuty(point.Y, plotTop, plotBottom);
                // Free/granular above the floor; below it the slider snaps to 0 (off) or the min-spin floor,
                // skipping the RPM-flat dead zone (MSI enforces no monotonicity — only this dead-zone skip).
                _msiFanDuties[_msiFanDragIndex] = SnapMsiDuty(duty);
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
                    if (_msiFanDuties[i] > MsiRefDuty()) { _msiFanDuties[i] = MsiRefDuty(); changed = true; }
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

            // A selection we made ourselves, delivered late — see SetPresetComboProgrammatically.
            // Consumed once, so a real user selection right afterwards still gets through.
            if (_pendingProgrammaticPreset >= 0 &&
                _pendingProgrammaticPreset == (MsiFanPresetComboBox?.SelectedIndex ?? -1))
            {
                _pendingProgrammaticPreset = -1;
                return;
            }
            _pendingProgrammaticPreset = -1;

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
                // Down → the per-fan selector when Separate is on (it sits between preset and graph),
                // otherwise straight into the curve graph for controller point editing.
                if (IsFanSelectorFocusable())
                    MsiFanSelectComboBox.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else if (MsiFanCurveFocus != null)
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
                    // Duty circle: vertical only. Granular 1-step above the floor; at the floor a step down
                    // goes straight to 0 (off) and a step up from 0 jumps to the floor — the RPM-flat dead
                    // zone between is skipped (see SnapMsiDuty). MSI still enforces no monotonicity.
                    if (up || down)
                    {
                        int cur = _msiFanDuties[idx];
                        int floor = MsiFanMinEffectiveDuty();
                        _msiFanDuties[idx] = _msiFanNoScaleBreak
                            // Scale break off: plain 1 % steps over the whole range, no jump at the floor.
                            ? (up ? Math.Min(MsiDutyMax(), cur + 1) : Math.Max(0, cur - 1))
                            : up
                            ? (cur < floor ? floor : Math.Min(MsiDutyMax(), cur + 1))
                            : (cur <= floor ? 0 : cur - 1);
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
                else FanRowAboveGraph()?.Focus(Windows.UI.Xaml.FocusState.Keyboard);                 // leave up to the fan selector / preset
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

        /// <summary>The 8-byte Get_Fan table we expect to see for the CPU (block 1) fan: the EC writes the
        /// 6 sliders into payload[1..6], preserving payload[0] (live duty) and payload[7] (EC top-duty). We
        /// verify ONLY payload[1..6] — the boundary bytes are the EC's, not ours. Uses the CPU curve (the
        /// helper's ReportMsiFanStatus reads block 1).</summary>
        private byte[] MsiExpectedTable()
        {
            byte D(int i) => (byte)Math.Max(0, Math.Min(150, _msiFanDutiesCpu[i]));
            return new byte[8] { 0, D(0), D(1), D(2), D(3), D(4), D(5), 0 };
        }

        // The 6 slider slots in the EC block = payload[1..6]. index 0 (live duty) and 7 (EC top-duty) are
        // firmware state and are never compared.
        private const int MsiWrittenDutyFirst = 1;
        private const int MsiWrittenDutyLast = 6;
        private const int MsiWrittenDutyCount = MsiWrittenDutyLast - MsiWrittenDutyFirst + 1; // = 6 sliders

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
                int rpm2 = -1;
                if (sections.Length > 6) int.TryParse(sections[6], out rpm2);

                // Measurement line: both fans' RPM + whether the EC full-speed override is engaged. Fan 2
                // is only appended when the EC reports a real reading for it (n/a on firmware that doesn't
                // populate it), so the CPU-only case stays a clean single-fan line.
                string measure = (rpm >= 0 ? $"Fan 1: {rpm} RPM" : "Fan 1: n/a")
                                 + (rpm2 > 0 ? $" · Fan 2: {rpm2} RPM" : "")
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

                // Show ONLY the 6 slider duties (EC payload[1..6]); index 0/7 are EC boundary state. The
                // temperature line is the fixed model axis (6 labels), not the raw Get_Thermal padding.
                string dutyList = string.Join(", ", new ArraySegment<byte>(ec, MsiWrittenDutyFirst, MsiWrittenDutyCount));
                string tempList = string.Join(", ", _msiFanTemps);
                string tempLine = $"\nTemps: {tempList} °C";
                _ = th; // Get_Thermal readback retained in the payload for diagnostics; not shown

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

                if (!readOk)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 230, 120, 120));
                    MsiFanCheckStatus.Text = "✗ Could not read fan values from the EC.";
                }
                else if (enabled && preset == 4)
                {
                    // DEBUG "EC Sport default": firmware hardware table drives the fan (control OFF is
                    // correct). Don't compare against the software-curve model — just show the 5 points.
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 120, 200, 230));
                    MsiFanCheckStatus.Text = $"EC Sport default (debug) — firmware drives the fan (control bit off is correct).\nFan %: {dutyList}{tempLine}";
                }
                else if (!enabled)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 200, 200, 200));
                    MsiFanCheckStatus.Text = $"Custom fan curve is OFF (firmware control).\nFan %: {dutyList}{tempLine}";
                }
                else if (match && controlOn)
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 120, 210, 120));
                    MsiFanCheckStatus.Text = $"✓ Applied correctly — EC matches the graph and control is active.\nFan %: {dutyList}{tempLine}";
                }
                else
                {
                    MsiFanCheckStatus.Foreground = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 240, 180, 80));
                    string why = !controlOn ? "control bit is OFF" : "duty values differ from the graph";
                    // Show the same 5 points, expected vs actual, so the numbers are the ones compared.
                    string wrote = string.Join(", ", new ArraySegment<byte>(expected, MsiWrittenDutyFirst, MsiWrittenDutyCount));
                    string got = string.Join(", ", new ArraySegment<byte>(ec, MsiWrittenDutyFirst, MsiWrittenDutyCount));
                    MsiFanCheckStatus.Text = $"⚠ Mismatch ({why}).\nFan % — expected {wrote}, got {got}{tempLine}";
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

        // ── Per-game gyro engine selector (Debug) ─────────────────────────────────
        // 0 = Auto (per-device: Virtual on AI, Firmware on EX), 1 = Virtual (software), 2 = Firmware.
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

        // ── Fan: 0–40 % scale break (debug menu) ─────────────────────────────────
        // Widget-local and global, same shape as the gyro-source override above: the helper keeps no copy
        // because it never enforced the constraint in the first place — it writes the six bytes it is given.
        private bool _msiFanNoScaleBreakLoading;

        /// <summary>Put the stored choice on the debug toggle. The field itself is read earlier, inside
        /// InitializeMsiFanCard, because the curve load depends on it.</summary>
        private void RestoreMsiFanNoScaleBreak()
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(MsiFanNoScaleBreakKey, out var o)
                    && o is bool b)
                    _msiFanNoScaleBreak = b;

                _msiFanNoScaleBreakLoading = true;
                if (MsiFanNoScaleBreakToggle != null) MsiFanNoScaleBreakToggle.IsOn = _msiFanNoScaleBreak;
                _msiFanNoScaleBreakLoading = false;
            }
            catch (Exception ex)
            {
                _msiFanNoScaleBreakLoading = false;
                Logger.Error($"RestoreMsiFanNoScaleBreak: {ex.Message}");
            }
        }

        private void MsiFanNoScaleBreakToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiFanNoScaleBreakLoading) return;
            _msiFanNoScaleBreak = MsiFanNoScaleBreakToggle?.IsOn ?? false;
            ApplicationData.Current.LocalSettings.Values[MsiFanNoScaleBreakKey] = _msiFanNoScaleBreak;

            // Turning the constraint back ON has to bring the curve with it: a bar sitting at 25 would
            // otherwise stay on screen at a value the editor can no longer produce, and the next Check
            // would read that as a mismatch. Same handling as the extended-range toggle's clamp — persist
            // and light Apply rather than writing the EC behind the user's back.
            bool changed = false;
            if (!_msiFanNoScaleBreak)
            {
                foreach (var duties in new[] { _msiFanDutiesCpu, _msiFanDutiesGpu })
                    for (int i = 0; i < MsiFanPoints; i++)
                    {
                        int snapped = SnapMsiDuty(duties[i]);
                        if (snapped != duties[i]) { duties[i] = snapped; changed = true; }
                    }
            }
            RenderMsiFanCurve();

            if (changed)
            {
                if ((MsiFanPresetComboBox?.SelectedIndex ?? 0) == 3)
                    ApplicationData.Current.LocalSettings.Values[MsiFanCurveKey] = CurveToCsv();
                if (MsiFanEnableToggle?.IsOn == true) MarkFanDirty();
            }
            Logger.Info($"MsiFan scale break {(_msiFanNoScaleBreak ? "DISABLED (1..39 settable)" : "enabled")}"
                        + (changed ? " - curve snapped back onto the allowed set" : ""));
        }

        // ── Gravity-relative gyro ────────────────────────────────────────────────
        // Widget-local and global on purpose, not a per-game profile field: it changes how the
        // gyro axes are derived, which is orthogonal to the engine and to every per-game gyro
        // setting, and the per-game gyro sync path has known issues (see CLAUDE.md) that this has
        // no reason to touch. Same persistence pattern as the gyro-source selector above — the
        // helper keeps no copy, the widget owns the value and pushes it on connect.
        private const string ClawGyroWorldSpaceKey = "ClawGyroWorldSpace";
        private bool _clawGyroWorldSpaceLoading;

        // Starting value for installs that have never touched the toggle. Per-model, pushed by the
        // helper (DeviceGyroWorldSpaceDefault): on everywhere except the Claw 8 EX. Until it arrives
        // this stays true, which is what every device did before the capability existed.
        private bool _clawGyroWorldSpaceDeviceDefault = true;

        private async void RestoreClawGyroWorldSpace()
        {
            try
            {
                // A stored user choice always wins; otherwise take the device's default.
                bool on = _clawGyroWorldSpaceDeviceDefault;
                var stored = Windows.Storage.ApplicationData.Current.LocalSettings.Values[ClawGyroWorldSpaceKey];
                if (stored is bool b) on = b;

                _clawGyroWorldSpaceLoading = true;
                if (GyroWorldSpaceToggle != null) GyroWorldSpaceToggle.IsOn = on;
                _clawGyroWorldSpaceLoading = false;

                if (App.IsConnected)
                    await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                        { { "ClawGyroWorldSpace", on ? "1" : "0" } });
            }
            catch (Exception ex) { _clawGyroWorldSpaceLoading = false; Logger.Error($"RestoreClawGyroWorldSpace: {ex.Message}"); }
        }

        /// <summary>
        /// The helper reported this model's default for the gravity-relative gyro axes. Applies it
        /// only while the user has never set the toggle themselves — and deliberately does NOT
        /// persist it, so an untouched install keeps following the device rather than freezing
        /// whatever the first connect happened to report. Ordering against
        /// RestoreClawGyroWorldSpace does not matter: both read the stored value directly.
        /// </summary>
        private async void ApplyClawGyroWorldSpaceDeviceDefault(bool deviceDefault)
        {
            try
            {
                _clawGyroWorldSpaceDeviceDefault = deviceDefault;

                if (Windows.Storage.ApplicationData.Current.LocalSettings.Values
                        .ContainsKey(ClawGyroWorldSpaceKey))
                    return;

                _clawGyroWorldSpaceLoading = true;
                if (GyroWorldSpaceToggle != null) GyroWorldSpaceToggle.IsOn = deviceDefault;
                _clawGyroWorldSpaceLoading = false;

                Logger.Info($"ClawGyroWorldSpace: no stored value, using the device default -> {deviceDefault}");

                if (App.IsConnected)
                    await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                        { { "ClawGyroWorldSpace", deviceDefault ? "1" : "0" } });
            }
            catch (Exception ex)
            {
                _clawGyroWorldSpaceLoading = false;
                Logger.Error($"ApplyClawGyroWorldSpaceDeviceDefault: {ex.Message}");
            }
        }

        private async void GyroWorldSpaceToggle_Toggled(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (_clawGyroWorldSpaceLoading) return;
            try
            {
                bool on = GyroWorldSpaceToggle?.IsOn == true;
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[ClawGyroWorldSpaceKey] = on;
                if (!App.IsConnected) return;
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                    { { "ClawGyroWorldSpace", on ? "1" : "0" } });
                Logger.Info($"ClawGyroWorldSpace -> {on}");
            }
            catch (Exception ex) { Logger.Error($"GyroWorldSpaceToggle_Toggled: {ex.Message}"); }
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
            var tempIdle = Windows.UI.Colors.White; // temp label idle (white, per request)
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

        /// <summary>Scroll the Fan tab so the live readout line (CPU temp + fan RPMs) sits near the top of
        /// the viewport, with the whole graph visible below it. Previously scrolled to the very bottom,
        /// which pushed the essential metrics above the graph out of view once the (now taller) graph took
        /// focus.</summary>
        private void ScrollMsiFanCardIntoView()
        {
            try
            {
                if (FanScrollViewer == null) return;
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                {
                    try
                    {
                        FanScrollViewer.UpdateLayout();
                        // Anchor on the readout line so it stays visible; fall back to bottom if unavailable.
                        if (MsiFanReadoutPanel != null && FanScrollViewer.Content is UIElement content)
                        {
                            var pt = MsiFanReadoutPanel
                                .TransformToVisual(content)
                                .TransformPoint(new Windows.Foundation.Point(0, 0));
                            double target = Math.Max(0, Math.Min(FanScrollViewer.ScrollableHeight, pt.Y - 8));
                            FanScrollViewer.ChangeView(null, target, null);
                        }
                        else
                        {
                            FanScrollViewer.ChangeView(null, FanScrollViewer.ScrollableHeight, null);
                        }
                    }
                    catch (Exception ex) { Logger.Debug($"ScrollMsiFanCardIntoView inner: {ex.Message}"); }
                });
            }
            catch (Exception ex) { Logger.Debug($"ScrollMsiFanCardIntoView: {ex.Message}"); }
        }

        // ── Sync / separate fan mode ────────────────────────────────────────────────
        // Default: one curve drives both fans (sync). The user can split into per-fan curves like MSI.
        // The canvas always edits the ACTIVE fan (_msiFanDuties → CPU or GPU); the selector just flips it.

        /// <summary>Reflect the current sync/separate + selected-fan state onto the UI controls.</summary>
        private void SyncFanModeUi()
        {
            if (MsiFanSeparateToggle != null) MsiFanSeparateToggle.IsOn = _msiFanSeparate;
            if (MsiFanSelectPanel != null)
                MsiFanSelectPanel.Visibility = _msiFanSeparate ? Visibility.Visible : Visibility.Collapsed;
            if (MsiFanSelectComboBox != null)
                MsiFanSelectComboBox.SelectedIndex = _msiFanEditingGpu ? 1 : 0;
            UpdateFanStatusBadge();
        }

        private void MsiFanSeparateToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_msiFanInitializing) return;
            _msiFanSeparate = MsiFanSeparateToggle?.IsOn ?? false;
            if (!_msiFanSeparate)
            {
                // Back to one curve: both fans follow the CPU curve. Edit CPU.
                Array.Copy(_msiFanDutiesCpu, _msiFanDutiesGpu, MsiFanPoints);
                _msiFanEditingGpu = false;
            }
            _msiFanInitializing = true;
            try { SyncFanModeUi(); } finally { _msiFanInitializing = false; }
            RenderMsiFanCurve();
            if (MsiFanEnableToggle?.IsOn == true) MarkFanDirty(); // apply the mode change on next Apply
        }

        private void MsiFanSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_msiFanInitializing) return;
            _msiFanEditingGpu = (MsiFanSelectComboBox?.SelectedIndex ?? 0) == 1;
            RenderMsiFanCurve(); // show the selected fan's own curve (no dirty — just switching view)
            UpdateFanStatusBadge();
        }

        /// <summary>True when the per-fan (Fan 1 / Fan 2) selector is shown and can take focus — i.e. Separate
        /// is on. Used so the D-Pad chain routes through it only when it actually exists on screen.</summary>
        private bool IsFanSelectorFocusable()
            => _msiFanSeparate && MsiFanSelectComboBox != null
               && MsiFanSelectPanel?.Visibility == Visibility.Visible;

        /// <summary>The control directly above the curve graph in the D-Pad chain: the per-fan selector when
        /// Separate is on, otherwise the preset combo.</summary>
        private Windows.UI.Xaml.Controls.Control FanRowAboveGraph()
            => IsFanSelectorFocusable()
               ? (Windows.UI.Xaml.Controls.Control)MsiFanSelectComboBox
               : MsiFanPresetComboBox;

        private void MsiFanSelectComboBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (MsiFanSelectComboBox?.IsDropDownOpen == true) return; // let the open dropdown handle keys

            if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp)
            {
                MsiFanPresetComboBox?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown)
            {
                if (MsiFanCurveFocus != null)
                    MsiFanCurveFocus.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                else
                    (PerGameProfileToggle ?? (Windows.UI.Xaml.Controls.Control)FPSLimitToggle)?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                e.Handled = true;
            }
        }

        /// <summary>Fan settings status shown below the readout: "Fan 1 &amp; 2 · Sync" when both fans share one
        /// curve, else which fan the graph is currently editing.</summary>
        private void UpdateFanStatusBadge()
        {
            if (MsiFanStatusText == null) return;
            MsiFanStatusText.Text = !_msiFanSeparate
                ? "Fan 1 & 2 · Sync"
                : (_msiFanEditingGpu ? "Editing Fan 2" : "Editing Fan 1");
        }

        private async void SendMsiFanCurveToHelper()
        {
            try
            {
                if (!App.IsConnected || !IsMsiClawDevice()) return;
                string csv = CurveToCsv(); // "sync|cpu6|gpu6"
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "MsiFanCurve6", csv } });
                Logger.Info($"SendMsiFanCurveToHelper: '{csv}'");
                AutoVerifyAfterApply();
            }
            catch (Exception ex)
            {
                Logger.Error($"SendMsiFanCurveToHelper: {ex.Message}");
            }
        }

        // ── Per-game fan ────────────────────────────────────────────────────────────────────────────
        //
        // The widget no longer drives this. SyncPerGameFan, GetGlobalFanCsv, SendMsiFanCurve6Transient and
        // SendMsiFanRestoreGlobal are GONE (2026-08-02).
        //
        // They were the second, weaker half of the fan pipeline: the widget decided from
        // LoadProfileSettings when a game's curve should go live and when the global one should come back.
        // LoadProfileSettings re-runs on every periodic property BatchGet (~10 s) and on every Game-Bar
        // detection flap, so that decision was re-made about twenty times an hour, and each one re-entered
        // software fan mode — the documented trigger for the Intel IPF/TFN1 fan latch. It was disconnected
        // for that reason on 2026-07-24 (0f23225).
        //
        // Per-game fan is now resolved and applied HELPER-side (Program.MSIClaw.ApplyFanCurveFromProfile),
        // from the same profile events as TDP and the FPS cap, with a dedup against what the EC is already
        // commanded with and a 25 s cooldown against profile flicker. What is left here is an editor: it
        // shows the curve belonging to the active scope, and its Apply button sends it once.

        /// <summary>Whether the global fan master switch is on. Used only to decide what this card shows;
        /// the helper makes the same call for itself before touching the EC.</summary>
        private bool IsFanGloballyEnabled()
        {
            var settings = ApplicationData.Current.LocalSettings;
            return settings.Values.TryGetValue(MsiFanEnabledKey, out var e) && e is bool b && b;
        }

        /// <summary>Resolve a (preset, customCsv) pair to the effective, snapped "sync|cpu6|gpu6" duties.
        /// preset 3 = the custom curve; 0/1/2/4 = the built-in preset (both fans synced). Null if unresolvable.</summary>
        private string ResolveEffectiveFanCsv(int preset, string customCsv)
        {
            int[] cpu, gpu;
            if (preset == 3)
            {
                if (!TryParseCurveCsv(customCsv, out cpu, out gpu, out _)) return null;
            }
            else
            {
                int[] d = preset == 1 ? MsiDutyQuietIdle
                        : preset == 2 ? MsiDutyCooling
                        : preset == 4 ? MsiDutyEcSport
                        : ModelDefaultDuty();
                if (d == null || d.Length < MsiFanPoints) return null;
                cpu = (int[])d.Clone();
                gpu = (int[])d.Clone();
            }
            SnapMsiCurveInPlace(cpu);
            SnapMsiCurveInPlace(gpu);
            bool sync = cpu.SequenceEqual(gpu);
            return (sync ? "1" : "0") + "|" + string.Join(",", cpu) + "|" + string.Join(",", gpu);
        }

        private bool TryParseCurveCsv(string csv, out int[] cpu, out int[] gpu, out bool separate)
        {
            cpu = null; gpu = null; separate = false;
            if (string.IsNullOrWhiteSpace(csv)) return false;
            var parts = csv.Split('|');
            if (parts.Length != 3) return false;
            separate = parts[0].Trim() == "0";
            cpu = ParseIntsN(parts[1], MsiFanPoints, 0, 150);
            gpu = ParseIntsN(parts[2], MsiFanPoints, 0, 150);
            return cpu != null && gpu != null;
        }

        /// <summary>
        /// A stored curve as a name for the profile cards and the game-start notification: the preset's
        /// name when the duties match one exactly, otherwise "Custom".
        ///
        /// Only the CURVE is stored, never a preset index — a preset index cannot express "no fan setting"
        /// (index 0 and "never captured" look the same), and that ambiguity is what made the first per-game
        /// fan write the factory curve over the user's global one. Naming happens here at display time
        /// instead, which also stays correct if a preset's duties are ever retuned.
        ///
        /// MSI Default is per MODEL, so it is compared against the axis the helper pushed for THIS device
        /// (ModelDefaultDuty), not against a constant.
        /// </summary>
        internal string DescribeFanCurve(string csv)
        {
            if (!TryParseCurveCsv(csv, out int[] cpu, out int[] gpu, out _)) return null;

            // Separate fan curves can never be a preset — every preset drives both fans with one curve.
            if (!cpu.SequenceEqual(gpu)) return "Custom";

            if (cpu.SequenceEqual(ModelDefaultDuty())) return "MSI Default";
            if (cpu.SequenceEqual(MsiDutyQuietIdle)) return "Quiet Idle";
            if (cpu.SequenceEqual(MsiDutyCooling)) return "Cooling";
            return "Custom";
        }

        /// <summary>
        /// Names what the Apply button will write to: the running game when the Fan save-flag is on and a
        /// game with an active per-game profile is running, otherwise "Global".
        ///
        /// The fan is the one setting behind an explicit Apply, so the target has to be visible at the
        /// moment of pressing it — every other setting saves the instant it changes, where the scope is
        /// implied by whatever was on screen.
        ///
        /// The GAME half comes from the helper (<see cref="_helperFanScope"/>), because the helper is what
        /// decides the save target too (IsPerGameFanTargetActive) — deriving it a second time here just
        /// produced a label that disagreed with the graph whenever detection flapped. The save-flag half
        /// stays local: it is a widget checkbox, and the helper reads it from the same flag set.
        /// </summary>
        private string CurrentFanApplyScope()
        {
            if (SaveFan && !string.IsNullOrEmpty(_helperFanSaveTarget))
                return ShortenGameNameForButton(_helperFanSaveTarget);
            return "Global";
        }

        // "Hollow Knight Silksong 2020" — the reference the caption is allowed to grow to. Game names
        // come from window titles and can be arbitrarily long ("… - Enhanced Edition", launcher
        // suffixes, episode names); the button sits in a fixed row next to the Fan 1 & 2 badge, so an
        // untruncated one pushes the layout instead of wrapping.
        private const int FanApplyScopeMaxChars = 27;

        /// <summary>Trims a game name to the button's width budget, with an ellipsis so it reads as cut
        /// off rather than as a different game.</summary>
        private static string ShortenGameNameForButton(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= FanApplyScopeMaxChars) return name;
            return name.Substring(0, FanApplyScopeMaxChars - 1).TrimEnd() + "…";
        }

        /// <summary>Keeps the Apply button's caption in step with the scope it would write to.</summary>
        private void UpdateFanApplyScopeLabel()
        {
            if (MsiFanApplyScopeText == null) return;
            try { MsiFanApplyScopeText.Text = CurrentFanApplyScope(); }
            catch (Exception ex) { Logger.Debug($"UpdateFanApplyScopeLabel: {ex.Message}"); }
            UpdateFanScopeBadge();
        }

        /// <summary>
        /// Green "Game Profile" / orange "Global Profile" next to the card title, with the power-state
        /// suffix the performance badge uses when that profile has a split.
        ///
        /// Names the profile being EDITED, like the performance tab's badge — the same value the Apply
        /// button targets, so the two halves of one card cannot contradict each other. It deliberately
        /// does NOT name what the EC currently runs: those differ for up to 25 s around a game start, and
        /// for a profile with no curve yet they differ permanently. The running state is not lost, it is
        /// said where it belongs — the countdown line and the cooldown overlay both spell out which curve
        /// is being written and how long it still takes.
        ///
        /// Still the helper's word rather than the widget's own game state: game detection flaps, and
        /// re-deriving this here is what made the label and the graph disagree in the first place.
        /// </summary>
        private void UpdateFanScopeBadge()
        {
            if (MsiFanScopeBadge == null || MsiFanScopeBadgeText == null) return;
            try
            {
                bool perGame = SaveFan && !string.IsNullOrEmpty(_helperFanSaveTarget);
                string label;
                if (perGame)
                {
                    label = GetPerGamePowerSourceProfileEnabled(_helperFanSaveTarget)
                        ? $"Game Profile ({CurrentPowerStateLabel()})"
                        : "Game Profile";
                }
                else
                {
                    label = GetGlobalPowerSourceProfileEnabled()
                        ? $"Global Profile ({CurrentPowerStateLabel()})"
                        : "Global Profile";
                }
                ApplyProfileStatusBadge(MsiFanScopeBadge, MsiFanScopeBadgeText, perGame, label);
            }
            catch (Exception ex) { Logger.Debug($"UpdateFanScopeBadge: {ex.Message}"); }
        }

        /// <summary>
        /// Shows the settle countdown for a scheduled curve change ("&lt;secondsLeft&gt;|&lt;isPerGame&gt;")
        /// and LOCKS the editor while it runs.
        ///
        /// Two lessons in one method. The countdown exists because the 25 s wait otherwise reads as "the
        /// sliders did nothing" — the delay is deliberate, so it gets said out loud. The lock exists
        /// because saying it in one small orange line was not enough: people carried on dragging bars
        /// during the window and ended up holding unsaved edits on top of a change that had not landed
        /// yet, with no good way to tell which of the two the fan was about to run. Nothing can be edited
        /// while the window is open, so nothing can be left dangling when it closes.
        /// </summary>
        private void OnHelperFanPending(string payload)
        {
            string[] parts = (payload ?? string.Empty).Split('|');
            int seconds = 0;
            if (parts.Length > 0) int.TryParse(parts[0], out seconds);
            bool perGame = parts.Length > 1 && parts[1] == "1";

            bool locked = seconds > 0;

            // The separate countdown line above the graph is gone — the overlay below says the same
            // thing across the whole graph and also explains the lock, so the line was a second copy of
            // one message.

            if (MsiFanCooldownOverlay != null)
            {
                MsiFanCooldownOverlay.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
                if (locked)
                {
                    if (MsiFanCooldownHeadline != null)
                        MsiFanCooldownHeadline.Text = perGame ? "Game fan curve is being applied"
                                                              : "Global fan curve is being applied";
                    if (MsiFanCooldownCountdown != null)
                        MsiFanCooldownCountdown.Text = $"{seconds} s";
                }
            }

            SetFanEditingLocked(locked);
        }

        // What the cooldown lock switched off, so releasing it cannot re-enable something that was
        // already disabled for its own reasons (Separate/Select only exist in some fan modes).
        private bool _fanEditingLocked;

        /// <summary>
        /// Blocks or releases every control that can change the curve. The graph is covered by the
        /// overlay, which stops the pointer on its own; the focus wrapper is switched off as well so a
        /// controller cannot walk into it and edit blind behind the message.
        ///
        /// Apply is left to <see cref="UpdateApplyButtonState"/> on release rather than being forced
        /// back on: with editing blocked there can be no new dirty state, so whatever it was before the
        /// lock is still the right answer afterwards.
        /// </summary>
        private void SetFanEditingLocked(bool locked)
        {
            if (locked == _fanEditingLocked) return;
            _fanEditingLocked = locked;

            try
            {
                if (MsiFanCurveFocus != null)
                {
                    MsiFanCurveFocus.IsEnabled = !locked;
                    MsiFanCurveFocus.IsTabStop = !locked;
                }
                if (MsiFanPresetComboBox != null) MsiFanPresetComboBox.IsEnabled = !locked;
                if (MsiFanSeparateToggle != null) MsiFanSeparateToggle.IsEnabled = !locked;
                if (MsiFanSelectComboBox != null) MsiFanSelectComboBox.IsEnabled = !locked;
                if (MsiFanEnableToggle != null) MsiFanEnableToggle.IsEnabled = !locked;

                if (locked)
                {
                    if (MsiFanApplyButton != null)
                    {
                        MsiFanApplyButton.IsEnabled = false;
                        MsiFanApplyButton.IsTabStop = false;
                    }
                }
                else
                {
                    UpdateApplyButtonState();
                }
            }
            catch (Exception ex) { Logger.Debug($"SetFanEditingLocked({locked}): {ex.Message}"); }
        }

        // The GLOBAL fan setting per power state, as the helper resolved it. Curve format is the widget's
        // own "<sync>|<cpu6>|<gpu6>", so DescribeFanCurve and FormatFanCurveShort read it unchanged.
        // Null = nothing received yet, which keeps the rows hidden rather than showing a guess.
        private string _globalFanCurvePlugged;
        private string _globalFanCurveBattery;

        /// <summary>
        /// Adopts the helper's global fan report, "&lt;plugMode&gt;|&lt;plugCpu&gt;|&lt;plugGpu&gt;|
        /// &lt;battMode&gt;|&lt;battCpu&gt;|&lt;battGpu&gt;", and repaints the profile cards.
        ///
        /// Both halves arrive already resolved — a plugged column with no override of its own carries the
        /// battery value, decided helper-side where the fan is actually applied. Nothing is inherited here.
        /// </summary>
        private void OnHelperGlobalFan(string payload)
        {
            string[] p = (payload ?? string.Empty).Split('|');
            if (p.Length < 6) return;

            // Mode -1 is firmware/Auto: no duties to name, so the rows stay away entirely.
            string Curve(int modeIdx, int cpuIdx, int gpuIdx)
            {
                if (!int.TryParse(p[modeIdx], out int mode) || mode < 0) return null;
                if (string.IsNullOrWhiteSpace(p[cpuIdx])) return null;
                return $"{(p[cpuIdx] == p[gpuIdx] ? "1" : "0")}|{p[cpuIdx]}|{p[gpuIdx]}";
            }

            _globalFanCurvePlugged = Curve(0, 1, 2);
            _globalFanCurveBattery = Curve(3, 4, 5);
            UpdateProfileDisplay();
        }

        // What the helper last reported as the running fan scope. Empty/null = global. This is the ONLY
        // thing the fan tab uses to decide scope; nothing here re-derives it from currentGameName.
        private string _helperFanScope;
        // Where the helper says an Apply would land. Empty/null = global. A SEPARATE value from the scope
        // above on purpose: a game whose profile has no curve yet runs the global one, so the scope is
        // "global" while the save target is that game. Reading the scope for both is what made the fan
        // card say "Global" on every first per-game save.
        private string _helperFanSaveTarget;
        // The curve the helper reported ("<sync>|<cpu6>|<gpu6>"), kept so it can be put back after any
        // other path reloads the editor from LocalSettings.
        private string _helperFanCurve;
        // The payload currently on screen, so an unchanged republish is a no-op.
        private string _helperFanCurveShown;

        /// <summary>
        /// Adopts the helper's report of what the EC is running: "&lt;scope&gt;|&lt;cpu6&gt;|&lt;gpu6&gt;",
        /// empty scope meaning global.
        ///
        /// DISPLAY ONLY. It goes through LoadFanEditorState, which is fully guarded, and it never sends or
        /// persists anything. That distinction is the whole point — the old per-game fan mixed "show the
        /// curve" and "apply the curve" into one method driven by a path that re-ran every few seconds.
        /// Here, showing is free and applying takes a button press.
        ///
        /// WHY THIS REPLACED THE WIDGET-SIDE DERIVATION. The editor used to compute the scope from
        /// currentGameName plus the per-game toggle and then look the curve up in the profile snapshot.
        /// Both inputs flap with game detection: measured 2026-08-03 the editor showed Silksong's curve at
        /// 10:56:31, dropped back to the global one at 10:56:35 when detection briefly lost the game, and
        /// stayed wrong for the next 96 seconds while the EC ran the game's curve — with the Apply button
        /// still captioned "Silksong", because the label was sampled at a different instant than the graph.
        /// Now both read one value from the process that actually wrote the curve.
        /// </summary>
        private void OnHelperFanScope(string payload)
        {
            try
            {
                if (!IsMsiFanControlSupported() || MsiFanPresetComboBox == null || string.IsNullOrEmpty(payload)) return;

                int sep = payload.IndexOf('|');
                if (sep < 0) return;
                string scope = payload.Substring(0, sep);
                string curve = payload.Substring(sep + 1);   // "<sync>|<cpu6>|<gpu6>"

                _helperFanScope = scope;
                _helperFanCurve = curve;
                UpdateFanApplyScopeLabel();
                ApplyHelperFanCurveToEditor(payload);
            }
            catch (Exception ex) { Logger.Info($"OnHelperFanScope: {ex.Message}"); }
        }

        /// <summary>
        /// Adopts the helper's report of where an Apply would land: the game name, or empty for global.
        ///
        /// DISPLAY ONLY, like the scope above — it changes captions, never the curve. It comes from the
        /// helper because the helper is what routes the save (IsPerGameFanTargetActive); a caption derived
        /// separately here would be free to disagree with what the button press then does.
        /// </summary>
        private void OnHelperFanSaveTarget(string payload)
        {
            try
            {
                string target = payload ?? string.Empty;
                if (target == (_helperFanSaveTarget ?? string.Empty)) return;
                _helperFanSaveTarget = target;
                UpdateFanApplyScopeLabel();
            }
            catch (Exception ex) { Logger.Info($"OnHelperFanSaveTarget: {ex.Message}"); }
        }

        /// <summary>
        /// Puts the helper's reported curve back on the sliders.
        ///
        /// THIS EXISTS BECAUSE THREE OTHER PATHS RELOAD THE EDITOR FROM LOCALSETTINGS, i.e. from the
        /// GLOBAL curve, and they run after we have adopted a per-game one:
        ///   • InitializeMsiFanCard — hangs off the deviceDisplayName PropertyChanged handler, which
        ///     fires a few seconds after every connect (the same handler that was also resetting the
        ///     Intel feature card),
        ///   • OnMsiFanState — the helper's own fan-state push, which carries the global preset,
        ///   • the fan enable toggle.
        /// Measured 2026-08-03: the log showed the game's curve adopted at 14:44:14 while the sliders
        /// kept the global one, and the Apply label stayed correct because it reads _helperFanScope,
        /// which none of those paths touch. Label right, graph wrong — exactly the reported symptom.
        ///
        /// Each of those paths now calls this afterwards instead of being taught to know about scopes.
        /// </summary>
        private void ApplyHelperFanCurveToEditor(string payloadForDedup = null)
        {
            try
            {
                if (!IsMsiFanControlSupported() || MsiFanPresetComboBox == null) return;
                if (string.IsNullOrEmpty(_helperFanCurve)) return;

                // Never yank the graph out from under someone mid-edit — unsaved changes are theirs.
                if (_msiFanDirty) return;

                string payload = payloadForDedup ?? ((_helperFanScope ?? string.Empty) + "|" + _helperFanCurve);
                if (_helperFanCurveShown == payload) return;
                _helperFanCurveShown = payload;

                // Show a preset by name when the running curve is one — decided from the DUTIES, so it
                // stays right if a preset is ever retuned.
                int preset = ResolveFanPresetIndex(_helperFanCurve);
                Logger.Info($"[PerGameFan] fan editor follows the helper: " +
                            $"scope='{(string.IsNullOrEmpty(_helperFanScope) ? "global" : _helperFanScope)}' " +
                            $"curve '{_helperFanCurve}' shown as preset {preset}");
                LoadFanEditorState(preset, _helperFanCurve);
            }
            catch (Exception ex) { Logger.Info($"ApplyHelperFanCurveToEditor: {ex.Message}"); }
        }

        /// <summary>
        /// Called by every path that has just reloaded the editor from LocalSettings. Drops the
        /// "already showing this" marker — the display was overwritten, so the next re-apply must not
        /// dedup itself away — and puts the helper's curve back.
        /// </summary>
        private void ReapplyHelperFanCurveAfterReload()
        {
            _helperFanCurveShown = null;
            ApplyHelperFanCurveToEditor();
        }

        /// <summary>
        /// Which entry of the preset picker a curve corresponds to, or 3 (Custom) when it matches none.
        /// Mirrors <see cref="DescribeFanCurve"/> — same comparisons, index instead of a name.
        /// </summary>
        private int ResolveFanPresetIndex(string curveCsv)
        {
            if (!TryParseCurveCsv(curveCsv, out int[] cpu, out int[] gpu, out _)) return 3;
            if (!cpu.SequenceEqual(gpu)) return 3;          // separate fans is never a preset
            if (cpu.SequenceEqual(ModelDefaultDuty())) return 0;
            if (cpu.SequenceEqual(MsiDutyQuietIdle)) return 1;
            if (cpu.SequenceEqual(MsiDutyCooling)) return 2;
            return 3;
        }

        /// <summary>Load a curve into the EDITOR only (arrays + preset combo + render), fully guarded so no
        /// send/persist fires. Used to reflect the per-game (or restored global) curve in the fan tab.</summary>
        private void LoadFanEditorState(int preset, string customCsv)
        {
            _msiFanInitializing = true;
            try
            {
                if (preset == 3 && TryParseCurveCsv(customCsv, out int[] cpu, out int[] gpu, out bool sep))
                {
                    _msiFanSeparate = sep;
                    _msiFanEditingGpu = false;
                    Array.Copy(cpu, _msiFanDutiesCpu, MsiFanPoints);
                    Array.Copy(gpu, _msiFanDutiesGpu, MsiFanPoints);
                    SnapMsiCurveInPlace(_msiFanDutiesCpu);
                    SnapMsiCurveInPlace(_msiFanDutiesGpu);
                    SetPresetComboProgrammatically(3);
                }
                else
                {
                    if (preset < 0 || preset > 4) preset = 0;
                    SetPresetComboProgrammatically(preset);
                    LoadCurveForPreset(preset);
                }
                SyncFanModeUi();
            }
            catch (Exception ex) { Logger.Info($"LoadFanEditorState: {ex.Message}"); }
            finally { _msiFanInitializing = false; }
            RenderMsiFanCurve();
        }

        // The preset index we set ourselves and are still expecting the queued SelectionChanged for.
        // -1 = nothing pending, i.e. the next event is a real user selection.
        private int _pendingProgrammaticPreset = -1;

        /// <summary>
        /// Moves the preset picker without letting its handler treat that as a user choice.
        ///
        /// THE BUG THIS FIXES. Assigning SelectedIndex only QUEUES SelectionChanged — UWP delivers it
        /// after the assigning method has returned, by which point the _msiFanInitializing guard is
        /// already back down. The handler then ran for real: LoadCurveForPreset(3) reloads the GLOBAL
        /// custom curve from LocalSettings, overwriting the per-game duties that had just been copied
        /// in, and SendMsiFanStateToHelper pushed that global curve back as if the user had applied it.
        /// Measured 2026-08-03: the log showed the game's curve being adopted correctly at 14:21:49
        /// while the sliders kept showing the global one.
        ///
        /// Remembering the index we set is deterministic and, unlike holding the guard across a
        /// dispatcher round-trip, cannot get stuck: worst case the flag is consumed by the next event,
        /// which is exactly what it is for.
        /// </summary>
        private void SetPresetComboProgrammatically(int index)
        {
            if (MsiFanPresetComboBox == null) return;
            if (MsiFanPresetComboBox.SelectedIndex == index) return;   // no event, nothing to suppress
            _pendingProgrammaticPreset = index;
            MsiFanPresetComboBox.SelectedIndex = index;
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
