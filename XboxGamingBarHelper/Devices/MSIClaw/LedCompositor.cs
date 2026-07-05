using System;
using Shared.Enums;
using Shared.Led;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Renders a <see cref="LedCompositeSpec"/> into 4 × 27-byte LED frames — the core of the per-zone
    /// LED system. Each of the 3 zones (right stick zones 0-3, left stick 4-7, buttons 8) is filled with
    /// its own effect per frame; the firmware then plays/interpolates the 4-frame sequence at the global
    /// speed. Global brightness scales every colour (0 = off). Battery zones use the passed SoC band colour.
    ///
    /// Verified on-device 2026-07-03: per-zone independent effects on one shared 4-frame timeline
    /// (see memory led-per-zone-architecture).
    /// </summary>
    internal static class LedCompositor
    {
        private const int Frames = 4;

        // Rainbow ring (4 hues) for static/breathing/wave rainbow on a 4-LED stick.
        private static readonly LedRgb[] Ring =
            { new LedRgb(255, 0, 0), new LedRgb(255, 255, 0), new LedRgb(0, 255, 0), new LedRgb(0, 0, 255) };

        // Color-cycle palette (purple verified on-device with red/green/blue/magenta).
        private static readonly LedRgb[] Palette =
            { new LedRgb(255, 0, 0), new LedRgb(0, 255, 0), new LedRgb(0, 0, 255), new LedRgb(255, 0, 255) };

        // Breathing brightness ramp across the 4 frames (bright→dim→bright loop).
        private static readonly double[] Ramp = { 1.0, 0.55, 0.2, 0.55 };

        /// <summary>Produces 4 frames (each 27 bytes) for the given composite.</summary>
        public static byte[][] Compose(LedCompositeSpec spec, LedRgb? socColor)
        {
            var frames = new byte[Frames][];
            double bri = Math.Max(0, Math.Min(100, spec.Brightness)) / 100.0;

            for (int k = 0; k < Frames; k++)
            {
                byte[] f = new byte[27];
                // right stick = zones 0-3, left stick = zones 4-7, buttons = zone 8
                FillLeds(f, 0, 4, spec.Right, k, socColor, bri);
                FillLeds(f, 4, 4, spec.Left, k, socColor, bri);
                FillLeds(f, 8, 1, spec.Buttons, k, socColor, bri);
                frames[k] = f;
            }
            return frames;
        }

        // Fills a zone's LEDs (startZone..startZone+count) in frame k.
        private static void FillLeds(byte[] frame, int startZone, int count, LedZoneSpec z, int k, LedRgb? socColor, double bri)
        {
            for (int j = 0; j < count; j++)
            {
                LedRgb c = ZoneLed(z, j, k, socColor);
                int b = (startZone + j) * 3;
                frame[b]     = (byte)Math.Round(c.R * bri);
                frame[b + 1] = (byte)Math.Round(c.G * bri);
                frame[b + 2] = (byte)Math.Round(c.B * bri);
            }
        }

        // The colour of LED j within a zone at frame k (before global brightness).
        private static LedRgb ZoneLed(LedZoneSpec z, int j, int k, LedRgb? socColor)
        {
            switch (z.Mode)
            {
                case LedMainMode.Static:
                    return z.Rainbow ? Ring[j % 4] : z.Color;

                case LedMainMode.Breathing:
                    return Scale(z.Rainbow ? Ring[j % 4] : z.Color, Ramp[k]);

                case LedMainMode.Wave:
                {
                    LedRgb[] cols = z.Rainbow ? Ring : (z.WaveColors ?? Ring);
                    int idx = z.Clockwise ? (j + k) : (j - k);
                    return cols[((idx % 4) + 4) % 4];
                }

                case LedMainMode.ColorCycle:
                    return Palette[k % 4];

                case LedMainMode.Battery:
                    return socColor ?? new LedRgb(255, 255, 255);

                default:
                    return z.Color;
            }
        }

        private static LedRgb Scale(LedRgb c, double f)
            => new LedRgb((byte)Math.Round(c.R * f), (byte)Math.Round(c.G * f), (byte)Math.Round(c.B * f));

        // Global speed byte for the 4-frame timeline (all effects use mode 0x04). higher = slower.
        public static byte SpeedByte(int speedIdx)
            => speedIdx <= 0 ? (byte)0x09 : speedIdx == 1 ? (byte)0x06 : (byte)0x03;
    }
}
