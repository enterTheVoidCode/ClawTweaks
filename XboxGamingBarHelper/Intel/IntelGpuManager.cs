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

        /// <summary>Apply adaptive sharpness (0 = off, 1..100 intensity) on the internal panel.</summary>
        public void ApplyAdaptiveSharpness(int intensity)
        {
            if (!IsAvailable || !IGCLBackend.IsDisplayReady)
            {
                Logger.Debug("[IntelGpuManager] ApplyAdaptiveSharpness skipped — IGCL display features unavailable.");
                return;
            }
            bool ok = IGCLBackend.SetAdaptiveSharpness(_deviceIdx, DisplayIdx, intensity);
            Logger.Info($"[IntelGpuManager] AdaptiveSharpness intensity={intensity} ok={ok}");
        }

        /// <summary>Apply colour saturation (percent, 100 = neutral) — converted to a multiplier.</summary>
        public void ApplySaturation(int saturationPercent)
        {
            if (!IsAvailable || !IGCLBackend.IsDisplayReady)
            {
                Logger.Debug("[IntelGpuManager] ApplySaturation skipped — IGCL display features unavailable.");
                return;
            }
            double mult = saturationPercent / 100.0;
            bool ok = IGCLBackend.SetSaturation(_deviceIdx, mult);
            Logger.Info($"[IntelGpuManager] Saturation={saturationPercent}% ({mult:0.00}x) ok={ok}");
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
