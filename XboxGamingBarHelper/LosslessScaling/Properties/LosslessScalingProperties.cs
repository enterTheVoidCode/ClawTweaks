using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.LosslessScaling.Properties
{
    internal class LosslessScalingInstalledProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingInstalledProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingInstalled, inManager)
        {
        }
    }

    internal class LosslessScalingRunningProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingRunningProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingRunning, inManager)
        {
        }
    }

    internal class LosslessScalingEnabledProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingEnabledProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingEnabled, inManager)
        {
        }
    }

    internal class LosslessScalingScalingTypeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingScalingTypeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingScalingType, inManager)
        {
        }
    }

    internal class LosslessScalingSharpnessProperty : HelperProperty<int, LosslessScalingManager>
    {
        public LosslessScalingSharpnessProperty(int inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingSharpness, inManager)
        {
        }
    }

    internal class LosslessScalingFSROptimizeProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingFSROptimizeProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingFSROptimize, inManager)
        {
        }
    }

    internal class LosslessScalingAnime4KSizeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingAnime4KSizeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingAnime4KSize, inManager)
        {
        }
    }

    internal class LosslessScalingAnime4KVRSProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingAnime4KVRSProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingAnime4KVRS, inManager)
        {
        }
    }

    internal class LosslessScalingScaleModeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingScaleModeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingScaleMode, inManager)
        {
        }
    }

    internal class LosslessScalingScaleFactorProperty : HelperProperty<int, LosslessScalingManager>
    {
        public LosslessScalingScaleFactorProperty(int inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingScaleFactor, inManager)
        {
        }
    }

    internal class LosslessScalingAspectRatioProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingAspectRatioProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingAspectRatio, inManager)
        {
        }
    }

    internal class LosslessScalingFrameGenTypeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingFrameGenTypeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingFrameGenType, inManager)
        {
        }
    }

    internal class LosslessScalingLSFG3ModeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingLSFG3ModeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingLSFG3Mode, inManager)
        {
        }
    }

    internal class LosslessScalingLSFG3MultiplierProperty : HelperProperty<int, LosslessScalingManager>
    {
        public LosslessScalingLSFG3MultiplierProperty(int inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingLSFG3Multiplier, inManager)
        {
        }
    }

    internal class LosslessScalingLSFG3TargetProperty : HelperProperty<int, LosslessScalingManager>
    {
        public LosslessScalingLSFG3TargetProperty(int inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingLSFG3Target, inManager)
        {
        }
    }

    internal class LosslessScalingLSFG2ModeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingLSFG2ModeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingLSFG2Mode, inManager)
        {
        }
    }

    internal class LosslessScalingFlowScaleProperty : HelperProperty<int, LosslessScalingManager>
    {
        public LosslessScalingFlowScaleProperty(int inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingFlowScale, inManager)
        {
        }
    }

    internal class LosslessScalingSizeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingSizeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingSize, inManager)
        {
        }
    }

    internal class LosslessScalingCurrentProfileProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingCurrentProfileProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingCurrentProfile, inManager)
        {
        }
    }

    internal class LosslessScalingAutoScaleProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingAutoScaleProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingAutoScale, inManager)
        {
        }
    }

    internal class LosslessScalingAutoScaleDelayProperty : HelperProperty<int, LosslessScalingManager>
    {
        public LosslessScalingAutoScaleDelayProperty(int inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingAutoScaleDelay, inManager)
        {
        }
    }

    internal class LosslessScalingSaveAndRestartProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingSaveAndRestartProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingSaveAndRestart, inManager)
        {
        }
    }

    internal class LosslessScalingCreateProfileProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingCreateProfileProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingCreateProfile, inManager)
        {
        }
    }

    internal class LosslessScalingBringToForegroundProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingBringToForegroundProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingBringToForeground, inManager)
        {
        }
    }

    internal class LosslessScalingLaunchProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingLaunchProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingLaunch, inManager)
        {
        }
    }

    // --- Additional Settings.xml fields (added 2026-05-01) ---
    // Each property mirrors a single XML field on the active LS profile.
    // The widget owns the source-of-truth for user input; helper persists it
    // back to Settings.xml on Apply-and-Restart.

    internal class LosslessScalingSyncModeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingSyncModeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingSyncMode, inManager) { }
    }

    internal class LosslessScalingCaptureApiProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingCaptureApiProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingCaptureApi, inManager) { }
    }

    internal class LosslessScalingDrawFpsProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingDrawFpsProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingDrawFps, inManager) { }
    }

    internal class LosslessScalingHdrSupportProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingHdrSupportProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingHdrSupport, inManager) { }
    }

    internal class LosslessScalingGsyncSupportProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingGsyncSupportProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingGsyncSupport, inManager) { }
    }

    internal class LosslessScalingResizeBeforeScalingProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingResizeBeforeScalingProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingResizeBeforeScaling, inManager) { }
    }

    internal class LosslessScalingLS1TypeProperty : HelperProperty<string, LosslessScalingManager>
    {
        public LosslessScalingLS1TypeProperty(string inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingLS1Type, inManager) { }
    }

    internal class LosslessScalingMaxFrameLatencyProperty : HelperProperty<int, LosslessScalingManager>
    {
        public LosslessScalingMaxFrameLatencyProperty(int inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingMaxFrameLatency, inManager) { }
    }

    internal class LosslessScalingResetProfileProperty : HelperProperty<bool, LosslessScalingManager>
    {
        public LosslessScalingResetProfileProperty(bool inValue, LosslessScalingManager inManager)
            : base(inValue, null, Function.LosslessScalingResetProfile, inManager) { }
    }
}
