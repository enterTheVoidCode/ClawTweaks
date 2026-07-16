using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClawTweaksSetup.Ui
{
    /// <summary>
    /// Plays Assets/branding/loading-spinner.gif as a looping frame sequence — WPF's Image control
    /// only shows a GIF's first frame natively, so this decodes every frame once (cached process-wide)
    /// and steps through them on a DispatcherTimer. Each instance owns its own timer, started/stopped
    /// on Loaded/Unloaded so a badge swapped out for a ✓/✕ doesn't keep a timer ticking in the background.
    /// </summary>
    public static class GifSpinner
    {
        private static readonly Uri SourceUri =
            new Uri("pack://application:,,,/Assets/branding/loading-spinner.gif", UriKind.Absolute);

        private sealed class Frame
        {
            public BitmapSource Image;
            public TimeSpan Delay;
        }

        // GIF Graphic Control Extension disposal methods.
        private const int DisposeRestoreToBackground = 2;
        private const int DisposeRestoreToPrevious = 3;

        // The source GIF is a full 1280x720 export; nothing in the app ever displays this spinner
        // above ~56px. Compositing 20 frames at native resolution blocked the UI thread for a visible
        // moment right at window construction (Badge(Working) fires from RenderDeviceBanner in the
        // CenterMenuWindow constructor) — cap the composited canvas so the one-time decode cost stays
        // small. Still comfortably supersampled for the sizes actually used.
        private const int MaxCanvasDimension = 160;

        private static List<Frame> _frames;

        private static List<Frame> Frames()
        {
            if (_frames != null) return _frames;

            var decoder = new GifBitmapDecoder(SourceUri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            int canvasW = decoder.Frames[0].PixelWidth;
            int canvasH = decoder.Frames[0].PixelHeight;
            try
            {
                if (decoder.Metadata is BitmapMetadata screen)
                {
                    canvasW = ToInt(SafeQuery(screen, "/logscrdesc/Width"), canvasW);
                    canvasH = ToInt(SafeQuery(screen, "/logscrdesc/Height"), canvasH);
                }
            }
            catch { /* not every codec exposes the logical screen descriptor */ }

            double scale = Math.Min(1.0, MaxCanvasDimension / (double)Math.Max(canvasW, canvasH));
            int outW = Math.Max(1, (int)Math.Round(canvasW * scale));
            int outH = Math.Max(1, (int)Math.Round(canvasH * scale));

            // WPF's GIF decoder hands back each frame as only its own changed sub-rectangle (position,
            // size and even DPI can differ frame to frame) — stretching those raw frames straight into
            // a fixed-size Image, as the first version of this did, is exactly what made the spinner
            // visibly wobble. Composite every frame onto a persistent 96-DPI canvas the size of the
            // GIF's logical screen (scaled down per above) instead, so every frame we hand to the Image
            // control is identically sized and correctly positioned, the same way a browser or Photos
            // plays it.
            var result = new List<Frame>();
            BitmapSource canvas = null;

            foreach (var raw in decoder.Frames)
            {
                int left = 0, top = 0, disposal = 0;
                double delayMs = 100;
                if (raw.Metadata is BitmapMetadata meta)
                {
                    left = ToInt(SafeQuery(meta, "/imgdesc/Left"), 0);
                    top = ToInt(SafeQuery(meta, "/imgdesc/Top"), 0);
                    disposal = ToInt(SafeQuery(meta, "/grctlext/Disposal"), 0);
                    int hundredths = ToInt(SafeQuery(meta, "/grctlext/Delay"), 10);
                    delayMs = hundredths <= 1 ? 100 : hundredths * 10; // browsers clamp near-zero delays the same way
                }

                BitmapSource baseBeforeThisFrame = canvas;

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    if (baseBeforeThisFrame != null)
                        dc.DrawImage(baseBeforeThisFrame, new Rect(0, 0, outW, outH));
                    dc.DrawImage(raw, new Rect(left * scale, top * scale, raw.PixelWidth * scale, raw.PixelHeight * scale));
                }
                var composed = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
                composed.Render(visual);
                composed.Freeze();

                result.Add(new Frame { Image = composed, Delay = TimeSpan.FromMilliseconds(delayMs) });

                // Disposal is applied to prepare the base for the NEXT frame, not this one. Restore-to-
                // background and restore-to-previous both reduce to "undo what this frame drew" in a
                // single-changed-rectangle-per-frame model (the norm for simple looping icon GIFs) — so
                // both just revert to the canvas as it was right before this frame was composited.
                canvas = (disposal == DisposeRestoreToBackground || disposal == DisposeRestoreToPrevious)
                    ? baseBeforeThisFrame
                    : composed;
            }

            _frames = result;
            return _frames;
        }

        private static object SafeQuery(BitmapMetadata meta, string query)
        {
            try { return meta.GetQuery(query); }
            catch { return null; }
        }

        private static int ToInt(object value, int fallback)
        {
            if (value == null) return fallback;
            try { return Convert.ToInt32(value); }
            catch { return fallback; }
        }

        public static FrameworkElement Create(double size)
        {
            var frames = Frames();
            var image = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Source = frames.Count > 0 ? frames[0].Image : null,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            if (frames.Count > 1)
            {
                int i = 0;
                var timer = new DispatcherTimer { Interval = frames[0].Delay };
                timer.Tick += (_, __) =>
                {
                    i = (i + 1) % frames.Count;
                    image.Source = frames[i].Image;
                    timer.Interval = frames[i].Delay;
                };
                image.Loaded += (_, __) => timer.Start();
                image.Unloaded += (_, __) => timer.Stop();
            }
            return image;
        }
    }
}
