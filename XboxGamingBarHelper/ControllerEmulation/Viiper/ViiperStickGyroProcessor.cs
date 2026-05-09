using System;
using NLog;
using Shared.Data;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Labs;
using SharedDeviceType = Shared.Enums.DeviceType;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// Synthesizes a right-stick override from gyro motion for VIIPER target types
    /// that have no native motion field in their wire format (xbox360, xboxelite2,
    /// switchpro family, etc.). Ported from <see cref="ControllerEmulationManager"/>'s
    /// legacy stick-gyro pipeline so users on the VIIPER backend get the same feel
    /// they had on legacy ViGEm Mode 1 ("Xbox / Stick").
    ///
    /// Reads <c>ControllerEmulationStick*</c> + <c>ControllerEmulationGyroActivation*</c>
    /// keys directly from <see cref="LocalSettingsHelper"/> so settings carry over
    /// between backends without a separate UI surface. Refreshes them lazily once
    /// per second on the poll thread.
    ///
    /// Gyro source is selected via <c>ControllerEmulationGyroSource</c> — same setting
    /// legacy CE uses — backed by the existing <see cref="LegionControllerGyroSourceAdapter"/>
    /// / <see cref="LegionControllerMixedGyroSourceAdapter"/> classes. All variants read
    /// through <see cref="LegionButtonMonitor"/>'s cached HID handle, so samples keep
    /// flowing regardless of HidHide visibility (see issue #79 round-3 findings).
    /// </summary>
    internal sealed class ViiperStickGyroProcessor
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ---- Settings cache (refreshed from LocalSettingsHelper periodically) ----
        private bool enabled;            // ViiperStickGyroEnabled — master kill switch for the feature
        private int gyroSource;          // 0=Mixed (default), 1=Right, 2=Left, 3=Mixed; matches legacy CE convention
        private int stickSelect;         // 0=Left stick, 1=Right stick (Send-to-joystick)
        private int gyroActivationMode;
        private int gyroActivationButton;
        private bool stickInvertX;
        private bool stickInvertY;
        private int stickMinGyroSpeed;
        private int stickMaxGyroSpeed;
        private int stickMinOutput;
        private int stickMaxOutput;
        private int stickPowerCurve;
        private int stickSensitivityV2;
        private int stickDeadzone;
        private int stickPrecisionSpeed;
        private int stickOutputMix;
        private int stickOrientationV2;
        private int stickConversion;
        private long lastSettingsRefreshTicks;
        // 200 ms refresh — fast enough that slider drags feel instant, slow enough
        // that the LocalSettings dictionary lookups don't show up in poll-loop
        // profiles. Was 1 s; that produced a noticeable lag when tuning sensitivity
        // / deadzone live during gameplay.
        private const long SettingsRefreshIntervalTicks = TimeSpan.TicksPerSecond / 5;

        // ---- Active gyro source adapter (rebuilt when gyroSource setting changes) ----
        // Reuses the legacy CE adapter classes so source switching matches Mode 1
        // behavior identically; no second implementation to keep in sync.
        private IGyroSourceAdapter activeAdapter;
        // Composite key for "is the active adapter still the right one?" — we have to
        // rebuild not only on gyroSource change but also when the detected device type
        // changes (rare, but happens during dock/undock or when the user reconfigures
        // which handheld profile is active). Pack both into a single int: low byte =
        // gyroSource, high byte = (int)deviceType.
        private int activeAdapterKey = -1;
        // Filter-state-relevant settings tracked separately so a change invalidates
        // the One-Euro filter without touching activation toggle state.
        private int prevConversion = -1;
        private int prevOrientation = -1;

        // ---- Diagnostic counters (5s rolling window). Periodic Info-level emit so
        // "no gyro on Xbox 360 VIIPER" reports can be triaged from a single helper log. ----
        private int statsCalls;
        private int statsGateOff;
        private int statsNoSample;
        private int statsMerged;
        private long statsLastEmitTicks;
        private const long StatsEmitIntervalTicks = TimeSpan.TicksPerSecond * 5;

        // Per-pipeline-stage diagnostic — fires once per second when the gate is
        // open AND any axis is producing output, so users testing "horizontal
        // works, vertical doesn't" can see at which stage the value dies.
        // Throttled to avoid log spam during normal gameplay.
        private long pipelineDiagLastTicks;
        private const long PipelineDiagIntervalTicks = TimeSpan.TicksPerSecond;

        // ---- One-Euro filter state ----
        private float stickFilteredHorizontal;
        private float stickFilteredVertical;
        private float stickFilteredDerivativeHorizontal;
        private float stickFilteredDerivativeVertical;
        private bool stickFilterInitialized;
        private long stickLastSampleTicksUtc;

        // ---- Activation toggle state ----
        private bool gyroToggleActive;
        private bool lastGyroActivationButtonPressed;

        // ---- Bias correction (issue #79 round 4) ----
        // vvalente30: "gyro is moving the device by itself uncontrollable when
        // always on." Stats showed merged=259-293/323 calls per 5s — feature was
        // alive, but a typical 5-10°/s residual IMU bias survives the 2°/s
        // deadzone and produces a small persistent stick deflection that games
        // integrate as a slow camera spin. Estimator updates only during
        // stationary periods so deliberate slow pans aren't learned as bias.
        private readonly GyroBiasEstimator biasEstimator = new GyroBiasEstimator();

        // ---- One-Euro filter constants (mirror legacy CE) ----
        private const float OneEuroMinCutoff = 1.2f;
        private const float OneEuroBeta = 0.25f;
        private const float OneEuroDerivativeCutoff = 1.5f;
        private const float DefaultDeltaSeconds = 1.0f / 250.0f;
        private const float MinDeltaSeconds = 0.002f;
        private const float MaxDeltaSeconds = 0.05f;

        // ---- Standard XInput button bits (match ViiperXInput in XInputNative.cs) ----
        private const ushort BTN_DPAD_UP = 0x0001;
        private const ushort BTN_DPAD_DOWN = 0x0002;
        private const ushort BTN_DPAD_LEFT = 0x0004;
        private const ushort BTN_DPAD_RIGHT = 0x0008;
        private const ushort BTN_START = 0x0010;
        private const ushort BTN_BACK = 0x0020;
        private const ushort BTN_LEFT_THUMB = 0x0040;
        private const ushort BTN_RIGHT_THUMB = 0x0080;
        private const ushort BTN_LB = 0x0100;
        private const ushort BTN_RB = 0x0200;
        private const ushort BTN_A = 0x1000;
        private const ushort BTN_B = 0x2000;
        private const ushort BTN_X = 0x4000;
        private const ushort BTN_Y = 0x8000;
        private const byte XINPUT_TRIGGER_THRESHOLD = 30;

        // ---- Legion HID raw aux button bits (match LegionButtonMonitor parser output) ----
        private const ushort LEGION_AUX_L1 = 0x0004;   // Y1
        private const ushort LEGION_AUX_L2 = 0x0008;   // Y2
        private const ushort LEGION_AUX_R1 = 0x0010;   // Y3
        private const ushort LEGION_AUX_RM1 = 0x0020;  // M1
        private const ushort LEGION_AUX_R2 = 0x0040;   // M3
        private const ushort LEGION_AUX_R3 = 0x0080;   // M2

        public ViiperStickGyroProcessor()
        {
            RefreshSettings();
            lastSettingsRefreshTicks = DateTime.UtcNow.Ticks;
            statsLastEmitTicks = lastSettingsRefreshTicks;
        }

        /// <summary>
        /// True when "Send to joystick" is set to Left. Forwarder reads this AFTER
        /// <see cref="TryComputeStickOverride"/> returns true to know whether to merge
        /// the stick override into ThumbLX/LY or ThumbRX/RY.
        /// </summary>
        public bool RoutesToLeftStick => stickSelect == 0;

        /// <summary>
        /// Targets without a native motion field in their wire format. Stick-gyro
        /// makes sense for these because games can't read gyro any other way.
        /// DS4 / DualSense Edge are deliberately excluded — their wire format
        /// already carries IMU bytes (see ViiperInputForwarder.BuildDualShock4Input
        /// / BuildDualSenseEdgeInput) so synthesizing a stick override on top of
        /// real gyro would double-feed motion to games.
        /// </summary>
        public static bool IsApplicableForTarget(string targetType)
        {
            if (string.IsNullOrEmpty(targetType)) return false;
            switch (targetType)
            {
                case "xbox360":
                case "xboxelite2":
                case "xbox-one":
                case "xbox-elite":
                case "steamdeck-generic":
                case "steam-generic":
                case "steam-controller":
                case "switchpro":
                case "joycon-left":
                case "joycon-right":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Clear filter + activation state. Call on backend swap / forwarder restart.</summary>
        public void Reset()
        {
            stickFilterInitialized = false;
            stickLastSampleTicksUtc = 0;
            stickFilteredHorizontal = 0f;
            stickFilteredVertical = 0f;
            stickFilteredDerivativeHorizontal = 0f;
            stickFilteredDerivativeVertical = 0f;
            gyroToggleActive = false;
            lastGyroActivationButtonPressed = false;
            // Bias state is held across forwarder Stop/Start because the IMU's
            // calibration doesn't change when our process pauses. Only forced
            // recalibration (the future "Calibrate now" button or backend
            // swap on dock/undock) should drop it.
        }

        /// <summary>
        /// Drop the bias estimate so the next stationary period re-snaps. Hooked
        /// up to the future "Calibrate now" UX. Public so corando can fire it
        /// from a settings change handler if we want a "Reset gyro calibration"
        /// command.
        /// </summary>
        public void RecalibrateBias() => biasEstimator.Reset();

        /// <summary>
        /// Tear down the active gyro source adapter (call from forwarder Stop). Safe
        /// to call repeatedly; idempotent.
        /// </summary>
        public void Shutdown()
        {
            try { activeAdapter?.Stop(); }
            catch (Exception ex) { Logger.Debug($"VIIPER stick-gyro adapter Stop threw: {ex.Message}"); }
            try { activeAdapter?.Dispose(); }
            catch (Exception ex) { Logger.Debug($"VIIPER stick-gyro adapter Dispose threw: {ex.Message}"); }
            activeAdapter = null;
            activeAdapterKey = -1;
        }

        /// <summary>
        /// Returns true with a non-zero stick X/Y when the feature is enabled,
        /// activation is engaged, and a fresh gyro sample is available. Caller merges
        /// (stickX, stickY) into ThumbLX/LY or ThumbRX/RY based on
        /// <see cref="RoutesToLeftStick"/>.
        /// </summary>
        public bool TryComputeStickOverride(
            ushort xinputButtons,
            byte leftTrigger,
            byte rightTrigger,
            ushort legionAuxButtonsRaw,
            out short stickX,
            out short stickY)
        {
            stickX = 0;
            stickY = 0;

            long nowTicksUtc = DateTime.UtcNow.Ticks;
            if (nowTicksUtc - lastSettingsRefreshTicks >= SettingsRefreshIntervalTicks)
            {
                RefreshSettings();
                EnsureGyroAdapter();
                lastSettingsRefreshTicks = nowTicksUtc;
            }

            statsCalls++;
            MaybeEmitStats(nowTicksUtc);

            if (!enabled)
            {
                statsGateOff++;
                return false;
            }

            // Always pull a sample (even when activation gate is closed) so the
            // bias estimator keeps learning continuously. Otherwise users who
            // play with Hold/Toggle activation get bias=uncal until the moment
            // they engage the gate, then suffer the full pre-correction drift
            // for the first ~500ms while the estimator catches up. Cheap path:
            // adapter samples are already cached from the polling backend.
            bool gateOpen = IsActivationEnabled(xinputButtons, leftTrigger, rightTrigger, legionAuxButtonsRaw);

            // Sample arrives via the gyro source adapter selected from the
            // ControllerEmulationGyroSource setting (see EnsureGyroAdapter). Same
            // adapter set legacy CE uses — Right/Left/Mixed all backed by
            // LegionButtonMonitor's cached HID handle.
            if (activeAdapter == null || !activeAdapter.TryGetLatestSample(out GyroSample sample))
            {
                statsNoSample++;
                return false;
            }

            // Feed the bias estimator on every sample, even when the gate is
            // closed. Without this, calibration would only happen during active
            // gyro engagement, leaving the user with a fully uncalibrated
            // estimator at the moment they first press the activation button.
            // Discard the corrected sample when the gate is closed — the work
            // is just to keep the estimator's state current.
            if (!gateOpen)
            {
                _ = biasEstimator.Correct(sample);
                statsGateOff++;
                return false;
            }

            ApplyStickFromGyro(sample, out stickX, out stickY);
            bool produced = stickX != 0 || stickY != 0;
            if (produced) statsMerged++;
            return produced;
        }

        // ----------------------------------------------------------------------
        // Gyro source adapter selection
        // ----------------------------------------------------------------------

        /// <summary>
        /// Build the gyro source adapter matching the current <c>gyroSource</c>
        /// setting AND the detected handheld device type. Idempotent — short-circuits
        /// when the active adapter already matches both. Disposes and rebuilds when
        /// either changes.
        ///
        /// Routing mirrors legacy CE <c>BuildGyroSourceAdapter</c>:
        /// • Legion Go / Go 2:        Legion controller IMU adapters (Right/Left/Mixed)
        ///                             driven by the user's gyroSource setting (1/2/3),
        ///                             defaulting to Mixed.
        /// • Legion Go S / GPD Win 5: Windows Sensor stack (handheld IMU exposed by
        ///                             the device's own Windows driver). The
        ///                             gyroSource setting is ignored — there's only
        ///                             one source available on these devices.
        /// • Generic / unknown:       Windows Sensor fallback as well, so non-Legion
        ///                             handhelds (ROG Ally, Steam Deck, MSI Claw, etc.)
        ///                             still get a working gyro→stick path. Same
        ///                             behavior the user requested: "non-Legion
        ///                             devices can be pulled from the Handheld (device
        ///                             IMU) since it's provided by Windows."
        /// </summary>
        private void EnsureGyroAdapter()
        {
            SharedDeviceType deviceType = TryDetectDeviceType();
            int targetKey = ((int)deviceType << 8) | (gyroSource & 0xFF);
            if (activeAdapter != null && activeAdapterKey == targetKey) return;

            try { activeAdapter?.Stop(); }
            catch (Exception ex) { Logger.Debug($"VIIPER stick-gyro previous adapter Stop threw: {ex.Message}"); }
            try { activeAdapter?.Dispose(); }
            catch (Exception ex) { Logger.Debug($"VIIPER stick-gyro previous adapter Dispose threw: {ex.Message}"); }
            activeAdapter = null;

            switch (deviceType)
            {
                case SharedDeviceType.LegionGo:
                case SharedDeviceType.LegionGo2:
                    switch (gyroSource)
                    {
                        case 1: activeAdapter = new LegionControllerGyroSourceAdapter(false); break;  // Right
                        case 2: activeAdapter = new LegionControllerGyroSourceAdapter(true); break;   // Left
                        default: activeAdapter = new LegionControllerMixedGyroSourceAdapter(); break; // Mixed (incl. default 0)
                    }
                    break;

                case SharedDeviceType.LegionGoS:
                    activeAdapter = new WindowsSensorGyroSourceAdapter("Legion Go S Internal Gyro");
                    break;

                case SharedDeviceType.GPDWin5:
                    activeAdapter = new WindowsSensorGyroSourceAdapter("GPD Internal Gyro");
                    break;

                default:
                    // ROG Ally, Steam Deck, MSI Claw, generic — fall back to the OS
                    // Windows Sensor stack with a generic name. Same path legacy CE
                    // uses for unknown devices when gyroSource defaults to Internal.
                    activeAdapter = new WindowsSensorGyroSourceAdapter("Handheld Internal Gyro");
                    break;
            }

            try
            {
                if (!activeAdapter.Start())
                {
                    Logger.Warn($"VIIPER stick-gyro adapter '{activeAdapter.Name}' failed to start; gyro→stick will be silent until next refresh");
                    activeAdapter.Dispose();
                    activeAdapter = null;
                    activeAdapterKey = -1;
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"VIIPER stick-gyro adapter Start threw: {ex.Message}");
                activeAdapter = null;
                activeAdapterKey = -1;
                return;
            }

            activeAdapterKey = targetKey;
            Logger.Info($"VIIPER stick-gyro adapter active: {activeAdapter.Name} (device={deviceType}, gyroSource={gyroSource})");
        }

        /// <summary>
        /// Best-effort device-type probe. Cheap (DeviceDetector caches), safe to call
        /// per refresh tick. Returns Generic on detection failure so the Windows
        /// Sensor fallback path still engages.
        /// </summary>
        private static SharedDeviceType TryDetectDeviceType()
        {
            try
            {
                var info = DeviceDetector.DetectDevice();
                return info?.DeviceType ?? SharedDeviceType.Generic;
            }
            catch (Exception ex)
            {
                Logger.Debug($"VIIPER stick-gyro DeviceDetector threw: {ex.Message}");
                return SharedDeviceType.Generic;
            }
        }

        // ----------------------------------------------------------------------
        // 5s diagnostic stats
        // ----------------------------------------------------------------------

        private void MaybeEmitStats(long nowTicksUtc)
        {
            if (nowTicksUtc - statsLastEmitTicks < StatsEmitIntervalTicks) return;
            // Only log when there's traffic — otherwise we'd spam the log when
            // the section is mounted but the user isn't on an applicable target.
            if (statsCalls > 0)
            {
                string biasField = biasEstimator.IsCalibrated
                    ? string.Format("bias=[{0:F1},{1:F1},{2:F1}]",
                        biasEstimator.BiasXDegPerSec,
                        biasEstimator.BiasYDegPerSec,
                        biasEstimator.BiasZDegPerSec)
                    : "bias=uncal";
                Logger.Info(
                    "VIIPER stick-gyro 5s stats: enabled={0} calls={1} gateOff={2} noSample={3} merged={4} src={5} route={6} mode={7} btn={8} {9}",
                    enabled, statsCalls, statsGateOff, statsNoSample, statsMerged,
                    gyroSource, stickSelect == 0 ? "Left" : "Right",
                    gyroActivationMode, gyroActivationButton, biasField);
            }
            statsCalls = 0;
            statsGateOff = 0;
            statsNoSample = 0;
            statsMerged = 0;
            statsLastEmitTicks = nowTicksUtc;
        }

        // ----------------------------------------------------------------------
        // Settings
        // ----------------------------------------------------------------------

        private void RefreshSettings()
        {
            // Master enable for the VIIPER stick-gyro feature. Defaults true so the
            // feature works out of the box on existing installs (it shipped on by
            // default in 0.3.2152). Users on the VIIPER backend can untoggle it
            // from the panel UI to get raw stick passthrough on xbox360 targets.
            enabled = !LocalSettingsHelper.TryGetValue("ViiperStickGyroEnabled", out bool savedEnabled) || savedEnabled;

            // Gyro source selector (shared with legacy CE — same key).
            gyroSource = LocalSettingsHelper.TryGetValue("ControllerEmulationGyroSource", out int savedGyroSource)
                ? Math.Max(0, Math.Min(3, savedGyroSource)) : 0;
            // Send-to-joystick. Default 1 (Right) per vvalente30's recommended settings.
            stickSelect = LocalSettingsHelper.TryGetValue("ControllerEmulationStickSelect", out int savedStickSelect)
                ? (savedStickSelect == 0 ? 0 : 1) : 1;

            gyroActivationMode = LocalSettingsHelper.TryGetValue("ControllerEmulationGyroActivationMode", out int savedMode)
                ? Math.Max(0, Math.Min(2, savedMode)) : 0;
            gyroActivationButton = LocalSettingsHelper.TryGetValue("ControllerEmulationGyroActivationButton", out int savedBtn)
                ? Math.Max(1, Math.Min(22, savedBtn)) : 1;

            stickInvertX = LocalSettingsHelper.TryGetValue("ControllerEmulationStickInvertX", out bool savedInvX) && savedInvX;
            stickInvertY = LocalSettingsHelper.TryGetValue("ControllerEmulationStickInvertY", out bool savedInvY) && savedInvY;

            // Same defaults as legacy CE (Normalize.cs:554-575) so behavior matches
            // when a user rolls back to the legacy backend.
            stickMinGyroSpeed = LocalSettingsHelper.TryGetValue("ControllerEmulationStickMinGyroSpeed", out int v1)
                ? Math.Max(0, Math.Min(100, v1)) : 0;
            stickMaxGyroSpeed = LocalSettingsHelper.TryGetValue("ControllerEmulationStickMaxGyroSpeed", out int v2)
                ? Math.Max(50, Math.Min(720, v2)) : 220;
            stickMinOutput = LocalSettingsHelper.TryGetValue("ControllerEmulationStickMinOutput", out int v3)
                ? Math.Max(0, Math.Min(100, v3)) : 0;
            stickMaxOutput = LocalSettingsHelper.TryGetValue("ControllerEmulationStickMaxOutput", out int v4)
                ? Math.Max(1, Math.Min(100, v4)) : 100;
            stickPowerCurve = LocalSettingsHelper.TryGetValue("ControllerEmulationStickPowerCurve", out int v5)
                ? Math.Max(10, Math.Min(400, v5)) : 100;
            stickSensitivityV2 = LocalSettingsHelper.TryGetValue("ControllerEmulationStickSensitivityV2", out int v6)
                ? Math.Max(1, Math.Min(400, v6)) : 100;
            stickDeadzone = LocalSettingsHelper.TryGetValue("ControllerEmulationStickDeadzone", out int v7)
                ? Math.Max(0, Math.Min(50, v7)) : 2;
            stickPrecisionSpeed = LocalSettingsHelper.TryGetValue("ControllerEmulationStickPrecisionSpeed", out int v8)
                ? Math.Max(0, Math.Min(100, v8)) : 0;
            stickOutputMix = LocalSettingsHelper.TryGetValue("ControllerEmulationStickOutputMix", out int v9)
                ? Math.Max(-100, Math.Min(100, v9)) : 0;
            stickOrientationV2 = LocalSettingsHelper.TryGetValue("ControllerEmulationStickOrientationV2", out int v10)
                ? (v10 == 1 ? 1 : 0) : 0;
            // Default 2 (Yaw + Roll) per vvalente30's recommended settings (issue #79).
            // Kept aligned with ControllerEmulationManager.Normalize.cs:LoadSettings.
            stickConversion = LocalSettingsHelper.TryGetValue("ControllerEmulationStickConversion", out int v11)
                ? Math.Max(0, Math.Min(2, v11)) : 2;

            // Conversion / Orientation reshape the input axes; with the One-Euro filter
            // initialized from previous-axis values, switching mid-stream produces
            // ~50ms of "ghost" stick output as the filter adapts. Drop the filter
            // state on these specific changes so the new mapping starts clean.
            if (prevConversion != -1 && (prevConversion != stickConversion || prevOrientation != stickOrientationV2))
            {
                stickFilterInitialized = false;
                stickFilteredHorizontal = 0f;
                stickFilteredVertical = 0f;
                stickFilteredDerivativeHorizontal = 0f;
                stickFilteredDerivativeVertical = 0f;
            }
            prevConversion = stickConversion;
            prevOrientation = stickOrientationV2;
        }

        // ----------------------------------------------------------------------
        // Activation gate
        // ----------------------------------------------------------------------

        private bool IsActivationEnabled(ushort buttons, byte lt, byte rt, ushort aux)
        {
            switch (gyroActivationMode)
            {
                case 1: // Hold
                    bool holdPressed = IsActivationButtonPressed(buttons, lt, rt, aux);
                    lastGyroActivationButtonPressed = holdPressed;
                    return holdPressed;
                case 2: // Toggle
                    bool togglePressed = IsActivationButtonPressed(buttons, lt, rt, aux);
                    if (togglePressed && !lastGyroActivationButtonPressed)
                    {
                        gyroToggleActive = !gyroToggleActive;
                    }
                    lastGyroActivationButtonPressed = togglePressed;
                    return gyroToggleActive;
                default: // Always
                    lastGyroActivationButtonPressed = IsActivationButtonPressed(buttons, lt, rt, aux);
                    return true;
            }
        }

        private bool IsActivationButtonPressed(ushort buttons, byte lt, byte rt, ushort aux)
        {
            switch (gyroActivationButton)
            {
                case 1: return rt > XINPUT_TRIGGER_THRESHOLD;
                case 2: return lt > XINPUT_TRIGGER_THRESHOLD;
                case 3: return (buttons & BTN_RB) != 0;
                case 4: return (buttons & BTN_LB) != 0;
                case 5: return (buttons & BTN_A) != 0;
                case 6: return (buttons & BTN_B) != 0;
                case 7: return (buttons & BTN_X) != 0;
                case 8: return (buttons & BTN_Y) != 0;
                case 9: return (buttons & BTN_RIGHT_THUMB) != 0;
                case 10: return (buttons & BTN_LEFT_THUMB) != 0;
                case 11: return (buttons & BTN_DPAD_UP) != 0;
                case 12: return (buttons & BTN_DPAD_DOWN) != 0;
                case 13: return (buttons & BTN_DPAD_LEFT) != 0;
                case 14: return (buttons & BTN_DPAD_RIGHT) != 0;
                case 15: return (buttons & BTN_START) != 0;
                case 16: return (buttons & BTN_BACK) != 0;
                // Legion-only aux buttons; only meaningful on LegionHid input source
                // (XInput input source can't see these — aux will be 0).
                case 17: return (aux & LEGION_AUX_R2) != 0;   // M3
                case 18: return (aux & LEGION_AUX_RM1) != 0;  // M1
                case 19: return (aux & LEGION_AUX_R3) != 0;   // M2
                case 20: return (aux & LEGION_AUX_L1) != 0;   // Y1
                case 21: return (aux & LEGION_AUX_L2) != 0;   // Y2
                case 22: return (aux & LEGION_AUX_R1) != 0;   // Y3
                default: return false;
            }
        }

        // ----------------------------------------------------------------------
        // Math (port of ControllerEmulationManager.GyroStick.cs:24-171)
        // ----------------------------------------------------------------------

        private void ApplyStickFromGyro(GyroSample sample, out short outputX, out short outputY)
        {
            // Capture the raw input before the bias-passthrough and conversion so
            // the per-stage diagnostic below has the unmodified gyro for comparison.
            float rawGyroX = sample.GyroXDegPerSecond;
            float rawGyroY = sample.GyroYDegPerSecond;
            float rawGyroZ = sample.GyroZDegPerSecond;

            // 0. Bias correction — see GyroBiasEstimator. Tracks rest-state IMU
            //    offset and subtracts it before the deadzone so users on the
            //    VIIPER backend don't see Always-On drift.
            sample = biasEstimator.Correct(sample);

            // 1. Orientation correction — swap Y↔Z (with sign flip on Z) when IMU
            //    is mounted orthogonally on this device.
            float gyroX = sample.GyroXDegPerSecond;
            float gyroY = sample.GyroYDegPerSecond;
            float gyroZ = sample.GyroZDegPerSecond;
            if (stickOrientationV2 == 1)
            {
                float origY = gyroY;
                gyroY = gyroZ;
                gyroZ = -origY;
            }

            // 2. 3DOF → 2D
            float horizontal;
            float vertical;
            switch (stickConversion)
            {
                case 1: // Roll
                    horizontal = gyroZ;
                    vertical = gyroX;
                    break;
                case 2: // Yaw + Roll
                    horizontal = gyroY + gyroZ;
                    vertical = gyroX;
                    break;
                default: // Yaw
                    horizontal = gyroY;
                    vertical = gyroX;
                    break;
            }
            float postConvHorizontal = horizontal;
            float postConvVertical = vertical;

            // 3. Axis invert
            if (stickInvertX) horizontal = -horizontal;
            if (stickInvertY) vertical = -vertical;

            // 4. Deadzone
            float deadzone = Math.Max(0.0f, stickDeadzone);
            horizontal = ApplyDeadzone(horizontal, deadzone);
            vertical = ApplyDeadzone(vertical, deadzone);
            float postDeadzoneHorizontal = horizontal;
            float postDeadzoneVertical = vertical;

            // 5. One-Euro filter
            float deltaSeconds;
            if (stickLastSampleTicksUtc > 0 && sample.TimestampTicksUtc > stickLastSampleTicksUtc)
            {
                deltaSeconds = (sample.TimestampTicksUtc - stickLastSampleTicksUtc) / (float)TimeSpan.TicksPerSecond;
                deltaSeconds = Math.Max(MinDeltaSeconds, Math.Min(MaxDeltaSeconds, deltaSeconds));
            }
            else
            {
                deltaSeconds = DefaultDeltaSeconds;
            }
            stickLastSampleTicksUtc = sample.TimestampTicksUtc;

            if (!stickFilterInitialized)
            {
                stickFilteredHorizontal = horizontal;
                stickFilteredVertical = vertical;
                stickFilteredDerivativeHorizontal = 0.0f;
                stickFilteredDerivativeVertical = 0.0f;
                stickFilterInitialized = true;
            }
            else
            {
                ApplyOneEuroAxis(horizontal, stickFilteredHorizontal,
                    stickFilteredDerivativeHorizontal, deltaSeconds,
                    out stickFilteredHorizontal, out stickFilteredDerivativeHorizontal);
                ApplyOneEuroAxis(vertical, stickFilteredVertical,
                    stickFilteredDerivativeVertical, deltaSeconds,
                    out stickFilteredVertical, out stickFilteredDerivativeVertical);
                horizontal = stickFilteredHorizontal;
                vertical = stickFilteredVertical;
            }

            // 6. Precision speed (slow-tilt damping)
            if (stickPrecisionSpeed > 0)
            {
                float speed = (float)Math.Sqrt(horizontal * horizontal + vertical * vertical);
                if (speed > 0.0f && speed < stickPrecisionSpeed)
                {
                    float scale = speed / stickPrecisionSpeed;
                    horizontal *= scale;
                    vertical *= scale;
                }
            }

            // 7. Sensitivity
            float sensitivity = Math.Max(0.01f, stickSensitivityV2 / 100.0f);
            horizontal *= sensitivity;
            vertical *= sensitivity;
            float postSensHorizontal = horizontal;
            float postSensVertical = vertical;

            // 8. Speed → normalized [-1, 1]
            float minSpeed = Math.Max(0.0f, stickMinGyroSpeed);
            float maxSpeed = Math.Max(1.0f, stickMaxGyroSpeed);
            float normalizedX = MapAxisWithCurve(horizontal, minSpeed, maxSpeed);
            float normalizedY = MapAxisWithCurve(-vertical, minSpeed, maxSpeed);

            // 9. Power curve
            float power = Math.Max(0.1f, stickPowerCurve / 100.0f);
            normalizedX = Math.Sign(normalizedX) * (float)Math.Pow(Math.Abs(normalizedX), power);
            normalizedY = Math.Sign(normalizedY) * (float)Math.Pow(Math.Abs(normalizedY), power);

            // 10. Output mix (bias horizontal vs vertical)
            if (stickOutputMix > 0)
            {
                float vertScale = 1.0f - (stickOutputMix / 100.0f);
                normalizedY *= vertScale;
            }
            else if (stickOutputMix < 0)
            {
                float horizScale = 1.0f + (stickOutputMix / 100.0f);
                normalizedX *= horizScale;
            }

            // 11. Output anti-deadzone + max range
            float minOut = stickMinOutput / 100.0f;
            float maxOut = Math.Max(0.01f, stickMaxOutput / 100.0f);
            normalizedX = ApplyOutputRange(normalizedX, minOut, maxOut);
            normalizedY = ApplyOutputRange(normalizedY, minOut, maxOut);

            // 12. Clamp & convert
            outputX = ConvertNormalizedToInt16(normalizedX);
            outputY = ConvertNormalizedToInt16(normalizedY);

            // Per-stage pipeline diagnostic — emit at most once per second when
            // raw gyro shows real motion, regardless of whether the output ended
            // up zero. Helps triage "horizontal works, vertical doesn't" by
            // pinpointing the stage where the value gets dropped.
            long nowDiag = DateTime.UtcNow.Ticks;
            float rawMag = Math.Abs(rawGyroX) + Math.Abs(rawGyroY) + Math.Abs(rawGyroZ);
            if (rawMag > 5.0f && (nowDiag - pipelineDiagLastTicks) >= PipelineDiagIntervalTicks)
            {
                pipelineDiagLastTicks = nowDiag;
                Logger.Info(
                    "VIIPER stick-gyro pipeline: raw=({0:F1},{1:F1},{2:F1}) " +
                    "conv{3}→H={4:F1} V={5:F1} | postDz(dz={6:F1})→H={7:F1} V={8:F1} | " +
                    "postSens(x{9:F2})→H={10:F1} V={11:F1} | norm=({12:F2},{13:F2}) | out=({14},{15})",
                    rawGyroX, rawGyroY, rawGyroZ,
                    stickConversion, postConvHorizontal, postConvVertical,
                    deadzone, postDeadzoneHorizontal, postDeadzoneVertical,
                    sensitivity, postSensHorizontal, postSensVertical,
                    normalizedX, normalizedY,
                    outputX, outputY);
            }
        }

        // ----------------------------------------------------------------------
        // Math helpers — duplicate of the legacy CE versions to keep this
        // processor self-contained. Same constants and exact same behavior.
        // ----------------------------------------------------------------------

        private static float ApplyDeadzone(float value, float deadzone)
        {
            float magnitude = Math.Abs(value);
            if (magnitude <= deadzone) return 0.0f;
            return Math.Sign(value) * (magnitude - deadzone);
        }

        private static float ComputeOneEuroAlpha(float cutoff, float deltaSeconds)
        {
            if (cutoff <= 0.0f) return 1.0f;
            float dt = Math.Max(0.0005f, deltaSeconds);
            float tau = 1.0f / (2.0f * (float)Math.PI * cutoff);
            return 1.0f / (1.0f + (tau / dt));
        }

        private static void ApplyOneEuroAxis(
            float rawValue,
            float previousFilteredValue,
            float previousFilteredDerivative,
            float deltaSeconds,
            out float filteredValue,
            out float filteredDerivative)
        {
            float dx = (rawValue - previousFilteredValue) / Math.Max(0.0005f, deltaSeconds);
            float derivativeAlpha = ComputeOneEuroAlpha(OneEuroDerivativeCutoff, deltaSeconds);
            filteredDerivative = previousFilteredDerivative + ((dx - previousFilteredDerivative) * derivativeAlpha);
            float dynamicCutoff = OneEuroMinCutoff + (OneEuroBeta * Math.Abs(filteredDerivative));
            float valueAlpha = ComputeOneEuroAlpha(dynamicCutoff, deltaSeconds);
            filteredValue = previousFilteredValue + ((rawValue - previousFilteredValue) * valueAlpha);
        }

        private static float MapAxisWithCurve(float degPerSec, float minSpeed, float maxSpeed)
        {
            float abs = Math.Abs(degPerSec);
            if (abs <= minSpeed) return 0.0f;
            float range = maxSpeed - minSpeed;
            if (range <= 0.0f) return Math.Sign(degPerSec);
            float normalized = (abs - minSpeed) / range;
            return Math.Sign(degPerSec) * Math.Min(1.0f, normalized);
        }

        private static float ApplyOutputRange(float normalized, float minOutput, float maxOutput)
        {
            if (normalized == 0.0f) return 0.0f;
            float abs = Math.Abs(normalized);
            float scaled = minOutput + (abs * (maxOutput - minOutput));
            return Math.Sign(normalized) * Math.Min(1.0f, scaled);
        }

        private static short ConvertNormalizedToInt16(float normalized)
        {
            float clamped = Math.Max(-1.0f, Math.Min(1.0f, normalized));
            return (short)Math.Round(clamped * short.MaxValue);
        }

        /// <summary>
        /// Sum-then-clamp merge of physical right-stick + gyro stick contribution.
        /// Mirrors <c>ControllerEmulationManager.MergeStickVectors</c>. When the
        /// vector sum exceeds the int16 range, scales both axes proportionally so
        /// direction is preserved.
        /// </summary>
        public static void MergeStickVectors(short physicalX, short physicalY, short gyroX, short gyroY,
            out short mergedX, out short mergedY)
        {
            float sumX = physicalX + gyroX;
            float sumY = physicalY + gyroY;
            float magnitude = (float)Math.Sqrt((sumX * sumX) + (sumY * sumY));
            if (magnitude > short.MaxValue && magnitude > 0.0f)
            {
                float scale = short.MaxValue / magnitude;
                sumX *= scale;
                sumY *= scale;
            }
            mergedX = ClampToInt16(sumX);
            mergedY = ClampToInt16(sumY);
        }

        private static short ClampToInt16(float value)
        {
            if (value > short.MaxValue) return short.MaxValue;
            if (value < short.MinValue) return short.MinValue;
            return (short)Math.Round(value);
        }
    }
}
