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
    }
}
