using NLog;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Legion
{
    // Legion Go detection (read-only)
    internal class LegionGoDetectedProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGoDetectedProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGoDetected, inManager)
        {
        }

        public void SetDetected(bool detected)
        {
            SetValue((object)detected);
        }
    }

    // Touchpad control
    internal class LegionTouchpadEnabledProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionTouchpadEnabledProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionTouchpadEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionTouchpadEnabled changed to {Value}");
            Manager?.SetTouchpadEnabled(Value);
        }
    }

    // Light mode (0=Off, 1=Solid, 2=Pulse, 3=Dynamic, 4=Spiral)
    internal class LegionLightModeProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionLightModeProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionLightMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionLightMode changed to {Value}");
            Manager?.SetLightMode(Value);
        }
    }

    // Light color (hex string "#RRGGBB")
    internal class LegionLightColorProperty : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionLightColorProperty(string initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionLightColor, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionLightColor changed to {Value}");
            Manager?.SetLightColor(Value);
        }
    }

    // Light brightness (0-100)
    internal class LegionLightBrightnessProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionLightBrightnessProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionLightBrightness, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionLightBrightness changed to {Value}");
            Manager?.SetLightBrightness(Value);
        }
    }

    // Light speed (0-100, for animated modes)
    internal class LegionLightSpeedProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionLightSpeedProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionLightSpeed, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionLightSpeed changed to {Value}");
            Manager?.SetLightSpeed(Value);
        }
    }

    // Performance mode (1=Quiet, 2=Balanced, 3=Performance, 255=Custom)
    internal class LegionPerformanceModeProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionPerformanceModeProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionPerformanceMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionPerformanceMode changed to {Value}");
            Manager?.SetPerformanceMode(Value);
        }
    }

    // Custom TDP Slow (SPL) in watts
    internal class LegionCustomTDPSlowProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionCustomTDPSlowProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionCustomTDPSlow, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionCustomTDPSlow changed to {Value}W");
            Manager?.ApplyCustomTDPSlow(Value);
        }
    }

    // Custom TDP Fast (SPPL) in watts
    internal class LegionCustomTDPFastProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionCustomTDPFastProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionCustomTDPFast, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionCustomTDPFast changed to {Value}W");
            Manager?.ApplyCustomTDPFast(Value);
        }
    }

    // Custom TDP Peak (FPPT) in watts
    internal class LegionCustomTDPPeakProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionCustomTDPPeakProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionCustomTDPPeak, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionCustomTDPPeak changed to {Value}W");
            Manager?.ApplyCustomTDPPeak(Value);
        }
    }

    // Fan full speed toggle
    internal class LegionFanFullSpeedProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionFanFullSpeedProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionFanFullSpeed, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionFanFullSpeed changed to {Value}");
            Manager?.SetFanFullSpeed(Value);
        }
    }

    // Gyro control (WIP)
    internal class LegionGyroEnabledProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroEnabledProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroEnabled changed to {Value} (WIP - not functional)");
            Manager?.SetGyroEnabled(Value);
        }
    }

    // Vibration level (0=Off, 1=Weak, 2=Medium, 3=Strong)
    internal class LegionVibrationProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionVibrationProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionVibration, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionVibration changed to {Value}");
            Manager?.SetVibration(Value);
        }
    }

    // Power Light toggle
    internal class LegionPowerLightProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionPowerLightProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionPowerLight, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionPowerLight changed to {Value}");
            Manager?.SetPowerLight(Value);
        }
    }

    // Battery Charge Limit (80%)
    internal class LegionChargeLimitProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionChargeLimitProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionChargeLimit, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionChargeLimit changed to {Value}");
            Manager?.SetChargeLimit(Value);
        }
    }

    // Button Y1 remap action (0-28)
    internal class LegionButtonY1Property : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonY1Property(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionButtonY1, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionButtonY1 changed to {Value}");
            Manager?.SetButtonMapping(0, Value);
        }
    }

    // Button Y2 remap action (0-28)
    internal class LegionButtonY2Property : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonY2Property(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionButtonY2, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionButtonY2 changed to {Value}");
            Manager?.SetButtonMapping(1, Value);
        }
    }

    // Button Y3 remap action (0-28)
    internal class LegionButtonY3Property : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonY3Property(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionButtonY3, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionButtonY3 changed to {Value}");
            Manager?.SetButtonMapping(2, Value);
        }
    }

    // Button M2 remap action (0-28)
    internal class LegionButtonM2Property : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonM2Property(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionButtonM2, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionButtonM2 changed to {Value}");
            Manager?.SetButtonMapping(3, Value);
        }
    }

    // Button M3 remap action (0-28)
    internal class LegionButtonM3Property : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonM3Property(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionButtonM3, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionButtonM3 changed to {Value}");
            Manager?.SetButtonMapping(4, Value);
        }
    }

    // Nintendo layout toggle (A↔B, X↔Y swap)
    internal class LegionNintendoLayoutProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionNintendoLayoutProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionNintendoLayout, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionNintendoLayout changed to {Value}");
            Manager?.SetNintendoLayout(Value);
        }
    }

    // Vibration mode preset (FPS=1, Racing=2, AVG=3, SPG=4, RPG=5)
    internal class LegionVibrationModeProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionVibrationModeProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionVibrationMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionVibrationMode changed to {Value}");
            Manager?.SetVibrationMode(Value);
        }
    }

    // Controller profile enabled (per-game toggle) - notification only, storage in widget
    internal class LegionControllerProfileEnabledProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionControllerProfileEnabledProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionControllerProfileEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionControllerProfileEnabled changed to {Value}");
            // No action needed - profile storage is handled by the widget
        }
    }

    // Gyro Target (0=Disabled, 1=LeftStick, 2=RightStick, 3=Mouse)
    internal class LegionGyroTargetProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroTargetProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroTarget, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroTarget changed to {Value}");
            Manager?.SetGyroTarget(Value);
        }
    }

    // Gyro Sensitivity X (1-100)
    internal class LegionGyroSensitivityXProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroSensitivityXProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroSensitivityX, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroSensitivityX changed to {Value}");
            Manager?.SetGyroSensitivityX(Value);
        }
    }

    // Gyro Sensitivity Y (1-100)
    internal class LegionGyroSensitivityYProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroSensitivityYProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroSensitivityY, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroSensitivityY changed to {Value}");
            Manager?.SetGyroSensitivityY(Value);
        }
    }

    // Gyro Invert X
    internal class LegionGyroInvertXProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroInvertXProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroInvertX, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroInvertX changed to {Value}");
            Manager?.SetGyroInvertX(Value);
        }
    }

    // Gyro Invert Y
    internal class LegionGyroInvertYProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroInvertYProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroInvertY, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroInvertY changed to {Value}");
            Manager?.SetGyroInvertY(Value);
        }
    }

    // Gyro Mapping Type (0=Instant, 1=Continuous)
    internal class LegionGyroMappingTypeProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroMappingTypeProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroMappingType, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroMappingType changed to {Value}");
            Manager?.SetGyroMappingType(Value);
        }
    }

    // Gyro Activation Mode (0=Hold, 1=Toggle)
    internal class LegionGyroActivationModeProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroActivationModeProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroActivationMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroActivationMode changed to {Value}");
            Manager?.SetGyroActivationMode(Value);
        }
    }

    // Gyro Activation Button (0-8: None, LB, LT, RB, RT, Y1, Y2, M2, M3)
    internal class LegionGyroActivationButtonProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroActivationButtonProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroActivationButton, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroActivationButton changed to {Value}");
            Manager?.SetGyroActivationButton(Value);
        }
    }

    // Left Stick Deadzone (0-50%)
    internal class LegionLeftStickDeadzoneProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionLeftStickDeadzoneProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionLeftStickDeadzone, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionLeftStickDeadzone changed to {Value}");
            Manager?.SetLeftStickDeadzone(Value);
        }
    }

    // Right Stick Deadzone (0-50%)
    internal class LegionRightStickDeadzoneProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionRightStickDeadzoneProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionRightStickDeadzone, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionRightStickDeadzone changed to {Value}");
            Manager?.SetRightStickDeadzone(Value);
        }
    }

    // Touchpad Vibration (on/off - GLOBAL setting)
    internal class LegionTouchpadVibrationProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionTouchpadVibrationProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionTouchpadVibration, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionTouchpadVibration changed to {Value}");
            Manager?.SetTouchpadVibration(Value);
        }
    }
}
