using NLog;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Intel
{
    /// <summary>
    /// Manages the Intel GPU IGCL integration — endurance gaming (FPS tier cap).
    /// The DLL is optional: if IGCL_Wrapper.dll is absent all operations are silent no-ops.
    /// Ported from IntelGameBar.
    /// </summary>
    internal class IntelGpuManager : Manager
    {
        private static new readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IntelFpsTierProperty intelFpsTier;
        public  IntelFpsTierProperty          IntelFpsTier => intelFpsTier;

        private readonly FpsCapModeProperty fpsCapMode;
        public  FpsCapModeProperty          FpsCapMode => fpsCapMode;

        private readonly IntelAdaptiveSharpnessProperty intelAdaptiveSharpness;
        public  IntelAdaptiveSharpnessProperty          IntelAdaptiveSharpness => intelAdaptiveSharpness;

        private readonly IntelColorSaturationProperty intelColorSaturation;
        public  IntelColorSaturationProperty          IntelColorSaturation => intelColorSaturation;

        private readonly IntelColorHueProperty intelColorHue;
        public  IntelColorHueProperty          IntelColorHue => intelColorHue;

        private readonly IntelDisplayContrastProperty intelDisplayContrast;
        public  IntelDisplayContrastProperty          IntelDisplayContrast => intelDisplayContrast;

        private readonly IntelDisplayBrightnessProperty intelDisplayBrightness;
        public  IntelDisplayBrightnessProperty          IntelDisplayBrightness => intelDisplayBrightness;

        private readonly IntelDisplayGammaProperty intelDisplayGamma;
        public  IntelDisplayGammaProperty          IntelDisplayGamma => intelDisplayGamma;

        private readonly IntelLowLatencyProperty intelLowLatency;
        public  IntelLowLatencyProperty          IntelLowLatency => intelLowLatency;

        private readonly IntelFrameSyncProperty intelFrameSync;
        public  IntelFrameSyncProperty          IntelFrameSync => intelFrameSync;

        /// <summary>Index of the selected Intel adapter (populated after Initialize()).</summary>
        private int _deviceIdx = -1;

        /// <summary>Display-output index for sharpness/saturation (internal eDP panel = 0).</summary>
        private const uint DisplayIdx = 0;

        /// <summary>True when the IGCL DLL is loaded and an Intel GPU was found.</summary>
        public bool IsAvailable => IGCLBackend.IsReady && _deviceIdx >= 0;

        public IntelGpuManager() : base()
        {
            intelFpsTier = new IntelFpsTierProperty(this);
            fpsCapMode   = new FpsCapModeProperty(this);
            intelAdaptiveSharpness = new IntelAdaptiveSharpnessProperty(this);
            intelColorSaturation   = new IntelColorSaturationProperty(this);
            intelColorHue          = new IntelColorHueProperty(this);
            intelDisplayContrast   = new IntelDisplayContrastProperty(this);
            intelDisplayBrightness = new IntelDisplayBrightnessProperty(this);
            intelDisplayGamma      = new IntelDisplayGammaProperty(this);
            intelLowLatency        = new IntelLowLatencyProperty(this);
            intelFrameSync         = new IntelFrameSyncProperty(this);

            Initialize();
        }

        private void Initialize()
        {
            Logger.Info("[IntelGpuManager] Initializing IGCL...");
            if (!IGCLBackend.Initialize())
            {
                Logger.Warn("[IntelGpuManager] IGCL not available (DLL missing or init failed) — Intel FPS cap disabled.");
                return;
            }

            _deviceIdx = IGCLBackend.FindIntelDeviceIndex("Intel");
            if (_deviceIdx < 0)
            {
                Logger.Warn("[IntelGpuManager] No Intel GPU found via IGCL.");
                return;
            }

            Logger.Info($"[IntelGpuManager] Using Intel adapter index {_deviceIdx}.");
        }

        /// <summary>
        /// Apply an Intel FPS tier.
        /// 0 = Off, 1 = Performance (60 fps), 2 = Balanced (40 fps), 3 = Efficiency (30 fps).
        /// </summary>
        public void ApplyTier(int tier)
        {
            if (!IsAvailable)
            {
                Logger.Debug("[IntelGpuManager] ApplyTier skipped — IGCL not available.");
                return;
            }

            IGCLBackend.ctl_3d_endurance_gaming_control_t control;
            IGCLBackend.ctl_3d_endurance_gaming_mode_t    mode;

            switch (tier)
            {
                case 1:
                    control = IGCLBackend.ctl_3d_endurance_gaming_control_t.AUTO;
                    mode    = IGCLBackend.ctl_3d_endurance_gaming_mode_t.PERFORMANCE;
                    break;
                case 2:
                    control = IGCLBackend.ctl_3d_endurance_gaming_control_t.AUTO;
                    mode    = IGCLBackend.ctl_3d_endurance_gaming_mode_t.BALANCED;
                    break;
                case 3:
                    control = IGCLBackend.ctl_3d_endurance_gaming_control_t.AUTO;
                    mode    = IGCLBackend.ctl_3d_endurance_gaming_mode_t.BATTERY;
                    break;
                default: // 0 = off
                    control = IGCLBackend.ctl_3d_endurance_gaming_control_t.OFF;
                    mode    = IGCLBackend.ctl_3d_endurance_gaming_mode_t.MAX;
                    break;
            }

            bool ok = IGCLBackend.SetEnduranceGaming(_deviceIdx, control, mode);
            Logger.Info($"[IntelGpuManager] SetEnduranceGaming tier={tier} control={control} mode={mode} ok={ok}");
        }

        /// <summary>True when the IGCL gaming exports (arbitrary FPS limit / low latency / frame sync) are usable.</summary>
        public bool IsGamingAvailable => IsAvailable && IGCLBackend.IsGamingReady;

        /// <summary>
        /// Back-compat: the old Intel FPS "tier" (1=Perf/60, 2=Balanced/40, 3=Efficiency/30) is
        /// migrated to a real FPS. Real fps values are always ≥ 20 (slider minimum), so 1/2/3 are
        /// unambiguously legacy tiers. Anything else (0 or ≥ 20) passes through unchanged.
        /// </summary>
        public static int MigrateTierToFps(int v)
        {
            switch (v)
            {
                case 1: return 60;
                case 2: return 40;
                case 3: return 30;
                default: return v;
            }
        }

        /// <summary>
        /// Apply an arbitrary Intel FPS cap via IGCL FRAME_LIMIT (any value, AC+DC).
        /// fps &lt;= 0 disables. This is the stepless replacement for the 3-tier Endurance Gaming path.
        /// </summary>
        public void ApplyFrameLimit(int fps)
        {
            if (!IsGamingAvailable)
            {
                Logger.Debug("[IntelGpuManager] ApplyFrameLimit skipped — IGCL gaming features not available.");
                return;
            }
            bool ok = IGCLBackend.SetFramesPerSecondLimit(_deviceIdx, fps);
            Logger.Info($"[IntelGpuManager] SetFramesPerSecondLimit fps={fps} ok={ok}");
        }

        /// <summary>Apply Intel low latency / anti-lag: 0=off, 1=on, 2=on+boost.</summary>
        public void ApplyLowLatency(int mode)
        {
            if (!IsGamingAvailable)
            {
                Logger.Debug("[IntelGpuManager] ApplyLowLatency skipped — IGCL gaming features not available.");
                return;
            }
            bool ok = IGCLBackend.SetLowLatency(_deviceIdx, mode);
            Logger.Info($"[IntelGpuManager] SetLowLatency mode={mode} ok={ok}");
        }

        /// <summary>Apply Intel frame sync / flip mode: 0=App default,1=VSync off,2=VSync on,3=Smooth,4=Speed.</summary>
        public void ApplyFrameSync(int mode)
        {
            if (!IsGamingAvailable)
            {
                Logger.Debug("[IntelGpuManager] ApplyFrameSync skipped — IGCL gaming features not available.");
                return;
            }
            bool ok = IGCLBackend.SetFrameSync(_deviceIdx, mode);
            Logger.Info($"[IntelGpuManager] SetFrameSync mode={mode} ok={ok}");
        }

        /// <summary>Apply adaptive sharpness (0 = off, 1..100 intensity) on the internal panel.</summary>
        public void ApplyAdaptiveSharpness(int intensity)
        {
            if (!IsAvailable || !IGCLBackend.IsDisplayReady) return;
            bool ok = IGCLBackend.SetAdaptiveSharpness(_deviceIdx, DisplayIdx, intensity);
            Logger.Info($"[IntelGpuManager] AdaptiveSharpness intensity={intensity} ok={ok}");
        }

        /// <summary>Apply hue (-180..180) + saturation (0..100, 50 = neutral) together.</summary>
        public void ApplyHueSaturation()
        {
            if (!IsAvailable || !IGCLBackend.IsDisplayReady) return;
            int hue = intelColorHue?.Value ?? 0;
            int sat = intelColorSaturation?.Value ?? 50;
            bool ok = IGCLBackend.SetHueSaturation(_deviceIdx, hue, sat);
            Logger.Info($"[IntelGpuManager] Hue={hue}, Saturation={sat} ok={ok}");
        }

        /// <summary>Apply contrast (0..100,50), gamma (stored ×100 → 0.30..2.80) and brightness (0..100,50).</summary>
        public void ApplyBrightnessContrastGamma()
        {
            if (!IsAvailable || !IGCLBackend.IsDisplayReady) return;
            int contrast   = intelDisplayContrast?.Value ?? 50;
            int brightness = intelDisplayBrightness?.Value ?? 50;
            double gamma   = (intelDisplayGamma?.Value ?? 100) / 100.0;
            bool ok = IGCLBackend.SetBrightnessContrastGamma(_deviceIdx, contrast, gamma, brightness);
            Logger.Info($"[IntelGpuManager] Contrast={contrast}, Gamma={gamma:0.00}, Brightness={brightness} ok={ok}");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IGCLBackend.Terminate();
            }
            base.Dispose(disposing);
        }
    }
}
