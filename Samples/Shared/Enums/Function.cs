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
        Resolutions,        // string[] - list of available resolutions
        Resolution,         // string - current resolution (e.g., "1920x1080")
        HDRSupported,       // bool - whether HDR is supported
        HDREnabled,         // bool - HDR on/off
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
        Settings_UseManufacturerWMI,    // bool - use manufacturer WMI for TDP instead of RyzenAdj

        // Legion Go specific functions
        LegionGoDetected,           // bool - whether a Legion Go device is detected
        LegionTouchpadEnabled,      // bool - touchpad on/off
        LegionLightMode,            // int - RGB mode (Off=0, Solid=1, Pulse=2, Dynamic=3, Spiral=4)
        LegionLightColor,           // string - hex color "#RRGGBB"
        LegionLightBrightness,      // int - brightness (0-100)
        LegionPerformanceMode,      // int - TDP mode (Quiet=1, Balanced=2, Performance=3, Custom=255)
        LegionCustomTDPSlow,        // int - Slow TDP (SPL) in watts
        LegionCustomTDPFast,        // int - Fast TDP (SPPL) in watts
        LegionCustomTDPPeak,        // int - Peak TDP (FPPT) in watts
        LegionFanFullSpeed,         // bool - fan full speed mode
        LegionGyroEnabled,          // bool - gyroscope on/off (WIP)

        // AutoTDP functions
        AutoTDPEnabled,             // bool - enable/disable AutoTDP
        AutoTDPTargetFPS,           // int - target FPS (30-144)
        AutoTDPCurrentFPS,          // int - current FPS reading (read-only)

        // OSD Customization
        OSDConfig,                  // string - OSD configuration per level (L1:items;L2:items;L3:items)

        // FPS Limiter (RTSS)
        FPSLimit,                   // int - FPS limit (0 = unlimited)
    }
}
