using NLog;
using Shared.Enums;
using System;
using System.Threading.Tasks;
using XboxGamingBarHelper.Devices.MSIClaw;
using XboxGamingBarHelper.MSI;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// MSI Claw controller emulation startup / shutdown logic.
    ///
    /// Uses ClawButtonMonitor (DInput path, like HC's DClawController) as the sole
    /// emulation path for MSI Claw:
    ///   - Switches the physical controller to DInput mode (PID 0x1902)
    ///   - Hides ONLY PID 0x1902 via HidHide (PID 0x1901 keyboard stays visible → Win+G works)
    ///   - Creates a ViGEm virtual Xbox 360 controller
    ///   - Forwards all inputs (buttons, sticks, triggers, d-pad, M1/M2) to ViGEm
    ///
    /// Why DInput instead of XInput:
    ///   In XInput mode (PID 0x1901) the keyboard HID interface shares the same PID.
    ///   Hiding 0x1901 via HidHide also hides the keyboard → Win+G stops working →
    ///   Game Bar becomes uncontrollable. In DInput mode (PID 0x1902) the keyboard
    ///   has its own PID (not 0x1902) and is not affected by HidHide.
    ///
    /// MSIClawViGEmForwarder (XInput path) is kept in the codebase but is no longer
    /// started here — ClawButtonMonitor (DInput path) replaces it entirely.
    ///
    /// Wires ClawButtonMonitor into the helper lifecycle:
    ///   - Suppresses the legacy ControllerEmulationManager
    ///   - Subscribes to the master "Enable Controller Emulation" toggle
    ///   - Starts/stops ClawButtonMonitor dynamically as the toggle changes
    ///
    /// Adapted from HC fork ClawA1M + DClawController.
    /// </summary>
    internal partial class Program
    {
        /// <summary>
        /// True when this startup code owns the active HidHide suppression for MSI Claw.
        /// Tracks ownership so Disable() is only called when Enable() succeeded.
        /// </summary>
        private static bool _msiClawOwnsSuppression = false;

        /// <summary>
        /// Software mouse forwarder for MSI Claw "Mouse mode".
        /// Active when controller emulation is disabled (mode tile shows "Mouse").
        /// Null when not running.
        /// </summary>
        private static MSIClawDesktopModeForwarder _msiClawMouseForwarder;
        private static readonly object _msiClawMouseForwarderLock = new object();

        /// <summary>
        /// Start the MSI Claw DInput-based virtual controller emulation.
        ///
        /// Adapted from HC DClawController + ClawA1M:
        ///  - Switches physical controller to DInput mode (PID 0x1902)
        ///  - Hides only PID 0x1902 via HidHide (keyboard at PID 0x1901 stays visible)
        ///  - Creates a ViGEm virtual Xbox 360 controller
        ///  - Reads HID input reports at DInput interface (UsagePage 0xFFF0)
        ///  - Forwards all buttons, sticks, triggers, d-pad + M1/M2 to ViGEm
        ///
        /// Runs on a background thread (mode switch + HidHide settle ~2 s).
        ///
        /// Respects the master "Enable Controller Emulation" toggle
        /// (ControllerEmulationManager.EmulationEnabled).
        /// Subscribes to EmulationEnabledChanged for dynamic start/stop.
        /// </summary>
        // ── Left CLAW button WMI listener ────────────────────────────────────────
        // The left front button (CLAW logo, OEM1) fires a WMI event MSI_Event with
        // EventCode=41 (LaunchMcxMainUI). Ported from HC ClawA1M.cs specialKeyWatcher.
        // Started once at startup regardless of emulation state.
        private static System.Management.ManagementEventWatcher _clawButtonWatcher;
        private static readonly object _clawButtonWatcherLock = new object();

        private static void StartClawButtonWmiListener()
        {
            var deviceInfo = Devices.DeviceDetector.DetectDevice();
            if (deviceInfo.DeviceType != DeviceType.MSIClaw) return;

            // Restore persisted action from global settings (survives reinstall)
            legionManager?.LoadDesktopButtonTileAction();

            lock (_clawButtonWatcherLock)
            {
                if (_clawButtonWatcher != null) return;
                try
                {
                    var scope = new System.Management.ManagementScope(@"\\.\root\WMI");
                    var query = new System.Management.WqlEventQuery("SELECT * FROM MSI_Event");
                    _clawButtonWatcher = new System.Management.ManagementEventWatcher(scope, query);
                    _clawButtonWatcher.EventArrived += OnClawWmiEvent;
                    _clawButtonWatcher.Start();
                    Logger.Info("MSIClaw: WMI MSI_Event listener started (left CLAW button)");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"MSIClaw: WMI listener failed to start: {ex.Message}");
                }
            }
        }

        private static void StopClawButtonWmiListener()
        {
            lock (_clawButtonWatcherLock)
            {
                try { _clawButtonWatcher?.Stop(); _clawButtonWatcher?.Dispose(); } catch { }
                _clawButtonWatcher = null;
            }
        }

        // Debug: timestamp of the previous CLAW WMI event, to log the gap between events
        // (used while investigating long-press / double-press detection on the left OEM button).
        private static long _lastClawWmiTicks;

        private static void OnClawWmiEvent(object sender, System.Management.EventArrivedEventArgs e)
        {
            try
            {
                // HC ClawA1M.cs: property name is "WMIEvent", value masked to 1 byte
                // EventCode 41 (0x29) = LaunchMcxMainUI = left CLAW button
                long raw = 0;
                int code = 0;
                try
                {
                    raw = Convert.ToInt64(e.NewEvent.Properties["MSIEvt"].Value);
                    code = (int)(raw & 0xFF);
                }
                catch
                {
                    // Fallback: log all property names for diagnosis
                    var sb = new System.Text.StringBuilder();
                    foreach (System.Management.PropertyData p in e.NewEvent.Properties)
                        sb.Append($"{p.Name}={p.Value} ");
                    var names = sb.ToString();
                    Logger.Info($"MSIClaw: MSI_Event received, properties: {names}");
                    return;
                }

                // ── DEBUG: every CLAW WMI event with timing + all properties ──────────────
                // Lets us see, from a log, whether HOLDING the left OEM button repeats the
                // event, fires a separate release/up event, or sends only one event per press
                // (which would mean long-press can't be derived from WMI alone). Also captures
                // the gap between events for short/long/double-press sequences.
                try
                {
                    long nowTicks = DateTime.UtcNow.Ticks;
                    double dtMs = _lastClawWmiTicks == 0 ? 0 : (nowTicks - _lastClawWmiTicks) / 10000.0;
                    _lastClawWmiTicks = nowTicks;
                    var props = new System.Text.StringBuilder();
                    foreach (System.Management.PropertyData p in e.NewEvent.Properties)
                        props.Append($"{p.Name}={p.Value} ");
                    Logger.Info($"MSIClaw[WMI-DEBUG]: raw=0x{raw:X} ({raw}) code={code} dtSinceLast={dtMs:F0}ms props=[ {props}]");
                }
                catch { /* logging only */ }

                if (code != 41) return; // 41 = LaunchMcxMainUI = left CLAW button

                Logger.Info("MSIClaw: Left CLAW button pressed (WMI LaunchMcxMainUI)");
                HandleLeftClawPress();
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnClawWmiEvent threw: {ex.Message}");
            }
        }

        // Double-click state for the Left MSI button.
        private static readonly object _clawClickLock = new object();
        private static System.Threading.Timer _clawSingleTimer;
        private static bool _clawSinglePending;

        /// <summary>
        /// Routes a Left-CLAW press. When double-click is disabled the single action fires
        /// immediately (no latency). When enabled, the first press is held for the configured
        /// window; a second press within it fires the double-click action instead, otherwise the
        /// single action fires when the window elapses.
        /// </summary>
        private static void HandleLeftClawPress()
        {
            bool dcEnabled = legionManager?.DoubleClickEnabled ?? false;
            int delay = legionManager?.DoubleClickDelayMs ?? 220;

            if (!dcEnabled)
            {
                FireClawClickToWidget("single");
                ExecuteLeftClawAction(legionManager?.DesktopButtonTileAction ?? -1,
                                      legionManager?.DesktopButtonTileActionParam ?? "");
                return;
            }

            lock (_clawClickLock)
            {
                if (_clawSinglePending)
                {
                    // Second press within the window → double-click.
                    _clawSinglePending = false;
                    _clawSingleTimer?.Dispose();
                    _clawSingleTimer = null;
                    int dcAction = legionManager?.DoubleClickAction ?? 0;
                    string dcParam = legionManager?.DoubleClickParam ?? "";
                    Logger.Info($"MSIClaw: Left CLAW DOUBLE-click → action {dcAction}");
                    FireClawClickToWidget("double");
                    ExecuteLeftClawAction(dcAction, dcParam);
                    return;
                }

                // First press: arm a one-shot timer for the single action.
                _clawSinglePending = true;
                _clawSingleTimer?.Dispose();
                _clawSingleTimer = new System.Threading.Timer(_ =>
                {
                    bool fire = false;
                    lock (_clawClickLock)
                    {
                        if (_clawSinglePending) { _clawSinglePending = false; fire = true; }
                        _clawSingleTimer?.Dispose();
                        _clawSingleTimer = null;
                    }
                    if (fire)
                    {
                        Logger.Info("MSIClaw: Left CLAW single-click (window elapsed)");
                        FireClawClickToWidget("single");
                        ExecuteLeftClawAction(legionManager?.DesktopButtonTileAction ?? -1,
                                              legionManager?.DesktopButtonTileActionParam ?? "");
                    }
                }, null, delay, System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>Notify the widget (when connected) of a resolved click so its UI can flash a
        /// detection indicator. Best-effort.</summary>
        private static void FireClawClickToWidget(string kind)
        {
            try { pipeServer?.SendMessage($"{{\"ClawClick\":\"{kind}\"}}"); } catch { }
        }

        /// <summary>Executes a Left-CLAW action id (same dispatch the tile/front-button path uses).</summary>
        private static void ExecuteLeftClawAction(int actionType, string actionParam)
        {
            if (actionType < 0)
            {
                Logger.Debug("MSIClaw: Left CLAW button — no Action mapping configured");
                return;
            }
            string actionName = TileActionNames.GetDisplayName(actionType);
            Logger.Info($"MSIClaw: Left CLAW executing action {actionType} ({actionName}) param='{actionParam}'");

            // Execute directly in helper — widget may be suspended when Game Bar is closed.
            switch (actionType)
            {
                case 1: // KeyboardShortcut — used by the double-click "Keyboard" mode (param = token string)
                    if (!string.IsNullOrWhiteSpace(actionParam)) SendKeyboardShortcutViaInputInjector(actionParam);
                    break;
                case 10: AdjustBrightness(+5); break;   // BrightnessUp
                case 11: AdjustBrightness(-5); break;   // BrightnessDown
                case 12: SendKeyboardShortcutViaInputInjector("Alt+Tab"); break; // AltTab
                case 13: SendKeyboardShortcutViaInputInjector("Alt+Tab"); break; // AltTabBack
                case 14: SendKeyboardShortcutViaInputInjector("Win+D"); break;   // GoToDesktop
                case 27: AdjustVolume(+5); break;       // VolumeUp
                case 28: AdjustVolume(-5); break;       // VolumeDown
                case 40: LaunchLauncher("SteamBigPicture"); break;
                case 41: LaunchLauncher("Playnite"); break;
                case 42: LaunchLauncher("XboxApp"); break;
                case 30: SendKeyboardShortcutViaInputInjector("MEDIA_NEXT_TRACK"); break;
                case 31: SendKeyboardShortcutViaInputInjector("MEDIA_PREV_TRACK"); break;
                case 32: SendKeyboardShortcutViaInputInjector("MEDIA_PLAY_PAUSE"); break;
                case 50: case 51: case 52: case 53: case 59: // Program Actions
                    LaunchProgramTarget(ResolveProgramTargetHelper(actionType, actionParam));
                    break;
                case 60: case 61: case 62: case 63: case 64: case 65: case 69: // Launch Website
                    LaunchUrl(ResolveWebsiteUrlHelper(actionType, actionParam));
                    break;
                default:
                    // App actions that need widget state — try widget (may not work when suspended)
                    FireTileHotkeyToWidget($"__action__{actionType}", actionName);
                    break;
            }
        }

        /// <summary>
        /// Apply an MSI Claw fan command from the widget.
        /// value: 0/1/2 = software preset (Quiet/Default/Aggressive); -1 = disable → firmware control.
        /// Persisted so it can be re-applied on startup (the EC resets across reboots).
        /// </summary>
        internal static void ApplyMsiFan(int value)
        {
            try
            {
                Logger.Info($"ApplyMsiFan: value={value}");
                Settings.LocalSettingsHelper.SetValue("MsiFan_Value", value);

                if (value < 0)
                {
                    MsiClawFanController.RestoreFirmwareControl();
                    return;
                }

                // 3 = custom curve stored as CSV; 0/1/2 = built-in presets.
                if (value == 3)
                {
                    if (Settings.LocalSettingsHelper.TryGetValue<string>("MsiFan_Curve", out string csv)
                        && TryParseCurve(csv, out double[] custom))
                    {
                        MsiClawFanController.ApplySoftwareCurve(custom);
                    }
                    else
                    {
                        Logger.Warn("ApplyMsiFan: custom selected but no valid saved curve — falling back to Default");
                        MsiClawFanController.ApplySoftwareCurve(MsiClawFanController.Curve_Default);
                    }
                    return;
                }

                double[] curve;
                switch (value)
                {
                    case 0:  curve = MsiClawFanController.Curve_Quiet;      break;
                    case 2:  curve = MsiClawFanController.Curve_Aggressive; break;
                    default: curve = MsiClawFanController.Curve_Default;    break;
                }
                MsiClawFanController.ApplySoftwareCurve(curve);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ApplyMsiFan({value}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Read the current EC fan table + control bit and push it back to the widget as
        /// "MsiFanStatus":"b0,..,b7|controlBit|readOk" so the widget can verify against the graph.
        /// </summary>
        internal static void ReportMsiFanStatus()
        {
            try
            {
                bool ok = MsiClawFanController.ReadStatus(out byte[] table, out bool controlOn);
                string csv = string.Join(",", table);
                string json = $"{{\"MsiFanStatus\":\"{csv}|{(controlOn ? 1 : 0)}|{(ok ? 1 : 0)}\"}}";
                if (pipeServer != null && pipeServer.IsConnected)
                {
                    pipeServer.SendMessage(json);
                    Logger.Info($"ReportMsiFanStatus: sent '{csv}|{(controlOn ? 1 : 0)}|{(ok ? 1 : 0)}'");
                }
                else
                {
                    Logger.Warn("ReportMsiFanStatus: pipe not connected");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"ReportMsiFanStatus failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Push the persisted MSI fan state (preset value + custom curve) to the widget on
        /// connect, so its UI reflects what the helper actually restored at boot. The helper is
        /// the single source of truth; the widget must NOT push its own state on open.
        /// Format: "MsiFanState":"&lt;value&gt;|&lt;curveCsvOrEmpty&gt;".
        /// </summary>
        internal static void PushMsiFanStateToWidget()
        {
            try
            {
                if (pipeServer == null || !pipeServer.IsConnected) return;
                int value = Settings.LocalSettingsHelper.TryGetValue<int>("MsiFan_Value", out int v) ? v : -1;
                string curve = (value == 3 && Settings.LocalSettingsHelper.TryGetValue<string>("MsiFan_Curve", out string c)) ? c : "";
                string json = $"{{\"MsiFanState\":\"{value}|{curve}\"}}";
                pipeServer.SendMessage(json);
                Logger.Info($"PushMsiFanStateToWidget: sent value={value} curve='{curve}'");
            }
            catch (Exception ex)
            {
                Logger.Warn($"PushMsiFanStateToWidget failed: {ex.Message}");
            }
        }

        /// <summary>Apply and persist a custom 11-point fan curve sent as a CSV string.</summary>
        internal static void ApplyMsiFanCurve(string csv)
        {
            try
            {
                Logger.Info($"ApplyMsiFanCurve: csv='{csv}'");
                if (!TryParseCurve(csv, out double[] curve))
                {
                    Logger.Warn("ApplyMsiFanCurve: could not parse curve");
                    return;
                }
                Settings.LocalSettingsHelper.SetValue("MsiFan_Curve", csv);
                Settings.LocalSettingsHelper.SetValue("MsiFan_Value", 3); // 3 = custom
                MsiClawFanController.ApplySoftwareCurve(curve);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ApplyMsiFanCurve failed: {ex.Message}");
            }
        }

        private static bool TryParseCurve(string csv, out double[] curve)
        {
            curve = null;
            if (string.IsNullOrWhiteSpace(csv)) return false;
            var parts = csv.Split(',');
            if (parts.Length != 11) return false;
            var result = new double[11];
            for (int i = 0; i < 11; i++)
            {
                if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    return false;
                result[i] = Math.Max(0, Math.Min(100, v));
            }
            curve = result;
            return true;
        }

        // ── Battery charge limit persistence (helper-side) ──────────────────────────
        // The EC charge-limit byte can be cleared by an EC reset / BIOS update / full drain,
        // and the helper restarts on every reboot (scheduled task). We persist the user's
        // last setting helper-side and re-apply it on startup so the limit survives.
        private const string ChargeLimitOnKey  = "MsiChargeLimit_On";
        private const string ChargeLimitPctKey = "MsiChargeLimit_Pct";

        /// <summary>Persist the charge-limit setting helper-side (called from the pipe handler).</summary>
        internal static void PersistMsiChargeLimit(bool enabled, int percent)
        {
            try
            {
                Settings.LocalSettingsHelper.SetValue(ChargeLimitOnKey, enabled);
                Settings.LocalSettingsHelper.SetValue(ChargeLimitPctKey, percent);
            }
            catch (Exception ex) { Logger.Warn($"PersistMsiChargeLimit failed: {ex.Message}"); }
        }

        /// <summary>
        /// Re-apply the saved battery charge limit at startup (EC value can be reset on reboot /
        /// EC reset). Only acts when the user had the limit enabled.
        /// </summary>
        private static void RestoreMsiChargeLimitOnStartup()
        {
            try
            {
                bool enabled = Settings.LocalSettingsHelper.TryGetValue<bool>(ChargeLimitOnKey, out bool on) && on;
                if (!enabled) return;

                int percent = Settings.LocalSettingsHelper.TryGetValue<int>(ChargeLimitPctKey, out int p) ? p : 90;
                Logger.Info($"RestoreMsiChargeLimitOnStartup: re-applying saved charge limit {percent}% (enabled)");
                Devices.MSIClaw.MsiClawBatteryManager.SetPercent(percent);
                Devices.MSIClaw.MsiClawBatteryManager.SetEnabled(true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"RestoreMsiChargeLimitOnStartup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Charge Limiter Quick Settings tile triggered by a controller shortcut while the Game
        /// Bar is CLOSED (widget suspended). Toggles the charge limit on/off using the persisted
        /// user value. Only acts if the limiter was configured at least once (persisted % present).
        /// </summary>
        internal static void ToggleMsiChargeLimitFromHotkey()
        {
            try
            {
                if (!Settings.LocalSettingsHelper.TryGetValue<int>(ChargeLimitPctKey, out int pct))
                {
                    Logger.Info("ChargeLimiter hotkey: not set up yet — ignoring");
                    rtssManager?.ShowNotification("Charge Limit: set it up in Settings first", 4000);
                    return;
                }
                if (pct < 20 || pct > 100) pct = 90;

                bool curOn = Settings.LocalSettingsHelper.TryGetValue<bool>(ChargeLimitOnKey, out bool on) && on;
                bool newOn = !curOn;

                Devices.MSIClaw.MsiClawBatteryManager.SetPercent(pct);
                Devices.MSIClaw.MsiClawBatteryManager.SetEnabled(newOn);
                PersistMsiChargeLimit(newOn, pct);
                Logger.Info($"ChargeLimiter hotkey → {(newOn ? $"On {pct}%" : "Off")}");
                rtssManager?.ShowNotification($"Charge Limit: {(newOn ? $"On {pct}%" : "Off")}", 4000);
            }
            catch (Exception ex) { Logger.Warn($"ToggleMsiChargeLimitFromHotkey: {ex.Message}"); }
        }

        /// <summary>
        /// FPS Limiter Quick Settings tile triggered by a controller shortcut while the Game Bar is
        /// CLOSED. Cycles the FPS cap in the CURRENT mode (RTSS or Intel), including Off — never
        /// switches mode. Uses the helper's own FPS/Intel properties whose SetValue applies the
        /// hardware change AND syncs the value back to the widget.
        /// </summary>
        internal static void CycleFpsLimitFromHotkey()
        {
            try
            {
                bool isIntel = intelGpuManager?.FpsCapMode?.Value == 1;
                if (isIntel)
                {
                    var tier = intelGpuManager?.IntelFpsTier;
                    if (tier == null) return;
                    int next = (tier.Value + 1) % 4;            // 0=Off,1=60,2=40,3=30
                    tier.SetValue(next);
                    string[] labels = { "Off", "60 FPS", "40 FPS", "30 FPS" };
                    Logger.Info($"FpsLimiter hotkey (Intel) → tier {next}");
                    rtssManager?.ShowNotification($"FPS Limit: {labels[next]}", 4000);
                }
                else
                {
                    var fp = rtssManager?.FPSLimit;
                    if (fp == null) return;
                    int[] vals = { 0, 30, 40, 60, 90, 120 };
                    int cur = fp.Value, idx = 0;
                    for (int i = 0; i < vals.Length; i++) if (vals[i] == cur) { idx = i; break; }
                    int nextV = vals[(idx + 1) % vals.Length];
                    fp.SetValue(nextV);
                    Logger.Info($"FpsLimiter hotkey (RTSS) → {nextV}");
                    rtssManager?.ShowNotification($"FPS Limit: {(nextV > 0 ? nextV + " FPS" : "Off")}", 4000);
                }
            }
            catch (Exception ex) { Logger.Warn($"CycleFpsLimitFromHotkey: {ex.Message}"); }
        }

        /// <summary>
        /// Re-apply the last saved MSI fan command at startup (EC resets on reboot).
        /// Only acts when a custom curve was enabled; otherwise leaves firmware control alone.
        /// </summary>
        private static void RestoreMsiFanOnStartup()
        {
            try
            {
                if (Settings.LocalSettingsHelper.TryGetValue<int>("MsiFan_Value", out int value) && value >= 0)
                {
                    Logger.Info($"RestoreMsiFanOnStartup: re-applying saved MSI fan preset {value}");
                    ApplyMsiFan(value);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"RestoreMsiFanOnStartup failed: {ex.Message}");
            }
        }

        // Set once the fan-sensor probe has run, so we don't repeat the (potentially slow)
        // SuperIO/EC enumeration.
        private static bool _msiFanSensorsProbed;

        /// <summary>
        /// One-shot diagnostic: spin up an ISOLATED LibreHardwareMonitor instance with the
        /// Motherboard + Controller sources enabled (the main PerformanceManager keeps them off
        /// because SuperIO probing can hang) and log every Fan / Control sensor it finds. This
        /// tells us whether the MSI Claw exposes a readable fan RPM and from which hardware/sensor.
        /// Runs on a background thread, fully guarded, and disposes the probe instance afterwards.
        /// </summary>
        private static void ProbeMsiFanSensors()
        {
            if (_msiFanSensorsProbed) return;
            _msiFanSensorsProbed = true;

            System.Threading.Tasks.Task.Run(() =>
            {
                LibreHardwareMonitor.Hardware.Computer probe = null;
                try
                {
                    Logger.Info("FanProbe: opening isolated LHM instance (Motherboard+Controller enabled) to scan for fan sensors...");
                    probe = new LibreHardwareMonitor.Hardware.Computer
                    {
                        IsMotherboardEnabled = true,
                        IsControllerEnabled  = true,
                        IsCpuEnabled         = false,
                        IsGpuEnabled         = false,
                        IsMemoryEnabled      = false,
                        IsStorageEnabled     = false,
                        IsNetworkEnabled     = false,
                        IsBatteryEnabled     = false,
                    };
                    probe.Open();

                    int fanCount = 0;
                    foreach (var hw in probe.Hardware)
                    {
                        try { hw.Update(); } catch { }
                        Logger.Info($"FanProbe: hardware '{hw.Name}' ({hw.HardwareType})");

                        foreach (var sensor in hw.Sensors)
                        {
                            if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan ||
                                sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Control)
                            {
                                fanCount++;
                                Logger.Info($"FanProbe:   [{sensor.SensorType}] '{sensor.Name}' = {(sensor.Value.HasValue ? sensor.Value.Value.ToString("0.#") : "null")} (id={sensor.Identifier})");
                            }
                        }

                        foreach (var sub in hw.SubHardware)
                        {
                            try { sub.Update(); } catch { }
                            Logger.Info($"FanProbe:   sub-hardware '{sub.Name}' ({sub.HardwareType})");
                            foreach (var sensor in sub.Sensors)
                            {
                                if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan ||
                                    sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Control)
                                {
                                    fanCount++;
                                    Logger.Info($"FanProbe:     [{sensor.SensorType}] '{sensor.Name}' = {(sensor.Value.HasValue ? sensor.Value.Value.ToString("0.#") : "null")} (id={sensor.Identifier})");
                                }
                            }
                        }
                    }

                    Logger.Info($"FanProbe: done — found {fanCount} fan/control sensor(s). " +
                                (fanCount > 0 ? "RPM is readable via LHM." : "No fan sensor exposed via LHM on this device."));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"FanProbe: failed: {ex.Message}");
                }
                finally
                {
                    try { probe?.Close(); } catch { }
                }
            });
        }

        private static void StartMSIClawControllerEmulation()
        {
            try
            {
                var deviceInfo = Devices.DeviceDetector.DetectDevice();
                if (deviceInfo.DeviceType != DeviceType.MSIClaw)
                {
                    Logger.Debug("MSIClaw: DInput path skipped (not an MSI Claw)");
                    return;
                }

                // Re-apply any saved custom fan curve (EC resets across reboots).
                RestoreMsiFanOnStartup();

                // Re-apply the saved battery charge limit if it was enabled (EC can reset on reboot).
                RestoreMsiChargeLimitOnStartup();

                // One-shot diagnostic: log which fan/RPM sensors LHM can see on this device.
                ProbeMsiFanSensors();

                // ── Step 0: Register MsiClawControllerMode callback ──────────────
                // MsiClawControllerModeManager.OnModeChanged is a static delegate.
                // Registering here (not at field-init time) ensures Program.MSIClaw.cs
                // is the authoritative owner; any previous value is safely overwritten.
                MsiClawControllerModeManager.OnModeChanged = OnMsiClawControllerModeChanged;

                // ── Step 0.1: Wire mouse sensitivity / threshold callbacks ─────────
                // ControllerEmulationManager is suppressed for MSI Claw, so its
                // SetMouseSensitivity/SetMouseThreshold never reach ClawButtonMonitor.
                // We wire Action callbacks here so slider changes propagate at runtime.
                if (controllerEmulationManager != null)
                {
                    controllerEmulationManager.OnMouseSensitivityChanged = s =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseModeSensitivity(s);
                    };
                    controllerEmulationManager.OnMouseThresholdChanged = t =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseModeThreshold(t);
                    };
                    controllerEmulationManager.OnMouseLeftClickButtonChanged = v =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseLeftClickButton(v);
                    };
                    controllerEmulationManager.OnMouseRightClickButtonChanged = v =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseRightClickButton(v);
                    };
                    controllerEmulationManager.OnMouseCursorStickChanged = v =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseCursorStick(v);
                    };
                    controllerEmulationManager.OnMouseScrollStickChanged = v =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseScrollStick(v);
                    };
                }

                // ── Step 1: Suppress legacy ControllerEmulationManager ─────────────
                // Reuse the same SetSuppressedByViiper() flag that VIIPER uses — identical
                // intent: tell the legacy backend that a higher-priority backend owns the
                // virtual controller for this session.
                if (controllerEmulationManager != null)
                {
                    controllerEmulationManager.SetSuppressedByViiper(true);
                    Logger.Info("MSIClaw: Legacy ControllerEmulationManager suppressed (ClawButtonMonitor DInput path takes over)");
                }

                // ── Step 1.5: Wire XInput button remap callback ───────────────────────
                // LegionManager.SetButtonMappingAdvanced() will invoke this callback instead
                // of connecting to Legion hardware (which doesn't exist on MSI Claw).
                // Pre-wired here so any stored button mapping loaded later (e.g. from profile
                // restore) is immediately routed to ClawButtonMonitor.ConfigureXInputRemap().
                WireClawXInputRemapCallback();

                // ── Step 2: Subscribe to master "Enable Controller Emulation" toggle ─
                // Do NOT unsubscribe inside the handler — if we did, toggling off would
                // remove the listener and re-enabling would silently do nothing.
                // Subscription lifetime = helper lifetime; only removed in the final
                // DisposeMSIClawControllerEmulation() called during shutdown.
                if (controllerEmulationManager != null)
                {
                    controllerEmulationManager.EmulationEnabledChanged += OnMSIClawEmulationEnabledChanged;
                }

                // ── Step 3: Guard on master "Enable Controller Emulation" toggle ──
                // The widget sends the persisted value on connect. Until the widget
                // connects, EmulationEnabled defaults to false — meaning emulation must
                // NOT start automatically. OnMSIClawEmulationEnabledChanged is already
                // subscribed (Step 2) and will start the correct path when the toggle
                // value arrives from the widget.
                bool emulationEnabled = controllerEmulationManager?.EmulationEnabled ?? false;
                if (!emulationEnabled)
                {
                    Logger.Info("MSIClaw: ControllerEmulationEnabled=false at startup — waiting for widget to send toggle state");
                    return;
                }

                // ── Step 4: Honour the current mode state ─────────────────────────
                // Use MsiClawControllerMode (default true = Controller).
                bool controllerModeOn = msiClawControllerModeManager?.MsiClawControllerMode?.Value ?? true;
                if (!controllerModeOn)
                {
                    Logger.Info("MSIClaw: MsiClawControllerMode=Mouse at startup — starting ClawButtonMonitor in mouse mode");
                    StartClawButtonMonitorBackground(startInMouseMode: true);
                    return;
                }

                StartClawButtonMonitorBackground();
            }
            catch (Exception ex)
            {
                Logger.Error($"MSIClaw: Failed to start ClawButtonMonitor: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the master "Enable Controller Emulation" toggle changes.
        /// Mirrors ViiperEmulationManager.OnEmulationEnabledChanged.
        ///
        /// Toggle semantics for MSI Claw mode tile:
        ///   emulationEnabled = true  → "Controller" mode: ClawButtonMonitor running (ViGEm Xbox 360)
        ///   emulationEnabled = false → "Mouse" mode: ClawButtonMonitor stopped, MSIClawDesktopModeForwarder active
        ///
        /// NOTE: Does NOT remove the event subscription — the subscription must remain
        /// alive so subsequent toggles (off → on → off) all work correctly.
        /// </summary>
        private static void OnMSIClawEmulationEnabledChanged(bool emulationEnabled)
        {
            try
            {
                if (emulationEnabled)
                {
                    // Switching to Controller mode:
                    // Clear mouse mode flag on ClawButtonMonitor — DInput poll loop resumes
                    // full ViGEm forwarding. No stop/start needed; no mode switch sent.
                    // If ClawButtonMonitor isn't running yet, start it normally.
                    Logger.Info("MSIClaw: EmulationEnabled → true (Controller mode) — disabling mouse mode on ClawButtonMonitor");
                    lock (clawButtonMonitorLock)
                    {
                        if (clawButtonMonitor != null && clawButtonMonitor.IsRunning)
                            clawButtonMonitor.SetMouseMode(false);
                        else
                            StartClawButtonMonitorBackground();
                    }
                }
                else
                {
                    // Switching to Mouse mode:
                    // Set mouse mode flag on ClawButtonMonitor — DInput poll loop translates
                    // stick/button inputs to Windows mouse events. Physical controller stays
                    // hidden (HidHide active), no firmware mode switch needed.
                    // HC fork approach: mouse mode is virtual, physical device stays in DInput.
                    Logger.Info("MSIClaw: EmulationEnabled → false (Mouse mode) — enabling mouse mode on ClawButtonMonitor");
                    lock (clawButtonMonitorLock)
                    {
                        if (clawButtonMonitor != null && clawButtonMonitor.IsRunning)
                            clawButtonMonitor.SetMouseMode(true);
                        else
                        {
                            // ClawButtonMonitor not running — start it and then set mouse mode.
                            // StartClawButtonMonitorBackground() is async; mouse mode will be
                            // applied once the monitor is running via the startup flow.
                            Logger.Info("MSIClaw: ClawButtonMonitor not running — starting in mouse mode");
                            StartClawButtonMonitorBackground(startInMouseMode: true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawEmulationEnabledChanged threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the MsiClawControllerMode Quick Settings tile is tapped.
        /// Routes to OnMSIClawEmulationEnabledChanged() which handles the full
        /// ClawButtonMonitor ↔ MSIClawDesktopModeForwarder lifecycle.
        ///
        /// true  = Controller mode (ClawButtonMonitor + ViGEm)
        /// false = Mouse mode (MSIClawDesktopModeForwarder)
        /// </summary>
        private static void OnMsiClawControllerModeChanged(bool controllerOn)
        {
            Logger.Info($"MSIClaw: MsiClawControllerMode changed → {(controllerOn ? "Controller" : "Mouse")}");
            OnMSIClawEmulationEnabledChanged(controllerOn);
        }

        /// <summary>
        /// Starts MSIClawDesktopModeForwarder on a background thread.
        /// The mode switch + settle (~600 ms) must not block the toggle handler.
        /// </summary>
        private static void StartMSIClawMouseForwarderBackground()
        {
            Task.Run(() =>
            {
                try
                {
                    lock (_msiClawMouseForwarderLock)
                    {
                        if (_msiClawMouseForwarder == null)
                            _msiClawMouseForwarder = new MSIClawDesktopModeForwarder();

                        if (!_msiClawMouseForwarder.IsRunning)
                        {
                            bool ok = _msiClawMouseForwarder.Start();
                            if (!ok)
                                Logger.Warn("MSIClaw: MSIClawDesktopModeForwarder Start() failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"MSIClaw: StartMSIClawMouseForwarderBackground threw: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Stops MSIClawDesktopModeForwarder synchronously.
        /// Releases any held mouse buttons before stopping.
        /// </summary>
        private static void StopMSIClawMouseForwarder()
        {
            try
            {
                lock (_msiClawMouseForwarderLock)
                {
                    _msiClawMouseForwarder?.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: StopMSIClawMouseForwarder threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops ClawButtonMonitor and performs a full HidHide reset.
        ///
        /// Uses ForceUnhideAll() instead of Disable() to guarantee physical controllers
        /// reappear even if the in-memory hiddenDeviceIds tracking was lost (e.g. helper
        /// restart, failed Enable(), or crash recovery):
        ///   1. Stop ClawButtonMonitor (disposes ViGEm virtual controller)
        ///   2. Read ALL blocked instance IDs live from the HidHide driver and remove them
        ///   3. Disable HidHide cloaking (IsActive = false)
        ///   4. Cycle USB ports for ALL MSI Claw devices (VID_0DB0 PID_1901 + PID_1902)
        ///      so they re-enumerate and reappear in Joy.cpl / Steam / games.
        ///
        /// Does NOT null out clawButtonMonitor — instance stays alive for re-enable.
        /// </summary>
        private static void StopMSIClawButtonMonitor()
        {
            // Stop ViGEm virtual controller
            try
            {
                lock (clawButtonMonitorLock)
                {
                    clawButtonMonitor?.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: Error stopping ClawButtonMonitor: {ex.Message}");
            }

            // Full HidHide reset — ForceUnhideAll() bypasses internal tracking and
            // reads the live blocked list directly from the driver. No ownership guard
            // (_msiClawOwnsSuppression) needed: we always want to clean up HidHide
            // on stop, regardless of whether Enable() completed successfully.
            if (controllerEmulationManager?.SuppressionManager != null)
            {
                try
                {
                    controllerEmulationManager.SuppressionManager.ForceUnhideAll();
                    Logger.Info("MSIClaw: ForceUnhideAll complete — physical MSI Claw controllers restored");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"MSIClaw: ForceUnhideAll threw: {ex.Message}");
                }
            }

            _msiClawOwnsSuppression = false;
        }

        /// <summary>
        /// Spins up ClawButtonMonitor (DInput path) on a background thread.
        ///
        /// Ordering matches HC DClawController:
        ///   1. Enable HidHide for PID 0x1902 only (keyboard at 0x1901 stays visible)
        ///   2. Start ClawButtonMonitor — switches to DInput mode, creates ViGEm, begins polling
        ///
        /// Must not block the caller — DInput mode switch + HidHide settle takes ~2 s.
        /// </summary>
        private static void StartClawButtonMonitorBackground(bool startInMouseMode = false)
        {
            Task.Run(() =>
            {
                try
                {
                    // ── Step 0: MSI Center guard ──────────────────────────────────
                    // Controller emulation requires MSI Center M to be fully stopped.
                    // MSI software re-registers controllers and fights HidHide suppression.
                    if (msiCenterManager?.DetectActive() == true)
                    {
                        Logger.Warn("[MSIClaw] MSI Center M is active — controller emulation blocked. " +
                                    "Stop MSI Center M via the Quick Tab tile first.");
                        return; // Hard block: do not start ClawButtonMonitor / ViGEm
                    }

                    // ── Step 0.5: Proactive XInput mode switch ────────────────────
                    // Mirrors HC ClawA1M.Open() → SwitchMode(gamepadMode):
                    // If the controller is in hardware Desktop mode (e.g. left by a previous
                    // HC session or after ClawTweaks shutdown), the user would have no gamepad
                    // input during the ~2.5 s DInput settle in ClawButtonMonitor.Start().
                    // Switching to XInput first gives immediate gamepad behaviour.
                    // No settle needed here — ClawButtonMonitor.Start() includes its own
                    // 2.5 s settle after the XInput → DInput transition.
                    MSIClawHidController.TrySwitchToXInput();

                    // ── Step 1: Start ClawButtonMonitor ───────────────────────────
                    // MUST happen before HidHide Enable.
                    // Start() is synchronous for the DInput mode switch: OpenClawInterfaces()
                    // sends SwitchMode(DInput) and waits ~2.5 s for PID_1902 to enumerate
                    // before returning. HidHide Enable must query PID_1902 — if called before
                    // Start() the device is still in XInput mode (PID_1901) and Enable() finds
                    // zero matching device IDs → suppression silently fails.
                    var monitor = EnsureClawButtonMonitor();

                    // Apply current gyro settings from profile BEFORE Start().
                    // LegionControllerSetting_PropertyChanged null-guards on clawButtonMonitor,
                    // so any gyro settings applied during profile load (when the monitor was null)
                    // were silently dropped. New instances default to _gyroActivationButton=0
                    // (always active) which caused the "always-on regardless of LT" bug.
                    // This block is the 1:1 equivalent of RestoreGlobalProfileSettings() for gyro,
                    // using the same properties that LegionControllerSetting_PropertyChanged routes.
                    if (legionManager != null)
                    {
                        monitor.SetGyroTarget(legionManager.LegionGyroTarget.Value);
                        monitor.SetGyroActivationButton(legionManager.LegionGyroActivationButton.Value);
                        monitor.SetGyroActivationMode(legionManager.LegionGyroActivationMode.Value);
                        monitor.SetGyroSensitivityX(legionManager.LegionGyroSensitivityX.Value);
                        monitor.SetGyroSensitivityY(legionManager.LegionGyroSensitivityY.Value);
                        monitor.SetGyroDeadzone(legionManager.LegionGyroDeadzone.Value);
                        monitor.SetGyroInvertX(legionManager.LegionGyroInvertX.Value);
                        monitor.SetGyroInvertY(legionManager.LegionGyroInvertY.Value);
                        Logger.Info($"MSIClaw: Gyro settings applied before Start() — target={legionManager.LegionGyroTarget.Value}, activationButton={legionManager.LegionGyroActivationButton.Value}, activationMode={legionManager.LegionGyroActivationMode.Value}");
                    }

                    bool ok = monitor.Start();

                    if (!ok)
                    {
                        Logger.Warn("MSIClaw: ClawButtonMonitor Start() failed — ViGEmBus not installed or Claw DInput interface unavailable");
                        return;
                    }

                    Logger.Info("MSIClaw: ClawButtonMonitor running (DInput path)");

                    // Apply current mouse sensitivity / threshold from persisted settings.
                    // Done here (after Start) so the values are ready before mouse mode
                    // is potentially activated below.
                    if (controllerEmulationManager != null)
                    {
                        int sens   = controllerEmulationManager.ControllerEmulationMouseSensitivity?.Value ?? 100;
                        int thresh = controllerEmulationManager.ControllerEmulationMouseThreshold?.Value ?? 15;
                        int leftBtn  = controllerEmulationManager.ControllerEmulationMouseLeftClickButton?.Value  ?? 6;
                        int rightBtn = controllerEmulationManager.ControllerEmulationMouseRightClickButton?.Value ?? 5;
                        int cursorStick = controllerEmulationManager.ControllerEmulationMouseCursorStick?.Value ?? 0;
                        int scrollStick = controllerEmulationManager.ControllerEmulationMouseScrollStick?.Value ?? 0;
                        monitor.SetMouseModeSensitivity(sens);
                        monitor.SetMouseModeThreshold(thresh);
                        monitor.SetMouseLeftClickButton(leftBtn);
                        monitor.SetMouseRightClickButton(rightBtn);
                        monitor.SetMouseCursorStick(cursorStick);
                        monitor.SetMouseScrollStick(scrollStick);
                        Logger.Info($"MSIClaw: Mouse mode settings applied — sensitivity={sens}, threshold={thresh}, leftBtn={leftBtn}, rightBtn={rightBtn}, cursorStick={cursorStick}, scrollStick={scrollStick}");
                    }

                    // Apply mouse mode immediately if requested (e.g. widget restored mouse mode on reconnect)
                    if (startInMouseMode)
                    {
                        monitor.SetMouseMode(true);
                        Logger.Info("MSIClaw: ClawButtonMonitor started in mouse mode");
                    }

                    // ── Step 2: HidHide — hide physical DInput controller ──────────
                    // PID_1902 is now enumerated (Start() completed the DInput mode switch).
                    // Hides PID 0x1902 only (configured in ControllerSuppressionManager).
                    // PID 0x1901 (command interface + keyboard HID) is NOT hidden so
                    // Win+G keeps working and Game Bar remains fully controllable.
                    var suppression = controllerEmulationManager?.SuppressionManager;
                    if (suppression != null)
                    {
                        try
                        {
                            _msiClawOwnsSuppression = suppression.Enable(
                                DeviceType.MSIClaw,
                                controllerEmulationManager?.HideTarget ?? 0);
                            Logger.Info($"[MSIClaw] HidHide suppression => {_msiClawOwnsSuppression}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"[MSIClaw] HidHide Enable threw: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"MSIClaw: ClawButtonMonitor startup threw: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Final shutdown: unsubscribes the emulation toggle event, stops ClawButtonMonitor,
        /// and disposes the mouse forwarder.
        /// Called ONLY during helper shutdown — NOT from the toggle handler.
        /// Full disposal of the ClawButtonMonitor instance is handled by DisposeClawButtonMonitor()
        /// in Program.Labs.cs which also nulls out the field.
        /// </summary>
        private static void DisposeMSIClawControllerEmulation()
        {
            // Remove event subscription (shutdown only — not called from toggle handler)
            try
            {
                if (controllerEmulationManager != null)
                    controllerEmulationManager.EmulationEnabledChanged -= OnMSIClawEmulationEnabledChanged;
            }
            catch { }

            StopMSIClawButtonMonitor();

            // Dispose mouse forwarder (releases stuck mouse buttons if active)
            try
            {
                lock (_msiClawMouseForwarderLock)
                {
                    _msiClawMouseForwarder?.Dispose();
                    _msiClawMouseForwarder = null;
                }
            }
            catch { }

            // Switch controller back to Desktop mode on shutdown —
            // mirrors HC ClawA1M.Close() → SwitchMode(GamepadMode.Desktop).
            // Ensures the controller is in a known, clean hardware state when
            // no companion app is running (MSI firmware handles basic cursor movement).
            try { MSIClawHidController.TrySwitchToDesktop(); } catch { }
        }

        /// <summary>
        /// Wire LegionManager.OnButtonMappingChanged to route M1/M2 gamepad button mappings
        /// (LS Click, A, B, etc.) to ClawButtonMonitor software remapping.
        ///
        /// Called once during StartMSIClawControllerEmulation(). The callback is a simple
        /// field assignment, not a C# event — no unsubscription needed on shutdown.
        ///
        /// Pre-wired before ClawButtonMonitor.Start() so any stored mapping loaded from
        /// profile restore (RestoreGlobalProfileSettings()) is applied immediately via
        /// LegionButtonM1Property.NotifyPropertyChanged() → SetButtonMappingAdvanced() →
        /// MSI Claw branch → OnButtonMappingChanged → ConfigureXInputRemap().
        /// </summary>
        /// <summary>
        /// Called when MSI Center M active state changes (poll-detected or user-toggled).
        ///
        /// active = true  → MSI Center M just started  → stop ClawButtonMonitor + mouse forwarder
        /// active = false → MSI Center M just stopped  → restart the appropriate emulation mode
        ///
        /// Wired in Program.cs after msiCenterManager is created.
        /// </summary>
        internal static void OnMsiCenterStateChanged(bool active)
        {
            var deviceInfo = Devices.DeviceDetector.DetectDevice();
            if (deviceInfo.DeviceType != DeviceType.MSIClaw) return;

            if (active)
            {
                // MSI Center M became active → shut down our controller emulation immediately
                Logger.Info("[Program.MSIClaw] MSI Center M active → stopping ClawButtonMonitor (mouse mode off)");
                lock (clawButtonMonitorLock)
                    clawButtonMonitor?.SetMouseMode(false);
                StopMSIClawButtonMonitor();
            }
            else
            {
                // MSI Center M deactivated → restart controller emulation if the master toggle is on
                Logger.Info("[Program.MSIClaw] MSI Center M deactivated → restarting controller emulation");
                bool controllerModeOn = msiClawControllerModeManager?.MsiClawControllerMode?.Value ?? true;
                bool emulationEnabled = controllerEmulationManager?.EmulationEnabled ?? true;

                if (emulationEnabled)
                    StartClawButtonMonitorBackground(startInMouseMode: !controllerModeOn);
            }
        }

        private static void WireClawXInputRemapCallback()
        {
            if (legionManager == null) return;
            legionManager.OnButtonMappingChanged = OnMSIClawButtonMappingChanged;
            legionManager.OnGamepadMappingChanged = OnMSIClawGamepadMappingChanged;
            Logger.Info("MSIClaw: LegionManager.OnButtonMappingChanged + OnGamepadMappingChanged wired → ClawButtonMonitor");
        }

        /// <summary>
        /// Invoked when the generic "Re-Map Specific Buttons" 24-button mapping changes (live edit,
        /// profile switch on game start/stop, or profile restore on startup). Routes the same JSON
        /// the widget persists to ClawButtonMonitor, which applies the gamepad-mode source→target
        /// swaps (e.g. A↔B) onto the outgoing ViGEm state. Keyboard/Mouse remap modes are ignored
        /// by ClawButtonMonitor.ConfigureGamepadSwaps().
        ///
        /// EnsureClawButtonMonitor() pre-configures the instance so the swaps are ready when the
        /// monitor Start()s, matching the M1/M2 ConfigureXInputRemap pre-config behaviour.
        /// </summary>
        private static void OnMSIClawGamepadMappingChanged(string json)
        {
            try
            {
                var monitor = EnsureClawButtonMonitor();
                // Keyboard-target swaps fire their chord through the same injector tiles/actions use.
                monitor.KeyboardChordCallback = SendKeyboardShortcutViaInputInjector;
                monitor.ConfigureGamepadSwaps(json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawGamepadMappingChanged threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Invoked when the user sets a button mapping in the Legion tab (M1/M2 → gamepad button).
        /// Routes the mapping to ClawButtonMonitor.ConfigureXInputRemap() for software remapping.
        ///
        /// Mirrors HC LayoutManager.MapController() path for OEM3/OEM4 buttons:
        ///   buttonIndex 3 = M1 (OEM3), buttonIndex 4 = M2 (OEM4).
        ///   mappingType 0 = Gamepad → values[0] is the RemapAction index.
        ///   mappingType != 0 = Keyboard/Mouse → no XInput remap; clears any prior action.
        ///
        /// EnsureClawButtonMonitor() pre-creates the monitor if not yet running so the remap
        /// is ready when Start() is called from StartClawButtonMonitorBackground().
        /// </summary>
        private static void OnMSIClawButtonMappingChanged(int buttonIndex, int mappingType, int[] values)
        {
            string button = buttonIndex == 3 ? "M1" : buttonIndex == 4 ? "M2" : null;
            if (button == null) return; // M3 and Y1/Y2/Y3 not applicable on MSI Claw

            // Only Gamepad (type=0) actions map to XInput; Keyboard/Mouse → clear XInput remap.
            int actionIndex = (mappingType == 0 && values?.Length > 0) ? values[0] : 0;

            try
            {
                // EnsureClawButtonMonitor() creates the instance if not yet present.
                // ConfigureXInputRemap() hot-applies if the monitor is running, or
                // pre-configures the instance so the remap is ready when Start() is called.
                EnsureClawButtonMonitor().ConfigureXInputRemap(button, actionIndex);
                Logger.Info($"MSIClaw: {button} XInput remap applied — actionIndex={actionIndex}, mappingType={mappingType}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawButtonMappingChanged threw: {ex.Message}");
            }
        }
    }
}
