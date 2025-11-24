namespace Shared.Enums
{
    public enum Function
    {
        None = 0,
        OSD,
        TDP,
        CurrentTDP,
        RunningGame,
        PerGameProfile,
        CPUBoost,
        CPUEPP,
        LimitCPUClock,
        CPUClockMax,
        LimitGPUClock,
        GPUClockMin,
        GPUClockMax,
        RefreshRates,
        RefreshRate,
        TrackedGame,
        RTSSInstalled,
        AMDRadeonSuperResolutionSupported,
        AMDRadeonSuperResolutionEnabled,
        AMDRadeonSuperResolutionSharpness,
        AMDFluidMotionFrameSupported,
        AMDFluidMotionFrameEnabled,
        AMDRadeonAntiLagSupported,
        AMDRadeonAntiLagEnabled,
        AMDRadeonBoostSupported,
        AMDRadeonBoostEnabled,
        AMDRadeonBoostResolution,
        AMDRadeonChillSupported,
        AMDRadeonChillEnabled,
        AMDRadeonChillMinFPS,
        AMDRadeonChillMaxFPS,
        Foreground,

        LosslessScalingInstalled,
        LosslessScalingRunning,
        LosslessScalingEnabled,
        LosslessScalingCurrentProfile,   // Name of active profile for current game
        LosslessScalingScalingType,      // Off, LS1, FSR, NIS, SGSR, BCAS, Anime4K, xBR, SharpBilinear, Integer, NearestNeighbor
        LosslessScalingSharpness,        // 0-100 (for FSR, NIS, SGSR, BCAS)
        LosslessScalingFSROptimize,      // bool - FSR optimize toggle
        LosslessScalingAnime4KSize,      // Small, Medium, Large, VeryLarge, UltraLarge
        LosslessScalingAnime4KVRS,       // bool - VRS toggle for Anime4K
        LosslessScalingScaleMode,        // Auto, Custom
        LosslessScalingScaleFactor,      // 1-5 (for Custom mode)
        LosslessScalingAspectRatio,      // AspectRatio, Fullscreen (for Auto mode)
        LosslessScalingFrameGenType,     // Off, LSFG1, LSFG2, LSFG3
        LosslessScalingLSFG3Mode,        // FIXED, ADAPTIVE
        LosslessScalingLSFG3Multiplier,  // 2, 3, 4
        LosslessScalingLSFG3Target,      // Target FPS (int)
        LosslessScalingLSFG2Mode,        // X2, X3, X4
        LosslessScalingFlowScale,        // 25-100
        LosslessScalingSize,             // PERFORMANCE, BALANCED
        LosslessScalingAutoScale,        // bool - auto-detect and scale
        LosslessScalingAutoScaleDelay,   // int - delay in ms before auto-scaling
        LosslessScalingSaveAndRestart,   // Action: save XML and restart LS
        LosslessScalingCreateProfile,    // Action: create profile for current game
        LosslessScalingBringToForeground, // Action: bring LS window to foreground
        LosslessScalingLaunch,           // Action: launch LS minimized (via helper)

        Settings_AutoStartRTSS,
        Settings_OnScreenDisplayProvider,
    }
}
