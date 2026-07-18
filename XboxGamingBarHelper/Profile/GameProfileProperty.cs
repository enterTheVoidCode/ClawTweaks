using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Profile
{
    internal class GameProfileProperty : HelperProperty<GameProfile, ProfileManager>
    {
        public GameProfileProperty(GameProfile inValue, ProfileManager inManager) : base(inValue, null, Function.None, inManager)
        {
        }

        public int TDP
        {
            get { return value.TDP; }
            set
            {
                if (this.value.TDP != value)
                {
                    this.value.TDP = value;
                }
            }
        }

        public bool CPUBoost
        {
            get { return value.CPUBoost; }
            set
            {
                if (this.value.CPUBoost != value)
                {
                    this.value.CPUBoost = value;
                }
            }
        }

        public int CPUEPP
        {
            get { return value.CPUEPP; }
            set
            {
                if (this.value.CPUEPP != value)
                {
                    this.value.CPUEPP = value;
                }
            }
        }

        public int MaxCPUState
        {
            get { return value.MaxCPUState; }
            set
            {
                if (this.value.MaxCPUState != value)
                {
                    this.value.MaxCPUState = value;
                }
            }
        }

        public int MinCPUState
        {
            get { return value.MinCPUState; }
            set
            {
                if (this.value.MinCPUState != value)
                {
                    this.value.MinCPUState = value;
                }
            }
        }

        // CPU advanced (ToothNClaw port)
        public int ProcessorSchedulingPolicy
        {
            get { return value.ProcessorSchedulingPolicy; }
            set { if (this.value.ProcessorSchedulingPolicy != value) this.value.ProcessorSchedulingPolicy = value; }
        }

        public int MaxPCoreFreqMHz
        {
            get { return value.MaxPCoreFreqMHz; }
            set { if (this.value.MaxPCoreFreqMHz != value) this.value.MaxPCoreFreqMHz = value; }
        }

        public int MaxECoreFreqMHz
        {
            get { return value.MaxECoreFreqMHz; }
            set { if (this.value.MaxECoreFreqMHz != value) this.value.MaxECoreFreqMHz = value; }
        }

        // Intel Display (IGCL) — nullable (null = not configured)
        public int? IntelAdaptiveSharpness
        {
            get { return value.IntelAdaptiveSharpness; }
            set { if (this.value.IntelAdaptiveSharpness != value) this.value.IntelAdaptiveSharpness = value; }
        }

        public int? IntelColorSaturation
        {
            get { return value.IntelColorSaturation; }
            set { if (this.value.IntelColorSaturation != value) this.value.IntelColorSaturation = value; }
        }

        public int? IntelColorHue
        {
            get { return value.IntelColorHue; }
            set { if (this.value.IntelColorHue != value) this.value.IntelColorHue = value; }
        }

        public int? IntelDisplayContrast
        {
            get { return value.IntelDisplayContrast; }
            set { if (this.value.IntelDisplayContrast != value) this.value.IntelDisplayContrast = value; }
        }

        public int? IntelDisplayBrightness
        {
            get { return value.IntelDisplayBrightness; }
            set { if (this.value.IntelDisplayBrightness != value) this.value.IntelDisplayBrightness = value; }
        }

        public int? IntelDisplayGamma
        {
            get { return value.IntelDisplayGamma; }
            set { if (this.value.IntelDisplayGamma != value) this.value.IntelDisplayGamma = value; }
        }

        // Intel gaming 3D features (IGCL) — nullable (null = not configured)
        public int? IntelLowLatency
        {
            get { return value.IntelLowLatency; }
            set { if (this.value.IntelLowLatency != value) this.value.IntelLowLatency = value; }
        }

        public int? IntelFrameSync
        {
            get { return value.IntelFrameSync; }
            set { if (this.value.IntelFrameSync != value) this.value.IntelFrameSync = value; }
        }

        public bool TDPBoostEnabled
        {
            get { return value.TDPBoostEnabled; }
            set
            {
                if (this.value.TDPBoostEnabled != value)
                {
                    this.value.TDPBoostEnabled = value;
                }
            }
        }

        public GameId GameId
        {
            get { return value.GameId; }
        }

        public bool Use
        {
            get { return value.Use; }
            set
            {
                if (this.value.Use != value)
                {
                    this.value.Use = value;
                }
            }
        }

        public bool IsGlobalProfile
        {
            get { return value.IsGlobalProfile; }
        }

        // Legion controller remapping properties
        public string LegionButtonY1
        {
            get { return value.LegionButtonY1; }
            set
            {
                if (this.value.LegionButtonY1 != value)
                {
                    this.value.LegionButtonY1 = value;
                }
            }
        }

        public string LegionButtonY2
        {
            get { return value.LegionButtonY2; }
            set
            {
                if (this.value.LegionButtonY2 != value)
                {
                    this.value.LegionButtonY2 = value;
                }
            }
        }

        public string LegionButtonY3
        {
            get { return value.LegionButtonY3; }
            set
            {
                if (this.value.LegionButtonY3 != value)
                {
                    this.value.LegionButtonY3 = value;
                }
            }
        }

        public string LegionButtonM2
        {
            get { return value.LegionButtonM2; }
            set
            {
                if (this.value.LegionButtonM2 != value)
                {
                    this.value.LegionButtonM2 = value;
                }
            }
        }

        public string LegionButtonM3
        {
            get { return value.LegionButtonM3; }
            set
            {
                if (this.value.LegionButtonM3 != value)
                {
                    this.value.LegionButtonM3 = value;
                }
            }
        }

        public string LegionButtonDesktop
        {
            get { return value.LegionButtonDesktop; }
            set
            {
                if (this.value.LegionButtonDesktop != value)
                {
                    this.value.LegionButtonDesktop = value;
                }
            }
        }

        public string LegionButtonPage
        {
            get { return value.LegionButtonPage; }
            set
            {
                if (this.value.LegionButtonPage != value)
                {
                    this.value.LegionButtonPage = value;
                }
            }
        }

        public int? LegionGyroButton
        {
            get { return value.LegionGyroButton; }
            set
            {
                if (this.value.LegionGyroButton != value)
                {
                    this.value.LegionGyroButton = value;
                }
            }
        }

        // Additional Legion controller settings

        public bool? LegionControllerProfileEnabled
        {
            get { return value.LegionControllerProfileEnabled; }
            set
            {
                if (this.value.LegionControllerProfileEnabled != value)
                {
                    this.value.LegionControllerProfileEnabled = value;
                }
            }
        }

        public string LegionButtonM1
        {
            get { return value.LegionButtonM1; }
            set
            {
                if (this.value.LegionButtonM1 != value)
                {
                    this.value.LegionButtonM1 = value;
                }
            }
        }

        public int? LegionGyroTarget
        {
            get { return value.LegionGyroTarget; }
            set
            {
                if (this.value.LegionGyroTarget != value)
                {
                    this.value.LegionGyroTarget = value;
                }
            }
        }

        public int? LegionGyroSensitivityX
        {
            get { return value.LegionGyroSensitivityX; }
            set
            {
                if (this.value.LegionGyroSensitivityX != value)
                {
                    this.value.LegionGyroSensitivityX = value;
                }
            }
        }

        public int? LegionGyroSensitivityY
        {
            get { return value.LegionGyroSensitivityY; }
            set
            {
                if (this.value.LegionGyroSensitivityY != value)
                {
                    this.value.LegionGyroSensitivityY = value;
                }
            }
        }

        public bool? LegionGyroInvertX
        {
            get { return value.LegionGyroInvertX; }
            set
            {
                if (this.value.LegionGyroInvertX != value)
                {
                    this.value.LegionGyroInvertX = value;
                }
            }
        }

        public bool? LegionGyroInvertY
        {
            get { return value.LegionGyroInvertY; }
            set
            {
                if (this.value.LegionGyroInvertY != value)
                {
                    this.value.LegionGyroInvertY = value;
                }
            }
        }

        public int? LegionGyroMappingType
        {
            get { return value.LegionGyroMappingType; }
            set
            {
                if (this.value.LegionGyroMappingType != value)
                {
                    this.value.LegionGyroMappingType = value;
                }
            }
        }

        public int? LegionGyroActivationMode
        {
            get { return value.LegionGyroActivationMode; }
            set
            {
                if (this.value.LegionGyroActivationMode != value)
                {
                    this.value.LegionGyroActivationMode = value;
                }
            }
        }

        public int? LegionGyroDeadzone
        {
            get { return value.LegionGyroDeadzone; }
            set
            {
                if (this.value.LegionGyroDeadzone != value)
                {
                    this.value.LegionGyroDeadzone = value;
                }
            }
        }

        public int? LegionLeftStickDeadzone
        {
            get { return value.LegionLeftStickDeadzone; }
            set
            {
                if (this.value.LegionLeftStickDeadzone != value)
                {
                    this.value.LegionLeftStickDeadzone = value;
                }
            }
        }

        public int? LegionRightStickDeadzone
        {
            get { return value.LegionRightStickDeadzone; }
            set
            {
                if (this.value.LegionRightStickDeadzone != value)
                {
                    this.value.LegionRightStickDeadzone = value;
                }
            }
        }

        public int? LegionLeftTriggerStart
        {
            get { return value.LegionLeftTriggerStart; }
            set
            {
                if (this.value.LegionLeftTriggerStart != value)
                {
                    this.value.LegionLeftTriggerStart = value;
                }
            }
        }

        public int? LegionLeftTriggerEnd
        {
            get { return value.LegionLeftTriggerEnd; }
            set
            {
                if (this.value.LegionLeftTriggerEnd != value)
                {
                    this.value.LegionLeftTriggerEnd = value;
                }
            }
        }

        public int? LegionRightTriggerStart
        {
            get { return value.LegionRightTriggerStart; }
            set
            {
                if (this.value.LegionRightTriggerStart != value)
                {
                    this.value.LegionRightTriggerStart = value;
                }
            }
        }

        public int? LegionRightTriggerEnd
        {
            get { return value.LegionRightTriggerEnd; }
            set
            {
                if (this.value.LegionRightTriggerEnd != value)
                {
                    this.value.LegionRightTriggerEnd = value;
                }
            }
        }

        public bool? LegionHairTriggers
        {
            get { return value.LegionHairTriggers; }
            set
            {
                if (this.value.LegionHairTriggers != value)
                {
                    this.value.LegionHairTriggers = value;
                }
            }
        }

        public int? LegionJoystickAsMouseMode
        {
            get { return value.LegionJoystickAsMouseMode; }
            set
            {
                if (this.value.LegionJoystickAsMouseMode != value)
                {
                    this.value.LegionJoystickAsMouseMode = value;
                }
            }
        }

        public int? LegionJoystickMouseSens
        {
            get { return value.LegionJoystickMouseSens; }
            set
            {
                if (this.value.LegionJoystickMouseSens != value)
                {
                    this.value.LegionJoystickMouseSens = value;
                }
            }
        }

        public string LegionGamepadMapping
        {
            get { return value.LegionGamepadMapping; }
            set
            {
                if (this.value.LegionGamepadMapping != value)
                {
                    this.value.LegionGamepadMapping = value;
                }
            }
        }

        public bool? LegionNintendoLayout
        {
            get { return value.LegionNintendoLayout; }
            set
            {
                if (this.value.LegionNintendoLayout != value)
                {
                    this.value.LegionNintendoLayout = value;
                }
            }
        }

        public int? LegionVibration
        {
            get { return value.LegionVibration; }
            set
            {
                if (this.value.LegionVibration != value)
                {
                    this.value.LegionVibration = value;
                }
            }
        }

        public int? LegionVibrationMode
        {
            get { return value.LegionVibrationMode; }
            set
            {
                if (this.value.LegionVibrationMode != value)
                {
                    this.value.LegionVibrationMode = value;
                }
            }
        }

        public int? LegionVibrationIntensity
        {
            get { return value.LegionVibrationIntensity; }
            set
            {
                if (this.value.LegionVibrationIntensity != value)
                {
                    this.value.LegionVibrationIntensity = value;
                }
            }
        }

        public int? LegionPerformanceMode
        {
            get { return value.LegionPerformanceMode; }
            set
            {
                if (this.value.LegionPerformanceMode != value)
                {
                    this.value.LegionPerformanceMode = value;
                }
            }
        }

        // Lighting properties
        public int? LegionLightMode
        {
            get { return value.LegionLightMode; }
            set
            {
                if (this.value.LegionLightMode != value)
                {
                    this.value.LegionLightMode = value;
                }
            }
        }

        public string LegionLightColor
        {
            get { return value.LegionLightColor; }
            set
            {
                if (this.value.LegionLightColor != value)
                {
                    this.value.LegionLightColor = value;
                }
            }
        }

        public int? LegionLightBrightness
        {
            get { return value.LegionLightBrightness; }
            set
            {
                if (this.value.LegionLightBrightness != value)
                {
                    this.value.LegionLightBrightness = value;
                }
            }
        }

        public int? LegionLightSpeed
        {
            get { return value.LegionLightSpeed; }
            set
            {
                if (this.value.LegionLightSpeed != value)
                {
                    this.value.LegionLightSpeed = value;
                }
            }
        }

        public bool? LegionPowerLight
        {
            get { return value.LegionPowerLight; }
            set
            {
                if (this.value.LegionPowerLight != value)
                {
                    this.value.LegionPowerLight = value;
                }
            }
        }

        // FPS limiter properties (RTSS + Intel IGCL)
        public int FPSLimit
        {
            get { return value.FPSLimit; }
            set
            {
                if (this.value.FPSLimit != value)
                    this.value.FPSLimit = value;
            }
        }

        public int FpsCapMode
        {
            get { return value.FpsCapMode; }
            set
            {
                if (this.value.FpsCapMode != value)
                    this.value.FpsCapMode = value;
            }
        }

        public int IntelFpsTier
        {
            get { return value.IntelFpsTier; }
            set
            {
                if (this.value.IntelFpsTier != value)
                    this.value.IntelFpsTier = value;
            }
        }

        // AutoTDP properties
        public bool AutoTDPEnabled
        {
            get { return value.AutoTDPEnabled; }
            set
            {
                if (this.value.AutoTDPEnabled != value)
                {
                    this.value.AutoTDPEnabled = value;
                }
            }
        }

        public int AutoTDPTargetFPS
        {
            get { return value.AutoTDPTargetFPS; }
            set
            {
                if (this.value.AutoTDPTargetFPS != value)
                {
                    this.value.AutoTDPTargetFPS = value;
                }
            }
        }

        public int AutoTDPMinTDP
        {
            get { return value.AutoTDPMinTDP; }
            set
            {
                if (this.value.AutoTDPMinTDP != value)
                {
                    this.value.AutoTDPMinTDP = value;
                }
            }
        }

        public int AutoTDPMaxTDP
        {
            get { return value.AutoTDPMaxTDP; }
            set
            {
                if (this.value.AutoTDPMaxTDP != value)
                {
                    this.value.AutoTDPMaxTDP = value;
                }
            }
        }

        public bool AutoTDPUseMLMode
        {
            get { return value.AutoTDPUseMLMode; }
            set
            {
                if (this.value.AutoTDPUseMLMode != value)
                {
                    this.value.AutoTDPUseMLMode = value;
                }
            }
        }

        public int AutoTDPControllerType
        {
            get { return value.AutoTDPControllerType; }
            set
            {
                if (this.value.AutoTDPControllerType != value)
                {
                    this.value.AutoTDPControllerType = value;
                }
            }
        }

        public bool AutoTDPPauseWhenUnfocused
        {
            get { return value.AutoTDPPauseWhenUnfocused; }
            set
            {
                if (this.value.AutoTDPPauseWhenUnfocused != value)
                {
                    this.value.AutoTDPPauseWhenUnfocused = value;
                }
            }
        }
    }
}
