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
}
