using NLog;
using System;
using XboxGamingBarHelper.Devices.EC;

namespace XboxGamingBarHelper.Devices.Libraries.GPD
{
    /// <summary>
    /// Fan mode for GPD devices.
    /// </summary>
    internal enum GPDFanMode
    {
        /// <summary>Auto mode - EC controls fan based on temperature.</summary>
        Auto = 0,
        /// <summary>Manual mode - user controls fan speed.</summary>
        Manual = 1
    }

    /// <summary>
    /// GPD Win 5 fan controller using EC (Embedded Controller) communication.
    /// Addresses derived from Handheld Companion research.
    /// </summary>
    internal class GPDFanController : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // GPD Win 5 EC Register Addresses
        private const ushort FAN1_DUTY_ADDR = 0x047A;     // Fan 1 PWM duty (0-244)
        private const ushort FAN2_DUTY_ADDR = 0x047B;     // Fan 2 PWM duty (0-244)
        private const ushort FAN_RPM_HIGH_ADDR = 0x0478;  // Fan RPM high byte
        private const ushort FAN_RPM_LOW_ADDR = 0x0479;   // Fan RPM low byte

        // PWM range
        private const int PWM_MIN = 0;                    // 0 = auto mode
        private const int PWM_MAX = 244;                  // Maximum manual PWM value
        private const int PWM_MANUAL_MIN = 75;            // Minimum safe manual PWM (~30%)

        // Percentage range for manual mode
        private const int PERCENT_MIN = 30;               // Minimum manual percentage
        private const int PERCENT_MAX = 100;              // Maximum percentage

        private readonly EcController _ec;
        private GPDFanMode _currentMode = GPDFanMode.Auto;
        private int _currentSpeedPercent = 0;
        private bool _disposed = false;
        private bool _isReady = false;

        public GPDFanController()
        {
            Logger.Info("[GPDFan] Initializing GPD Fan Controller...");

            _ec = new EcController();
            if (_ec.Initialize())
            {
                _isReady = true;
                Logger.Info("[GPDFan] EC controller initialized successfully");
                Logger.Info($"[GPDFan] Chip ID: 0x{_ec.ChipId:X4}");

                // Read initial fan speed
                try
                {
                    int rpm = GetFanRPM();
                    Logger.Info($"[GPDFan] Current fan RPM: {rpm}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[GPDFan] Could not read initial fan RPM: {ex.Message}");
                }
            }
            else
            {
                Logger.Error("[GPDFan] Failed to initialize EC controller - fan control unavailable");
            }
        }

        /// <summary>
        /// Gets whether the fan controller is ready to use.
        /// </summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Gets the current fan mode.
        /// </summary>
        public GPDFanMode CurrentMode => _currentMode;

        /// <summary>
        /// Gets the current fan speed percentage (0 = auto, 30-100 = manual).
        /// </summary>
        public int CurrentSpeedPercent => _currentSpeedPercent;

