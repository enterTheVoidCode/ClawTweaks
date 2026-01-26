using NLog;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Devices.Libraries.Legion
{
    /// <summary>
    /// Helper class to parse ButtonMapping JSON format
    /// Format: {"Type":0,"GamepadAction":5,"KeyboardKeys":[4,5,6],"MouseButton":0}
    /// </summary>
    internal static class ButtonMappingParser
    {
        public static (int type, int gamepadAction, int[] keyboardKeys, int mouseButton) Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return (0, 0, Array.Empty<int>(), 0);

            try
            {
                int type = ExtractInt(json, "Type") ?? 0;
                int gamepadAction = ExtractInt(json, "GamepadAction") ?? 0;
                int mouseButton = ExtractInt(json, "MouseButton") ?? 0;
                int[] keyboardKeys = ExtractIntArray(json, "KeyboardKeys");

                return (type, gamepadAction, keyboardKeys, mouseButton);
            }
            catch
            {
                return (0, 0, Array.Empty<int>(), 0);
            }
        }

        private static int? ExtractInt(string json, string property)
        {
            var match = Regex.Match(json, $"\"{property}\"\\s*:\\s*(-?\\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
                return value;
            return null;
        }

        private static int[] ExtractIntArray(string json, string property)
        {
            var match = Regex.Match(json, $"\"{property}\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!match.Success)
                return Array.Empty<int>();

            var values = new List<int>();
            var numbers = match.Groups[1].Value.Split(',');
            foreach (var num in numbers)
            {
                if (int.TryParse(num.Trim(), out int value))
                    values.Add(value);
            }
            return values.ToArray();
        }

        /// <summary>
        /// Returns true if the mapping JSON represents a default/empty mapping (Type=0, GamepadAction=0).
        /// Default mappings should not be applied as they would clear existing button mappings.
        /// </summary>
        public static bool IsDefaultMapping(string json)
        {
            if (string.IsNullOrEmpty(json))
                return true;

            var (type, gamepadAction, _, _) = Parse(json);
            // A mapping is default if Type=0 (Gamepad) and GamepadAction=0 (Disabled)
            return type == 0 && gamepadAction == 0;
        }

        /// <summary>
        /// Gets the mapping values to send to the HID command based on mapping type
        /// </summary>
        public static int[] GetMappingValues(int type, int gamepadAction, int[] keyboardKeys, int mouseButton)
        {
            switch (type)
            {
                case 0: // Gamepad
                    return gamepadAction > 0 ? new[] { gamepadAction } : Array.Empty<int>();
                case 1: // Keyboard
                    return keyboardKeys;
                case 2: // Mouse
                    return mouseButton > 0 ? new[] { mouseButton } : Array.Empty<int>();
                default:
                    return Array.Empty<int>();
            }
        }
    }

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

    // Fan curve data property - manages all 10 fan speed values as a comma-separated string
    internal class LegionFanCurveDataProperty : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private bool _initialized = false;
        private string _lastSentValue = null;

        public LegionFanCurveDataProperty(string initialValue, LegionManager inManager)
            : base(initialValue, null, Function.LegionFanCurveData, inManager)
        {
            _lastSentValue = initialValue; // Track initial value to avoid re-sending same curve
        }

        // Default fan curve string for comparison
        private const string DEFAULT_FAN_CURVE = "44,48,55,60,71,79,87,87,100,100";

        /// <summary>
        /// Call this after initial sync is complete to enable device writes.
        /// Only pushes the device-read value to widget if it's NOT the default curve.
        /// The WMI Fan_Get_Table typically returns defaults; actual custom curves may be stored elsewhere.
        /// </summary>
        public void EnableDeviceWrites()
        {
            // Only push to widget if we got a NON-default curve from the device
            // If WMI returned defaults, let the widget keep its own state
            bool isDefaultCurve = _lastSentValue == DEFAULT_FAN_CURVE;

            if (!isDefaultCurve && !string.IsNullOrEmpty(_lastSentValue))
            {
                Logger.Info($"Fan curve device writes enabled, pushing non-default curve to widget: {_lastSentValue}");
                SetValue(_lastSentValue);
            }
            else
            {
                Logger.Info($"Fan curve device writes enabled, NOT pushing default curve to widget (device returned defaults)");
            }

            // Now enable device writes for future changes
            _initialized = true;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionFanCurveData changed to {Value}, initialized={_initialized}, lastSent={_lastSentValue}");

            // Only write to device if:
            // 1. Initialized (not during startup sync)
            // 2. Value is different from last sent (avoid redundant WMI calls)
            if (_initialized && Value != _lastSentValue)
            {
                _lastSentValue = Value;
                Manager?.SetFanCurveFromString(Value);
            }
        }
    }

    // CPU temperature property - read-only, updated by LegionManager periodically
    internal class LegionCPUCurrentTempProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionCPUCurrentTempProperty(int initialValue, LegionManager inManager)
            : base(initialValue, null, Function.LegionCPUCurrentTemp, inManager)
        {
        }

        /// <summary>
        /// Called by LegionManager to update the temperature and sync to widget
        /// </summary>
        public void UpdateTemp(int tempC)
        {
            if (tempC != Value)
            {
                SetValue(tempC);
            }
        }
    }

    // Fan control sensor temperature (0x01 sensor) - what EC uses for fan curve lookup
    // This is typically 10-17°C lower than CPU temperature
    internal class LegionFanSensorTempProperty : HelperProperty<int, LegionManager>
    {
        public LegionFanSensorTempProperty(int initialValue, LegionManager inManager)
            : base(initialValue, null, Function.LegionFanSensorTemp, inManager)
        {
        }

        /// <summary>
        /// Called by LegionManager to update the fan sensor temp and sync to widget
        /// </summary>
        public void UpdateTemp(int tempC)
        {
            if (tempC != Value)
            {
                SetValue(tempC);
            }
        }
    }

    // CPU fan RPM property - read-only, updated by LegionManager periodically
    internal class LegionCPUFanRPMProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionCPUFanRPMProperty(int initialValue, LegionManager inManager)
            : base(initialValue, null, Function.LegionCPUFanRPM, inManager)
        {
        }

        /// <summary>
        /// Called by LegionManager to update the RPM and sync to widget
        /// </summary>
        public void UpdateRPM(int rpm)
        {
            if (rpm != Value)
            {
                SetValue(rpm);
            }
        }
    }

    /// <summary>
    /// Widget sets this property to indicate when the fan curve graph is visible.
    /// The helper uses this to know when to push CPU temp and fan RPM updates.
    /// </summary>
    internal class LegionFanCurveVisibleProperty : HelperProperty<bool, LegionManager>
    {
        public LegionFanCurveVisibleProperty(bool initialValue, LegionManager inManager)
            : base(initialValue, null, Function.LegionFanCurveVisible, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager?.SetFanCurveVisible(Value);
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

    // Button Y1 remap (JSON ButtonMapping: Type, GamepadAction, KeyboardKeys[], MouseButton)
    internal class LegionButtonY1Property : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonY1Property(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionButtonY1, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            // Skip applying default/empty mappings to prevent clearing existing button bindings
            if (ButtonMappingParser.IsDefaultMapping(Value))
            {
                Logger.Info($"LegionButtonY1 skipping default mapping: {Value}");
                return;
            }
            Logger.Info($"LegionButtonY1 applying mapping: {Value}");
            var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(Value);
            Manager?.SetButtonMappingAdvanced(0, type, ButtonMappingParser.GetMappingValues(type, gamepadAction, keyboardKeys, mouseButton));
        }
    }

    // Button Y2 remap (JSON ButtonMapping: Type, GamepadAction, KeyboardKeys[], MouseButton)
    internal class LegionButtonY2Property : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonY2Property(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionButtonY2, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (ButtonMappingParser.IsDefaultMapping(Value))
            {
                Logger.Info($"LegionButtonY2 skipping default mapping: {Value}");
                return;
            }
            Logger.Info($"LegionButtonY2 applying mapping: {Value}");
            var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(Value);
            Manager?.SetButtonMappingAdvanced(1, type, ButtonMappingParser.GetMappingValues(type, gamepadAction, keyboardKeys, mouseButton));
        }
    }

    // Button Y3 remap (JSON ButtonMapping: Type, GamepadAction, KeyboardKeys[], MouseButton)
    internal class LegionButtonY3Property : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonY3Property(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionButtonY3, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (ButtonMappingParser.IsDefaultMapping(Value))
            {
                Logger.Info($"LegionButtonY3 skipping default mapping: {Value}");
                return;
            }
            Logger.Info($"LegionButtonY3 applying mapping: {Value}");
            var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(Value);
            Manager?.SetButtonMappingAdvanced(2, type, ButtonMappingParser.GetMappingValues(type, gamepadAction, keyboardKeys, mouseButton));
        }
    }

    // Button M1 remap (JSON ButtonMapping: Type, GamepadAction, KeyboardKeys[], MouseButton)
    internal class LegionButtonM1Property : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonM1Property(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionButtonM1, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (ButtonMappingParser.IsDefaultMapping(Value))
            {
                Logger.Info($"LegionButtonM1 skipping default mapping: {Value}");
                return;
            }
            Logger.Info($"LegionButtonM1 applying mapping: {Value}");
            var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(Value);
            Manager?.SetButtonMappingAdvanced(3, type, ButtonMappingParser.GetMappingValues(type, gamepadAction, keyboardKeys, mouseButton));
        }
    }

    // Button M2 remap (JSON ButtonMapping: Type, GamepadAction, KeyboardKeys[], MouseButton)
    internal class LegionButtonM2Property : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonM2Property(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionButtonM2, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (ButtonMappingParser.IsDefaultMapping(Value))
            {
                Logger.Info($"LegionButtonM2 skipping default mapping: {Value}");
                return;
            }
            Logger.Info($"LegionButtonM2 applying mapping: {Value}");
            var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(Value);
            Manager?.SetButtonMappingAdvanced(4, type, ButtonMappingParser.GetMappingValues(type, gamepadAction, keyboardKeys, mouseButton));
        }
    }

    // Button M3 remap (JSON ButtonMapping: Type, GamepadAction, KeyboardKeys[], MouseButton)
    internal class LegionButtonM3Property : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonM3Property(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionButtonM3, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (ButtonMappingParser.IsDefaultMapping(Value))
            {
                Logger.Info($"LegionButtonM3 skipping default mapping: {Value}");
                return;
            }
            Logger.Info($"LegionButtonM3 applying mapping: {Value}");
            var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(Value);
            Manager?.SetButtonMappingAdvanced(5, type, ButtonMappingParser.GetMappingValues(type, gamepadAction, keyboardKeys, mouseButton));
        }
    }

    // Button Desktop remap (JSON ButtonMapping: Type, GamepadAction, KeyboardKeys[], MouseButton)
    // Default: Win+G (Game Bar)
    internal class LegionButtonDesktopProperty : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonDesktopProperty(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionButtonDesktop, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (ButtonMappingParser.IsDefaultMapping(Value))
            {
                Logger.Info($"LegionButtonDesktop skipping default mapping: {Value}");
                return;
            }
            Logger.Info($"LegionButtonDesktop applying mapping: {Value}");
            var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(Value);
            Manager?.SetLegionButtonMapping(GamepadButton.DesktopButton, type, ButtonMappingParser.GetMappingValues(type, gamepadAction, keyboardKeys, mouseButton));
        }
    }

    // Button Page remap (JSON ButtonMapping: Type, GamepadAction, KeyboardKeys[], MouseButton)
    // Default: Win+Tab (Task View)
    internal class LegionButtonPageProperty : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionButtonPageProperty(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionButtonPage, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (ButtonMappingParser.IsDefaultMapping(Value))
            {
                Logger.Info($"LegionButtonPage skipping default mapping: {Value}");
                return;
            }
            Logger.Info($"LegionButtonPage applying mapping: {Value}");
            var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(Value);
            Manager?.SetLegionButtonMapping(GamepadButton.PageButton, type, ButtonMappingParser.GetMappingValues(type, gamepadAction, keyboardKeys, mouseButton));
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

    // Gyro Deadzone (1-100)
    internal class LegionGyroDeadzoneProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGyroDeadzoneProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionGyroDeadzone, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGyroDeadzone changed to {Value}");
            Manager?.SetGyroDeadzone(Value);
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

    // Trigger Travel - Left Start (0-100%)
    internal class LegionLeftTriggerStartProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionLeftTriggerStartProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionLeftTriggerStart, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionLeftTriggerStart changed to {Value}");
            // Send HID command with current Start and End values
            Manager?.SetLeftTriggerTravel(Value, Manager.LegionLeftTriggerEnd.Value);
        }
    }

    // Trigger Travel - Left End (0-100%)
    internal class LegionLeftTriggerEndProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionLeftTriggerEndProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionLeftTriggerEnd, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionLeftTriggerEnd changed to {Value}");
            // Send the HID command when end value changes (start should already be set)
            Manager?.SetLeftTriggerTravel(Manager.LegionLeftTriggerStart.Value, Value);
        }
    }

    // Trigger Travel - Right Start (0-100%)
    internal class LegionRightTriggerStartProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionRightTriggerStartProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionRightTriggerStart, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionRightTriggerStart changed to {Value}");
            // Send HID command with current Start and End values
            Manager?.SetRightTriggerTravel(Value, Manager.LegionRightTriggerEnd.Value);
        }
    }

    // Trigger Travel - Right End (0-100%)
    internal class LegionRightTriggerEndProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionRightTriggerEndProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionRightTriggerEnd, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionRightTriggerEnd changed to {Value}");
            // Send the HID command when end value changes (start should already be set)
            Manager?.SetRightTriggerTravel(Manager.LegionRightTriggerStart.Value, Value);
        }
    }

    // Hair Triggers toggle (sets both triggers to 0%/1% for instant response)
    internal class LegionHairTriggersProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionHairTriggersProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionHairTriggers, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionHairTriggers changed to {Value}");
            if (Value)
            {
                // Hair triggers: start at 0%, full at 1% (instant response)
                Manager?.SetLeftTriggerTravel(0, 1);
                Manager?.SetRightTriggerTravel(0, 1);
            }
            // When turned off, the individual slider values will be re-applied by the widget
        }
    }

    // Touchpad Vibration level (1=Off, 2=Low, 3=Medium, 4=High - GLOBAL setting)
    internal class LegionTouchpadVibrationProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionTouchpadVibrationProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionTouchpadVibration, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            string levelName = Value switch
            {
                1 => "Off",
                2 => "Low",
                3 => "Medium",
                4 => "High",
                _ => "Unknown"
            };
            Logger.Info($"LegionTouchpadVibration changed to {levelName} ({Value})");
            Manager?.SetTouchpadVibration(Value);
        }
    }

    // Joystick as Mouse Mode (0=Disabled, 1=Left Stick, 2=Right Stick)
    internal class LegionJoystickAsMouseModeProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionJoystickAsMouseModeProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionJoystickAsMouseMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionJoystickAsMouseMode changed to {Value}");
            Manager?.SetJoystickAsMouseMode(Value);
        }
    }

    // Joystick Mouse Sensitivity (10-100)
    internal class LegionJoystickMouseSensProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionJoystickMouseSensProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionJoystickMouseSens, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionJoystickMouseSens changed to {Value}");
            Manager?.SetJoystickMouseSens(Value);
        }
    }

    // Gamepad Button Mapping (JSON string for all 24 buttons)
    internal class LegionGamepadMappingProperty : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionGamepadMappingProperty(string initialValue, LegionManager inManager) : base(initialValue ?? "", null, Function.LegionGamepadButtonMapping, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionGamepadMapping changed to {Value}");
            Manager?.ApplyGamepadButtonMappings(Value);
        }
    }

    // Desktop Controls Preset (state tracking - actual application via JoystickAsMouseMode + GamepadButtonMapping)
    internal class LegionDesktopControlsProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public LegionDesktopControlsProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.LegionDesktopControls, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"LegionDesktopControls changed to {Value}");
            // Note: Actual application happens through JoystickAsMouseMode and GamepadButtonMapping
            // properties, so this property mainly serves as state tracking for the UI toggle
        }
    }

    // Controller Battery Left (read-only, 1-100 or -1 if unavailable)
    internal class ControllerBatteryLeftProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerBatteryLeftProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.ControllerBatteryLeft, inManager)
        {
        }

        public void SetValueAndSync(int value)
        {
            SetValue((object)value);
            SyncToRemote();
        }
    }

    // Controller Battery Right (read-only, 1-100 or -1 if unavailable)
    internal class ControllerBatteryRightProperty : HelperProperty<int, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerBatteryRightProperty(int initialValue, LegionManager inManager) : base(initialValue, null, Function.ControllerBatteryRight, inManager)
        {
        }

        public void SetValueAndSync(int value)
        {
            SetValue((object)value);
            SyncToRemote();
        }
    }

    // Controller Charging Left (read-only, true if charging)
    internal class ControllerChargingLeftProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerChargingLeftProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.ControllerChargingLeft, inManager)
        {
        }

        public void SetValueAndSync(bool value)
        {
            SetValue((object)value);
            SyncToRemote();
        }
    }

    // Controller Charging Right (read-only, true if charging)
    internal class ControllerChargingRightProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerChargingRightProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.ControllerChargingRight, inManager)
        {
        }

        public void SetValueAndSync(bool value)
        {
            SetValue((object)value);
            SyncToRemote();
        }
    }

    // Controller Connected Left (read-only, true if controller is connected/attached)
    internal class ControllerConnectedLeftProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerConnectedLeftProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.ControllerConnectedLeft, inManager)
        {
        }

        public void SetValueAndSync(bool value)
        {
            SetValue((object)value);
            SyncToRemote();
        }
    }

    // Controller Connected Right (read-only, true if controller is connected/attached)
    internal class ControllerConnectedRightProperty : HelperProperty<bool, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerConnectedRightProperty(bool initialValue, LegionManager inManager) : base(initialValue, null, Function.ControllerConnectedRight, inManager)
        {
        }

        public void SetValueAndSync(bool value)
        {
            SetValue((object)value);
            SyncToRemote();
        }
    }

    // Controller VID:PID (read-only, e.g., "17EF:6182")
    internal class ControllerVidPidProperty : HelperProperty<string, LegionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerVidPidProperty(string initialValue, LegionManager inManager) : base(initialValue, null, Function.ControllerVidPid, inManager)
        {
        }

        public void SetValueAndSync(string value)
        {
            SetValue((object)value);
            SyncToRemote();
        }
    }
}
