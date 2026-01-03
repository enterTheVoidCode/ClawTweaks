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
        MaxCPUState,
        MinCPUState,
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
        AMDImageSharpeningSupported,
        AMDImageSharpeningEnabled,
        AMDImageSharpeningSharpness,
        AMDDisplayBrightnessSupported,
        AMDDisplayBrightness,
        AMDDisplayContrastSupported,
        AMDDisplayContrast,
        AMDDisplaySaturationSupported,
        AMDDisplaySaturation,
        AMDDisplayTemperatureSupported,
        AMDDisplayTemperature,
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
        Settings_UseManufacturerWMI,    // DEPRECATED: bool - use manufacturer WMI for TDP instead of RyzenAdj
        Settings_TdpMethod,             // int (TdpMethod enum) - TDP control method (ManufacturerWMI=0, PawnIO=1, WinRing0=2)
        TdpMethod_WinRing0Available,    // bool - whether WinRing0 files exist in C:\GoTweaks
        TdpMethod_PawnIOAvailable,      // bool - whether PawnIO/RyzenSMU is available for TDP control
        TdpMethod_PawnIOInstalled,      // bool - whether PawnIO driver is installed (driver present, may not work for TDP yet)
        TdpMethod_InstallPawnIO,        // string - trigger to install PawnIO (write "install" to trigger)

        // Legion Go specific functions
        LegionGoDetected,           // bool - whether a Legion Go device is detected
        LegionTouchpadEnabled,      // bool - touchpad on/off
        LegionLightMode,            // int - RGB mode (Off=0, Solid=1, Pulse=2, Dynamic=3, Spiral=4)
        LegionLightColor,           // string - hex color "#RRGGBB"
        LegionLightBrightness,      // int - brightness (0-100)
        LegionLightSpeed,           // int - animation speed (0-100)
        LegionPerformanceMode,      // int - TDP mode (Quiet=1, Balanced=2, Performance=3, Custom=255)
        LegionCustomTDPSlow,        // int - Slow TDP (SPL) in watts
        LegionCustomTDPFast,        // int - Fast TDP (SPPL) in watts
        LegionCustomTDPPeak,        // int - Peak TDP (FPPT) in watts
        LegionFanFullSpeed,         // bool - fan full speed mode
        LegionFanCurveData,         // string - fan curve data "v0,v1,v2,...,v9" (10 values 0-100)
        LegionCPUCurrentTemp,       // int - current CPU temperature in Celsius (read-only from helper)
        LegionCPUFanRPM,            // int - current CPU fan speed in RPM (read-only from helper)
        LegionFanCurveVisible,      // bool - widget sets this when fan curve is expanded and visible
        LegionGyroEnabled,          // bool - gyroscope on/off (WIP)
        LegionVibration,            // int - vibration level (0=Off, 1=Weak, 2=Medium, 3=Strong)
        LegionPowerLight,           // bool - power button LED on/off
        LegionChargeLimit,          // bool - battery charge limit (80%) on/off

        // Legion Go Controller Remapping (supports Gamepad, Keyboard, Mouse mapping)
        LegionButtonY1,             // string - JSON ButtonMapping (type, gamepadAction, keyboardKeys[], mouseButton)
        LegionButtonY2,             // string - JSON ButtonMapping
        LegionButtonY3,             // string - JSON ButtonMapping
        LegionButtonM1,             // string - JSON ButtonMapping (new button)
        LegionButtonM2,             // string - JSON ButtonMapping
        LegionButtonM3,             // string - JSON ButtonMapping
        LegionNintendoLayout,       // bool - Nintendo-style face button swap (A↔B, X↔Y)
        LegionVibrationMode,        // int - vibration mode preset (FPS=1, Racing=2, AVG=3, SPG=4, RPG=5)
        LegionControllerProfileEnabled, // bool - per-game controller profile toggle

        // Legion Go Gyro Settings (per-game profile)
        LegionGyroTarget,               // int - 0=Disabled, 1=LeftStick, 2=RightStick, 3=Mouse
        LegionGyroSensitivityX,         // int - 1-100
        LegionGyroSensitivityY,         // int - 1-100
        LegionGyroInvertX,              // bool
        LegionGyroInvertY,              // bool
        LegionGyroMappingType,          // int - 0=Instant, 1=Continuous
        LegionGyroActivationMode,       // int - 0=Hold, 1=Toggle
        LegionGyroActivationButton,     // int - 0-8 (None, LB, LT, RB, RT, Y1, Y2, M2, M3)

        // Legion Go Advanced Gyro Settings (per-game profile)
        LegionGyroDeadzone,             // int - 1-100 (suppresses small motions near center)

        // Legion Go Stick Deadzones (per-game profile)
        LegionLeftStickDeadzone,        // int - 0-50 (percent)
        LegionRightStickDeadzone,       // int - 0-50 (percent)

        // Legion Go Joystick as Mouse (per-game profile)
        LegionJoystickAsMouseMode,      // int - 0=Disabled, 1=Left Stick, 2=Right Stick
        LegionJoystickMouseSens,        // int - Mouse sensitivity (10-100)

        // Legion Go Gamepad Button Remapping (per-game profile)
        LegionGamepadButtonMapping,     // string - JSON mapping of gamepad buttons to actions

        // Legion Go Desktop Controls (preset: RS→Mouse, RT→LClick, LT→RClick, A→Enter, B→Esc)
        LegionDesktopControls,          // bool - desktop controls preset enabled

        // Legion Go Touchpad Vibration (GLOBAL setting)
        LegionTouchpadVibration,        // bool - on/off toggle for touchpad haptics

        // Controller Battery (read-only, from HID input reports)
        ControllerBatteryLeft,          // int - left controller battery (1-100, or -1 if unavailable)
        ControllerBatteryRight,         // int - right controller battery (1-100, or -1 if unavailable)
        ControllerChargingLeft,         // bool - whether left controller is charging
        ControllerChargingRight,        // bool - whether right controller is charging

        // AutoTDP functions
        AutoTDPEnabled,             // bool - enable/disable AutoTDP
        AutoTDPTargetFPS,           // int - target FPS (30-144)
        AutoTDPCurrentFPS,          // int - current FPS reading (read-only)
        AutoTDPMinTDP,              // int - minimum TDP for AutoTDP range (4-85)
        AutoTDPMaxTDP,              // int - maximum TDP for AutoTDP range (4-85)

        // OSD Customization
        OSDConfig,                  // string - OSD configuration per level (L1:items;L2:items;L3:items)

        // FPS Limiter (RTSS)
        FPSLimit,                   // int - FPS limit (0 = unlimited)

        // Device TDP Limits
        TDPLimits,                  // string - "min,max" format (e.g., "4,35")

        // TDP Boost (apply additional power to SPPT/FPPT above base TDP)
        TDPBoostEnabled,            // bool - enable/disable TDP boost (profile-synced)
        TDPBoostSPPT,               // int - additional watts for SPPT (0-10, default 1)
        TDPBoostFPPT,               // int - additional watts for FPPT (0-15, default 3)

        // CPU Core Configuration
        CPUCoreConfig,              // string - "pCores,eCores,isHybrid" format (e.g., "3,5,true") - helper to widget (detection)
        CPUCoreActiveConfig,        // string - "activePCores,activeECores" format (e.g., "2,4") - widget to helper (user selection)
        CoreParkingPercent,         // int - CPMAXCORES percentage (0-100), 100 = all cores active, 50 = half parked
        ForceParkMode,              // bool - Force affinity on ALL processes (aggressive mode)

        // OS Power Mode (Windows 11 power slider)
        OSPowerMode,                // int - 0=Best Power Efficiency, 1=Balanced, 2=Best Performance

        // System Actions
        RefreshDisplaySettings,     // Action: re-query display resolution, refresh rate, HDR status
    }
}