        /// <summary>
        /// Sets the fan mode (Auto or Manual).
        /// </summary>
        /// <param name="mode">Fan mode.</param>
        /// <returns>True if successful.</returns>
        public bool SetFanMode(GPDFanMode mode)
        {
            if (!_isReady)
            {
                Logger.Warn("[GPDFan] Cannot set fan mode - controller not ready");
                return false;
            }

            try
            {
                if (mode == GPDFanMode.Auto)
                {
                    // Set PWM to 0 to enable auto mode
                    _ec.WriteByte(FAN1_DUTY_ADDR, 0);
                    _ec.WriteByte(FAN2_DUTY_ADDR, 0);
                    _currentMode = GPDFanMode.Auto;
                    _currentSpeedPercent = 0;
                    Logger.Info("[GPDFan] Fan mode set to Auto");
                    return true;
                }
                else
                {
                    // Manual mode - set to minimum safe speed initially
                    _currentMode = GPDFanMode.Manual;
                    return SetFanSpeed(PERCENT_MIN);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDFan] Error setting fan mode: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets the fan speed as a percentage (0 = auto, 30-100 = manual speed).
        /// </summary>
        /// <param name="percent">Fan speed percentage. 0 enables auto mode.</param>
        /// <returns>True if successful.</returns>
        public bool SetFanSpeed(int percent)
        {
            if (!_isReady)
            {
                Logger.Warn("[GPDFan] Cannot set fan speed - controller not ready");
                return false;
            }

            try
            {
                // Handle auto mode
                if (percent <= 0)
                {
                    return SetFanMode(GPDFanMode.Auto);
                }

                // Clamp to safe range for manual mode
                percent = Math.Max(PERCENT_MIN, Math.Min(PERCENT_MAX, percent));

                // Convert percentage to PWM value
                // Map 30-100% to PWM_MANUAL_MIN-PWM_MAX
                int pwm = PercentToPwm(percent);

                Logger.Debug($"[GPDFan] Setting fan speed: {percent}% (PWM: {pwm})");

                // Write to both fan registers
                _ec.WriteByte(FAN1_DUTY_ADDR, (byte)pwm);
                _ec.WriteByte(FAN2_DUTY_ADDR, (byte)pwm);

                _currentMode = GPDFanMode.Manual;
                _currentSpeedPercent = percent;

                Logger.Info($"[GPDFan] Fan speed set to {percent}%");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDFan] Error setting fan speed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current fan RPM.
        /// </summary>
        /// <returns>Fan speed in RPM, or 0 if unavailable.</returns>
        public int GetFanRPM()
        {
            if (!_isReady)
            {
                return 0;
            }

            try
            {
                // Read RPM as 16-bit value (high byte first)
                ushort rpmRaw = _ec.ReadWordHighFirst(FAN_RPM_HIGH_ADDR);

                // Some EC implementations return raw tachometer counts
                // Typical formula: RPM = 1350000 / raw_value
                // But GPD may return direct RPM - try both
                int rpm;
                if (rpmRaw > 10000)
                {
                    // Likely tachometer count - convert to RPM
                    rpm = rpmRaw > 0 ? 1350000 / rpmRaw : 0;
                }
                else
                {
                    // Likely direct RPM value
                    rpm = rpmRaw;
                }

                Logger.Debug($"[GPDFan] Read RPM: raw=0x{rpmRaw:X4}, calculated={rpm}");
                return rpm;
            }
            catch (Exception ex)
            {
                Logger.Debug($"[GPDFan] Error reading fan RPM: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Converts percentage (30-100) to PWM value (PWM_MANUAL_MIN-PWM_MAX).
        /// </summary>
        private int PercentToPwm(int percent)
        {
            // Linear interpolation from PERCENT_MIN-PERCENT_MAX to PWM_MANUAL_MIN-PWM_MAX
            double ratio = (double)(percent - PERCENT_MIN) / (PERCENT_MAX - PERCENT_MIN);
            int pwm = (int)(PWM_MANUAL_MIN + ratio * (PWM_MAX - PWM_MANUAL_MIN));
            return Math.Max(PWM_MANUAL_MIN, Math.Min(PWM_MAX, pwm));
        }

        /// <summary>
        /// Converts PWM value to percentage.
        /// </summary>
        private int PwmToPercent(int pwm)
        {
            if (pwm <= 0)
                return 0; // Auto mode

            if (pwm < PWM_MANUAL_MIN)
                return PERCENT_MIN;

            double ratio = (double)(pwm - PWM_MANUAL_MIN) / (PWM_MAX - PWM_MANUAL_MIN);
            int percent = (int)(PERCENT_MIN + ratio * (PERCENT_MAX - PERCENT_MIN));
            return Math.Max(PERCENT_MIN, Math.Min(PERCENT_MAX, percent));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                // Return to auto mode on dispose for safety
                if (_isReady && _currentMode == GPDFanMode.Manual)
                {
                    try
                    {
                        SetFanMode(GPDFanMode.Auto);
                        Logger.Info("[GPDFan] Restored auto fan mode on dispose");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[GPDFan] Could not restore auto mode on dispose: {ex.Message}");
                    }
                }

                _ec?.Dispose();
                _isReady = false;
                Logger.Debug("[GPDFan] GPDFanController disposed");
            }
        }
    }
}
