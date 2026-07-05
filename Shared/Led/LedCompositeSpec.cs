using System;
using System.Globalization;
using Shared.Enums;

namespace Shared.Led
{
    /// <summary>Which physical LED group a zone spec targets.</summary>
    public enum LedZoneId { Right = 0, Left = 1, Buttons = 2 }

    public struct LedRgb
    {
        public byte R, G, B;
        public LedRgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public override string ToString() => $"{R}-{G}-{B}";
        public static LedRgb Parse(string s)
        {
            var p = s.Split('-');
            return new LedRgb(byte.Parse(p[0], CultureInfo.InvariantCulture),
                              byte.Parse(p[1], CultureInfo.InvariantCulture),
                              byte.Parse(p[2], CultureInfo.InvariantCulture));
        }
    }

    /// <summary>One zone's effect: mode + its per-mode settings.</summary>
    public sealed class LedZoneSpec
    {
        public LedMainMode Mode = LedMainMode.Static;
        public bool Rainbow;                  // static/breathing/wave: rainbow instead of a solid colour
        public bool Clockwise = true;         // wave direction
        public LedRgb Color = new LedRgb(255, 255, 255);   // static/breathing solid colour
        public LedRgb[] WaveColors =          // wave custom colours (4)
            { new LedRgb(255, 0, 0), new LedRgb(255, 255, 0), new LedRgb(0, 255, 0), new LedRgb(0, 0, 255) };

        public LedZoneSpec Clone() => new LedZoneSpec
        {
            Mode = Mode, Rainbow = Rainbow, Clockwise = Clockwise, Color = Color,
            WaveColors = new[] { WaveColors[0], WaveColors[1], WaveColors[2], WaveColors[3] }
        };

        // "mode;rainbow;cw;color;w0,w1,w2,w3"
        public string Serialize()
        {
            var w = WaveColors ?? new LedZoneSpec().WaveColors;
            return string.Join(";",
                ((int)Mode).ToString(CultureInfo.InvariantCulture),
                Rainbow ? "1" : "0",
                Clockwise ? "1" : "0",
                Color.ToString(),
                string.Join(",", w[0], w[1], w[2], w[3]));
        }

        public static LedZoneSpec ParseZone(string s)
        {
            var z = new LedZoneSpec();
            var f = s.Split(';');
            if (f.Length < 5) return z;
            z.Mode = (LedMainMode)int.Parse(f[0], CultureInfo.InvariantCulture);
            z.Rainbow = f[1] != "0";
            z.Clockwise = f[2] != "0";
            z.Color = LedRgb.Parse(f[3]);
            var wc = f[4].Split(',');
            var w = new LedRgb[4];
            for (int i = 0; i < 4; i++) w[i] = LedRgb.Parse(wc[i]);
            z.WaveColors = w;
            return z;
        }
    }

    /// <summary>
    /// The full per-zone LED composite: global speed/brightness + a spec for each of the 3 LED zones
    /// (right stick, left stick, buttons). Sync is a UI concept (the widget copies one config into all
    /// three) — the helper always composites the three explicit zones.
    ///
    /// Wire/file format: "1|sync|speedIdx|brightness|&lt;rightZone&gt;|&lt;leftZone&gt;|&lt;buttonsZone&gt;"
    /// </summary>
    public sealed class LedCompositeSpec
    {
        public bool Sync = true;
        public int SpeedIdx = 1;        // 0 slow, 1 med, 2 fast
        public int Brightness = 100;    // 0..100 (0 = off)
        public LedZoneSpec Right = new LedZoneSpec();
        public LedZoneSpec Left = new LedZoneSpec();
        public LedZoneSpec Buttons = new LedZoneSpec();

        public LedZoneSpec Zone(LedZoneId id)
            => id == LedZoneId.Right ? Right : id == LedZoneId.Left ? Left : Buttons;

        public string Serialize() => string.Join("|",
            "1",
            Sync ? "1" : "0",
            SpeedIdx.ToString(CultureInfo.InvariantCulture),
            Brightness.ToString(CultureInfo.InvariantCulture),
            Right.Serialize(), Left.Serialize(), Buttons.Serialize());

        public static bool TryParse(string s, out LedCompositeSpec spec)
        {
            spec = new LedCompositeSpec();
            try
            {
                if (string.IsNullOrWhiteSpace(s)) return false;
                var p = s.Split('|');
                if (p.Length < 7 || p[0] != "1") return false;
                spec.Sync = p[1] != "0";
                spec.SpeedIdx = Clamp(int.Parse(p[2], CultureInfo.InvariantCulture), 0, 2);
                spec.Brightness = Clamp(int.Parse(p[3], CultureInfo.InvariantCulture), 0, 100);
                spec.Right = LedZoneSpec.ParseZone(p[4]);
                spec.Left = LedZoneSpec.ParseZone(p[5]);
                spec.Buttons = LedZoneSpec.ParseZone(p[6]);
                return true;
            }
            catch { return false; }
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>
        /// True if this equals the factory default (sync on, medium speed, full brightness, all zones
        /// static white). Used to avoid clobbering a good persisted state with an unconfigured default.
        /// </summary>
        public bool IsPristineDefault => Serialize() == new LedCompositeSpec().Serialize();

        /// <summary>True if any zone is set to the Battery (SoC-tint) mode.</summary>
        public bool HasBatteryZone =>
            Right.Mode == LedMainMode.Battery || Left.Mode == LedMainMode.Battery || Buttons.Mode == LedMainMode.Battery;
    }
}
