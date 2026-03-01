using NLog;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.ControllerEmulation
{
    internal class ControllerEmulationAvailableProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        public ControllerEmulationAvailableProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationAvailable, manager)
        {
        }
    }

    internal class ControllerEmulationEnabledProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationEnabledProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationEnabled, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationEnabled changed to {Value}");
            Manager?.SetEnabled(Value);
        }
    }

    internal class ControllerEmulationHideStockControllerProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationHideStockControllerProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationHideStockController, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationHideStockController changed to {Value}");
            Manager?.SetHideStockController(Value);
        }
    }

    internal class ControllerEmulationImprovedInputProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationImprovedInputProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationImprovedInput, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationImprovedInput changed to {Value}");
            Manager?.SetImprovedInputRead(Value);
        }
    }

    internal class ControllerEmulationHideTargetProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationHideTargetProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationHideTarget, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationHideTarget changed to {Value}");
            Manager?.SetHideTarget(Value);
        }
    }

    internal class ControllerEmulationGyroSourceProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationGyroSourceProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationGyroSource, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationGyroSource changed to {Value}");
            Manager?.SetGyroSource(Value);
        }
    }

    internal class ControllerEmulationModeProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationModeProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMode, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMode changed to {Value}");
            Manager?.SetMode(Value);
        }
    }

    internal class ControllerEmulationRumbleProfileProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationRumbleProfileProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationRumbleProfile, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationRumbleProfile changed to {Value}");
            Manager?.SetRumbleProfile(Value);
        }
    }

    internal class ControllerEmulationGyroActivationModeProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationGyroActivationModeProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationGyroActivationMode, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationGyroActivationMode changed to {Value}");
            Manager?.SetGyroActivationMode(Value);
        }
    }

    internal class ControllerEmulationGyroActivationButtonProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationGyroActivationButtonProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationGyroActivationButton, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationGyroActivationButton changed to {Value}");
            Manager?.SetGyroActivationButton(Value);
        }
    }

    internal class ControllerEmulationDs4OrientationProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationDs4OrientationProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationDs4Orientation, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationDs4Orientation changed to {Value}");
            Manager?.SetDs4Orientation(Value);
        }
    }

    internal class ControllerEmulationPs4TouchpadEnabledProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationPs4TouchpadEnabledProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationPs4TouchpadEnabled, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationPs4TouchpadEnabled changed to {Value}");
            Manager?.SetPs4TouchpadEnabled(Value);
        }
    }

    internal class ControllerEmulationMouseSensitivityProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationMouseSensitivityProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMouseSensitivity, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMouseSensitivity changed to {Value}");
            Manager?.SetMouseSensitivity(Value);
        }
    }

    internal class ControllerEmulationMouseThresholdProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationMouseThresholdProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMouseThreshold, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMouseThreshold changed to {Value}");
            Manager?.SetMouseThreshold(Value);
        }
    }

    internal class ControllerEmulationMouseAxisProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationMouseAxisProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMouseAxis, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMouseAxis changed to {Value}");
            Manager?.SetMouseAxis(Value);
        }
    }

    internal class ControllerEmulationMouseInvertXProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationMouseInvertXProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMouseInvertX, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMouseInvertX changed to {Value}");
            Manager?.SetMouseInvertX(Value);
        }
    }

    internal class ControllerEmulationMouseInvertYProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationMouseInvertYProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMouseInvertY, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMouseInvertY changed to {Value}");
            Manager?.SetMouseInvertY(Value);
        }
    }

    internal class ControllerEmulationMouseGainXProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationMouseGainXProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMouseGainX, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMouseGainX changed to {Value}");
            Manager?.SetMouseGainX(Value);
        }
    }

    internal class ControllerEmulationMouseGainYProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationMouseGainYProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMouseGainY, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMouseGainY changed to {Value}");
            Manager?.SetMouseGainY(Value);
        }
    }

    internal class ControllerEmulationStickSensitivityProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickSensitivityProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickSensitivity, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickSensitivity changed to {Value}");
            Manager?.SetStickSensitivity(Value);
        }
    }

    internal class ControllerEmulationStickThresholdProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickThresholdProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickThreshold, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickThreshold changed to {Value}");
            Manager?.SetStickThreshold(Value);
        }
    }

    internal class ControllerEmulationStickAxisProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickAxisProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickAxis, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickAxis changed to {Value}");
            Manager?.SetStickAxis(Value);
        }
    }

    internal class ControllerEmulationStickInvertXProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickInvertXProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickInvertX, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickInvertX changed to {Value}");
            Manager?.SetStickInvertX(Value);
        }
    }

    internal class ControllerEmulationStickInvertYProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickInvertYProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickInvertY, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickInvertY changed to {Value}");
            Manager?.SetStickInvertY(Value);
        }
    }

    internal class ControllerEmulationStickGainXProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickGainXProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGainX, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGainX changed to {Value}");
            Manager?.SetStickGainX(Value);
        }
    }

    internal class ControllerEmulationStickGainYProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickGainYProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGainY, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGainY changed to {Value}");
            Manager?.SetStickGainY(Value);
        }
    }

    internal class ControllerEmulationStickSelectProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickSelectProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickSelect, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickSelect changed to {Value}");
            Manager?.SetStickSelect(Value);
        }
    }

    internal class ControllerEmulationStickExcessMoveProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickExcessMoveProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickExcessMove, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickExcessMove changed to {Value}");
            Manager?.SetStickExcessMove(Value);
        }
    }

    internal class ControllerEmulationStickRangeProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickRangeProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickRange, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickRange changed to {Value}");
            Manager?.SetStickRange(Value);
        }
    }

    internal class ControllerEmulationStickOnlyJoystickDataProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickOnlyJoystickDataProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickOnlyJoystickData, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickOnlyJoystickData changed to {Value}");
            Manager?.SetStickOnlyJoystickData(Value);
        }
    }

    internal class ControllerEmulationVirtualABXYLayoutProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationVirtualABXYLayoutProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationVirtualABXYLayout, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationVirtualABXYLayout changed to {Value}");
            Manager?.SetVirtualABXYLayout(Value);
        }
    }

    internal class ControllerEmulationLedForwardingEnabledProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationLedForwardingEnabledProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationLedForwardingEnabled, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationLedForwardingEnabled changed to {Value}");
            Manager?.SetLedForwardingEnabled(Value);
        }
    }

    internal class ControllerEmulationCalibrateGyroProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationCalibrateGyroProperty(ControllerEmulationManager manager)
            : base(false, null, Function.ControllerEmulationCalibrateGyro, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (Value)
            {
                Logger.Info("ControllerEmulationCalibrateGyro triggered");
                Manager?.CalibrateGyro();
                SetValue(false);
            }
        }
    }
}
