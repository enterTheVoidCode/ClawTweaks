using NLog;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Devices.Libraries.GPD
{
    // Helper class for GPD button position constants
    // Based on reference implementation (GPDWin5Device.cpp)
    internal static class GPDButtonPosition
    {
        public const int DPadUp = 0;
        public const int DPadDown = 1;
        public const int DPadLeft = 2;
        public const int DPadRight = 3;
        public const int L3 = 4;          // Fixed from 15
        public const int R3 = 5;          // Fixed from 16
        // position 6 unused
        public const int A = 7;
        public const int B = 8;
        public const int X = 9;
        public const int Select = 10;
        public const int Y = 11;          // Fixed from 10
        public const int LB = 12;
        public const int RB = 13;
        public const int LT = 14;
        public const int L4 = 15;
        public const int RT = 16;
        public const int Menu = 17;       // Start
        public const int View = 18;       // Back
        public const int Xbox = 19;
        public const int LeftStickLeft = 20;
        public const int LeftStickRight = 21;
        // Note: LeftStickUp/Down may not be supported by protocol
        // Kept for backwards compatibility with UI, but may not work
        public const int LeftStickUp = -2;
        public const int LeftStickDown = -3;
        public const int R4Paddle = -1;   // Special handling
    }

    /// <summary>
    /// Read-only property indicating if a GPD device (Win Mini, Win 4, Win 5, etc.) is detected.
    /// Sent from helper to widget to control GPD tab visibility.
    /// </summary>
    internal class GPDDetectedProperty : HelperProperty<bool, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDDetectedProperty(bool initialValue, GPDManager inManager)
            : base(initialValue, null, Function.GPDDetected, inManager)
        {
            Logger.Debug($"[GPD] GPDDetectedProperty created with initial value: {initialValue}");
        }

        /// <summary>
        /// Updates the detected state and notifies the widget
        /// </summary>
        public void SetDetected(bool detected)
        {
            if (Value != detected)
            {
                Logger.Info($"[GPD] GPD detected state changed: {Value} -> {detected}");
                SetValue(detected);
            }
        }
    }

    /// <summary>
    /// Read-only property indicating if the GPD Win 5 HID controller is connected.
    /// Sent from helper to widget to show Win 5 specific features.
    /// Also indicates that the device IS a Win 5 (even if HID not connected).
    /// </summary>
    internal class GPDWin5ConnectedProperty : HelperProperty<bool, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private bool _hasNotifiedWidget = false;

        public GPDWin5ConnectedProperty(bool initialValue, GPDManager inManager)
            : base(initialValue, null, Function.GPDWin5Connected, inManager)
        {
            Logger.Debug($"[GPDWin5] GPDWin5ConnectedProperty created with initial value: {initialValue}");
        }

        /// <summary>
        /// Updates the connected state and notifies the widget.
        /// The first call always notifies (even if value unchanged) to signal
        /// that this IS a Win 5 device.
        /// </summary>
        public void SetConnected(bool connected)
        {
            // Always notify on first call to indicate this is a Win 5 device
            // Subsequent calls only notify if value changed
            if (!_hasNotifiedWidget || Value != connected)
            {
                Logger.Info($"[GPDWin5] Win 5 controller connected state: {connected} (first notify: {!_hasNotifiedWidget})");
                _hasNotifiedWidget = true;
                SetValue(connected);
            }
        }
    }

    /// <summary>
    /// Read-only property for the GPD device display name (e.g., "GPD Win 5").
    /// Set based on SMBIOS detection, independent of HID controller connection.
    /// </summary>
    internal class GPDDeviceNameProperty : HelperProperty<string, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDDeviceNameProperty(string initialValue, GPDManager inManager)
            : base(initialValue, null, Function.GPDDeviceName, inManager)
        {
            Logger.Debug($"[GPD] GPDDeviceNameProperty created with value: {initialValue}");
        }
    }

    /// <summary>
    /// Read-only property indicating if the GPD device supports fan control.
    /// This is based on device detection, independent of HID controller connection.
    /// </summary>
    internal class GPDSupportsFanControlProperty : HelperProperty<bool, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDSupportsFanControlProperty(bool initialValue, GPDManager inManager)
            : base(initialValue, null, Function.GPDSupportsFanControl, inManager)
        {
            Logger.Debug($"[GPD] GPDSupportsFanControlProperty created with value: {initialValue}");
        }
    }

    /// <summary>
    /// Trigger property to restore default button mappings on GPD Win 5.
    /// When the widget sets this to true, the helper restores default mappings.
    /// </summary>
    internal class GPDRestoreDefaultsProperty : HelperProperty<bool, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDRestoreDefaultsProperty(GPDManager inManager)
            : base(false, null, Function.GPDRestoreDefaults, inManager)
        {
            Logger.Debug("[GPDWin5] GPDRestoreDefaultsProperty created");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // When widget sets this to true, restore default mappings
            if (Value)
            {
                Logger.Info("[GPDWin5] RestoreDefaults trigger received from widget");

                bool success = Manager.RestoreDefaultMappings();

                if (success)
                {
                    Logger.Info("[GPDWin5] Successfully restored default button mappings");
                }
                else
                {
                    Logger.Warn("[GPDWin5] Failed to restore default button mappings (not connected or not Win 5)");
                }

                // Reset the trigger
                SetValue(false, 0);
            }
        }
    }

    /// <summary>
    /// Base class for GPD button remapping properties.
    /// When widget sends a keycode, the helper calls GPDManager.RemapButton().
    /// </summary>
    internal abstract class GPDButtonPropertyBase : HelperProperty<int, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        protected readonly int ButtonPosition;
        protected readonly string ButtonName;

        protected GPDButtonPropertyBase(int buttonPosition, string buttonName, Function function, GPDManager inManager)
            : base(0, null, function, inManager)
        {
            ButtonPosition = buttonPosition;
            ButtonName = buttonName;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            ushort keycode = (ushort)Value;
            Logger.Info($"[GPD] {ButtonName} property changed to keycode 0x{keycode:X4}");

            if (Manager != null && keycode != 0)
            {
                bool success = Manager.RemapButton(ButtonPosition, keycode);
                Logger.Info($"[GPD] {ButtonName} remap result: {(success ? "success" : "failed")}");
            }
        }
    }

    // GPD Button A Property
    internal class GPDButtonAProperty : GPDButtonPropertyBase
    {
        public GPDButtonAProperty(GPDManager inManager)
            : base(GPDButtonPosition.A, "ButtonA", Function.GPDButtonA, inManager) { }
    }

    // GPD Button B Property
    internal class GPDButtonBProperty : GPDButtonPropertyBase
    {
        public GPDButtonBProperty(GPDManager inManager)
            : base(GPDButtonPosition.B, "ButtonB", Function.GPDButtonB, inManager) { }
    }

    // GPD Button X Property
    internal class GPDButtonXProperty : GPDButtonPropertyBase
    {
        public GPDButtonXProperty(GPDManager inManager)
            : base(GPDButtonPosition.X, "ButtonX", Function.GPDButtonX, inManager) { }
    }

    // GPD Button Y Property
    internal class GPDButtonYProperty : GPDButtonPropertyBase
    {
        public GPDButtonYProperty(GPDManager inManager)
            : base(GPDButtonPosition.Y, "ButtonY", Function.GPDButtonY, inManager) { }
    }

    // GPD D-Pad Up Property
    internal class GPDButtonDPadUpProperty : GPDButtonPropertyBase
    {
        public GPDButtonDPadUpProperty(GPDManager inManager)
            : base(GPDButtonPosition.DPadUp, "DPadUp", Function.GPDButtonDPadUp, inManager) { }
    }

    // GPD D-Pad Down Property
    internal class GPDButtonDPadDownProperty : GPDButtonPropertyBase
    {
        public GPDButtonDPadDownProperty(GPDManager inManager)
            : base(GPDButtonPosition.DPadDown, "DPadDown", Function.GPDButtonDPadDown, inManager) { }
    }

    // GPD D-Pad Left Property
    internal class GPDButtonDPadLeftProperty : GPDButtonPropertyBase
    {
        public GPDButtonDPadLeftProperty(GPDManager inManager)
            : base(GPDButtonPosition.DPadLeft, "DPadLeft", Function.GPDButtonDPadLeft, inManager) { }
    }

    // GPD D-Pad Right Property
    internal class GPDButtonDPadRightProperty : GPDButtonPropertyBase
    {
        public GPDButtonDPadRightProperty(GPDManager inManager)
            : base(GPDButtonPosition.DPadRight, "DPadRight", Function.GPDButtonDPadRight, inManager) { }
    }

    // GPD L3 Property
    internal class GPDButtonL3Property : GPDButtonPropertyBase
    {
        public GPDButtonL3Property(GPDManager inManager)
            : base(GPDButtonPosition.L3, "L3", Function.GPDButtonL3, inManager) { }
    }

    // GPD R3 Property
    internal class GPDButtonR3Property : GPDButtonPropertyBase
    {
        public GPDButtonR3Property(GPDManager inManager)
            : base(GPDButtonPosition.R3, "R3", Function.GPDButtonR3, inManager) { }
    }

    // GPD Left Stick Up Property
    internal class GPDButtonLSUpProperty : GPDButtonPropertyBase
    {
        public GPDButtonLSUpProperty(GPDManager inManager)
            : base(GPDButtonPosition.LeftStickUp, "LSUp", Function.GPDButtonLSUp, inManager) { }
    }

    // GPD Left Stick Down Property
    internal class GPDButtonLSDownProperty : GPDButtonPropertyBase
    {
        public GPDButtonLSDownProperty(GPDManager inManager)
            : base(GPDButtonPosition.LeftStickDown, "LSDown", Function.GPDButtonLSDown, inManager) { }
    }

    // GPD Left Stick Left Property
    internal class GPDButtonLSLeftProperty : GPDButtonPropertyBase
    {
        public GPDButtonLSLeftProperty(GPDManager inManager)
            : base(GPDButtonPosition.LeftStickLeft, "LSLeft", Function.GPDButtonLSLeft, inManager) { }
    }

    // GPD Left Stick Right Property
    internal class GPDButtonLSRightProperty : GPDButtonPropertyBase
    {
        public GPDButtonLSRightProperty(GPDManager inManager)
            : base(GPDButtonPosition.LeftStickRight, "LSRight", Function.GPDButtonLSRight, inManager) { }
    }

    // GPD L4 Paddle Property - uses regular button remapping at position 15
    internal class GPDButtonL4Property : GPDButtonPropertyBase
    {
        public GPDButtonL4Property(GPDManager inManager)
            : base(GPDButtonPosition.L4, "L4", Function.GPDButtonL4, inManager) { }
    }

    /// <summary>
    /// GPD R4 Paddle Property - special handling as it uses a different packet.
    /// </summary>
    internal class GPDButtonR4Property : HelperProperty<int, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDButtonR4Property(GPDManager inManager)
            : base(0, null, Function.GPDButtonR4, inManager) { }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            ushort keycode = (ushort)Value;
            Logger.Info($"[GPD] R4 paddle property changed to keycode 0x{keycode:X4}");

            if (Manager != null && keycode != 0)
            {
                // R4 requires remapping all buttons with the R4 keycode
                // Use RemapButtons with empty mappings but custom R4 keycode
                var mappings = new System.Collections.Generic.Dictionary<int, ushort>();
                bool success = Manager.RemapButtons(mappings, keycode);
                Logger.Info($"[GPD] R4 paddle remap result: {(success ? "success" : "failed")}");
            }
        }
    }

    #region Fan Control Properties

    /// <summary>
    /// Fan speed property - widget sends percentage (0 = auto, 30-100 = manual).
    /// </summary>
    internal class GPDFanSpeedProperty : HelperProperty<int, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDFanSpeedProperty(GPDManager inManager)
            : base(0, null, Function.GPDFanSpeed, inManager)
        {
            Logger.Debug("[GPDFan] GPDFanSpeedProperty created");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            int percent = Value;
            Logger.Info($"[GPDFan] Fan speed property changed to {percent}%");

            if (Manager != null)
            {
                bool success = Manager.SetFanSpeed(percent);
                Logger.Info($"[GPDFan] SetFanSpeed result: {(success ? "success" : "failed")}");
            }
        }
    }

    /// <summary>
    /// Fan RPM property - read-only, sent from helper to widget.
    /// Updated periodically by GPDManager.
    /// </summary>
    internal class GPDFanRPMProperty : HelperProperty<int, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDFanRPMProperty(GPDManager inManager)
            : base(0, null, Function.GPDFanRPM, inManager)
        {
            Logger.Debug("[GPDFan] GPDFanRPMProperty created");
        }

        /// <summary>
        /// Updates the RPM value and notifies the widget.
        /// Called by GPDManager during update loop.
        /// </summary>
        public void UpdateRPM(int rpm)
        {
            if (Value != rpm)
            {
                SetValue(rpm);
            }
        }
    }

    /// <summary>
    /// Fan mode property - 0 = auto, 1 = manual.
    /// Widget can set this, or it's updated when fan speed changes.
    /// </summary>
    internal class GPDFanModeProperty : HelperProperty<int, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDFanModeProperty(GPDManager inManager)
            : base(0, null, Function.GPDFanMode, inManager)
        {
            Logger.Debug("[GPDFan] GPDFanModeProperty created");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            int mode = Value;
            Logger.Info($"[GPDFan] Fan mode property changed to {(mode == 0 ? "Auto" : "Manual")}");

            if (Manager != null)
            {
                var fanMode = mode == 0 ? GPDFanMode.Auto : GPDFanMode.Manual;
                Manager.SetFanMode(fanMode);
            }
        }

        /// <summary>
        /// Updates the mode value and notifies the widget (used internally).
        /// </summary>
        public void UpdateMode(GPDFanMode mode)
        {
            int modeInt = mode == GPDFanMode.Auto ? 0 : 1;
            if (Value != modeInt)
            {
                SetValue(modeInt);
            }
        }
    }

    #endregion

    #region Software Fan Curve Properties

    /// <summary>
    /// Fan curve enabled property - widget toggle to enable/disable the software fan curve.
    /// When enabled, the helper periodically reads CPU temp and sets fan speed via interpolation.
    /// </summary>
    internal class GPDFanCurveEnabledProperty : HelperProperty<bool, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDFanCurveEnabledProperty(GPDManager inManager)
            : base(false, null, Function.GPDFanCurveEnabled, inManager)
        {
            Logger.Debug("[GPDFan] GPDFanCurveEnabledProperty created");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"[GPDFan] Fan curve enabled changed to: {Value}");
            Manager?.SetFanCurveEnabled(Value);
        }
    }

    /// <summary>
    /// Fan curve data property - comma-separated 10 fan speed values from the widget.
    /// </summary>
    internal class GPDFanCurveDataProperty : HelperProperty<string, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDFanCurveDataProperty(GPDManager inManager)
            : base("0,30,35,45,55,65,75,85,95,100", null, Function.GPDFanCurveData, inManager)
        {
            Logger.Debug("[GPDFan] GPDFanCurveDataProperty created");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"[GPDFan] Fan curve data changed to: {Value}");
            Manager?.SetFanCurveData(Value);
        }
    }

    /// <summary>
    /// Fan curve visible property - widget tells helper when the graph UI is visible.
    /// Helper pushes CPU temp updates only when visible.
    /// </summary>
    internal class GPDFanCurveVisibleProperty : HelperProperty<bool, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDFanCurveVisibleProperty(GPDManager inManager)
            : base(false, null, Function.GPDFanCurveVisible, inManager)
        {
            Logger.Debug("[GPDFan] GPDFanCurveVisibleProperty created");
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"[GPDFan] Fan curve visible changed to: {Value}");
            Manager?.SetFanCurveVisible(Value);
        }
    }

    /// <summary>
    /// CPU temperature property - read-only, pushed from helper to widget for the fan curve graph.
    /// </summary>
    internal class GPDCPUTempProperty : HelperProperty<int, GPDManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GPDCPUTempProperty(GPDManager inManager)
            : base(0, null, Function.GPDCPUTemp, inManager)
        {
            Logger.Debug("[GPDFan] GPDCPUTempProperty created");
        }

        /// <summary>
        /// Updates the CPU temperature and notifies the widget.
        /// </summary>
        public void UpdateTemp(int temp)
        {
            if (Value != temp)
            {
                SetValue(temp);
            }
        }
    }

    #endregion
}
