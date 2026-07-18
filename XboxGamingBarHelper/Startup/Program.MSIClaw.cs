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
        /// Guards the one-time HW-controller baseline self-heal (emulation off→on cycle) performed at
        /// the first virtual-controller mount of the helper session. Once only — no retry loop.
        /// </summary>
        private static bool _clawHwBaselineHealAttempted = false;

        /// <summary>
        /// Counts how often <see cref="StartClawButtonMonitorBackground"/> has been invoked this helper
        /// session. Purely diagnostic — surfaces in the boot-timing log so we can see whether the mount
        /// runs once at boot or is re-triggered (e.g. widget toggle echo, MSI-Center restart). Each
        /// invocation = one full DInput-settle + (ViGEm/VIIPER) mount cycle, so a count > 1 at boot is
        /// the prime suspect for "the LED cycled several times".
        /// </summary>
        private static int _startClawMonitorInvocations;

        /// <summary>
        /// External Gamepad Mode runtime state (NOT persisted — always starts off after a helper
        /// start). When active, the handheld is parked as a hidden DInput device with no virtual
        /// controller, so only an external gamepad is visible. <see cref="_externalGamepadWasVirtual"/>
        /// remembers whether emulation (virtual ViGEm controller) was running before, so OFF can
        /// restore the right prior state.
        /// </summary>
        internal static bool ExternalGamepadModeActive => _externalGamepadModeActive;
        private static volatile bool _externalGamepadModeActive;
        private static bool _externalGamepadWasVirtual;
        private static readonly object _externalGamepadLock = new object();

        /// <summary>
        /// Experimental VIIPER backend selector (Debug menu). When true, the next
        /// controller-emulation activation mounts a VIIPER virtual pad instead of ViGEm
        /// (read by <see cref="StartClawButtonMonitorBackground"/>). Mirrors the helper's
        /// EmulationBackend property; the legacy ViiperEmulationManager skips the Claw, so
        /// this flag is the Claw's authoritative backend state.
        /// </summary>
        internal static bool ViiperBackendActive => _viiperBackendActive;
        private static volatile bool _viiperBackendActive;

        /// <summary>
        /// Software mouse forwarder for MSI Claw "Mouse mode".
        /// Active when controller emulation is disabled (mode tile shows "Mouse").
        /// Null when not running.
        /// </summary>
        private static MSIClawDesktopModeForwarder _msiClawMouseForwarder;
        private static readonly object _msiClawMouseForwarderLock = new object();

        /// <summary>
        /// HW-mouse killswitch INTENT. True only while OUR tile/action has forced the firmware into
        /// its native Desktop/mouse mode (SwitchMode(Desktop)). The auto-recovery watcher reads this
        /// to distinguish an intentional HW mouse (leave it) from an accidental one — long-press Start,
        /// booted-in-mouse-mode, etc. — which must be switched back to the controller. Defaults false,
        /// so every helper start begins in controller intent (Req: always controller mode at startup).
        /// </summary>
        internal static bool HwMouseIntended => _hwMouseIntended;
        private static volatile bool _hwMouseIntended;
        private static readonly object _hwMouseLock = new object();

        /// <summary>
        /// While an exit/re-establish is in flight, the watcher must NOT re-trigger — otherwise it fires
        /// again mid-transition and its SwitchMode fights the monitor's, so the firmware never settles and
        /// the controller churns/flickers. Set to a short deadline whenever ExitHwMouseKillswitch runs.
        /// </summary>
        private static long _hwMouseRecoverBlockUntilTicks;

        /// <summary>
        /// Watches the Claw mouse collection for an UNINTENDED firmware mouse (long-press Start,
        /// booted-in-mouse-mode) and switches back to the controller. Started once per session after
        /// BOOT COMPLETE (off the boot critical path). Never fires for our own intentional killswitch.
        /// </summary>
        private static Devices.MSIClaw.MsiClawHwMouseWatcher _hwMouseWatcher;
        private static readonly object _hwMouseWatcherLock = new object();

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
                case 43: ToggleClawTweaksWindow(); break; // OpenClawTweaksWindow — toggle the standalone app-mode window
                case 30: SendKeyboardShortcutViaInputInjector("MEDIA_NEXT_TRACK"); break;
                case 31: SendKeyboardShortcutViaInputInjector("MEDIA_PREV_TRACK"); break;
                case 32: SendKeyboardShortcutViaInputInjector("MEDIA_PLAY_PAUSE"); break;
                case 50: case 51: case 52: case 53: case 59: // Program Actions
                    LaunchProgramTarget(ResolveProgramTargetHelper(actionType, actionParam));
                    break;
                case 60: case 61: case 62: case 63: case 64: case 65: case 69: // Launch Website
                    LaunchUrl(ResolveWebsiteUrlHelper(actionType, actionParam));
                    break;
                case 74: Windows.User32.SendCtrlComboViaKeybdEvent(0x31); break; // Steam BPM: Steam menu (Ctrl+1)
                case 75: Windows.User32.SendCtrlComboViaKeybdEvent(0x32); break; // Steam BPM: Quick Access (Ctrl+2)
                case 76: SendKeyboardShortcutViaInputInjector("Shift+Tab"); break; // Steam in-game overlay (Shift+Tab, SendInput path)
                case 77: // Steam menu, context-sensitive: in-game → Shift+Tab overlay; otherwise → Steam BPM left (Ctrl+1)
                    if (systemManager?.RunningGame?.Value.IsValid() == true)
                    {
                        Logger.Info("MSIClaw: contextual Steam menu — game running → Shift+Tab (in-game overlay)");
                        SendKeyboardShortcutViaInputInjector("Shift+Tab");
                    }
                    else
                    {
                        Logger.Info("MSIClaw: contextual Steam menu — no game → Ctrl+1 (Steam BPM left)");
                        Windows.User32.SendCtrlComboViaKeybdEvent(0x31);
                    }
                    break;
                default:
                    // App actions that need widget state — try widget (may not work when suspended)
                    FireTileHotkeyToWidget($"__action__{actionType}", actionName);
                    break;
            }
        }

        // Fan persistence keys. Bumped to *_2 for the MSI-axis rework so the old (×1.5, wrong-axis)
        // 11-point curves and preset indices are ignored → existing user fan settings are wiped clean.
        private const string MsiFanValueKey = "MsiFan_Value2";
        private const string MsiFanCurveKey = "MsiFan_Curve2";

        /// <summary>
        /// Apply an MSI Claw fan command from the widget (MSI-axis software-curve model).
        /// value: 0 = MSI Default, 1 = Quiet Idle, 2 = Cooling / early ramp, 3 = Custom (saved curve);
        ///   -1 = disabled → firmware control. Persisted so it can be re-applied on startup (the EC
        /// resets across reboots). Pure MSI software curves, decoupled from TDP/power-shift
        /// (see Doku/PLAN_MSI_Exact_Fan_TDP.md).
        /// </summary>
        internal static void ApplyMsiFan(int value)
        {
            try
            {
                Logger.Info($"ApplyMsiFan: value={value}");
                Settings.LocalSettingsHelper.SetValue(MsiFanValueKey, value);

                if (value < 0)
                {
                    // Disabled → MSI-clean firmware hand-back: replicate MSI Center M's "Auto" state
                    // (MSI default curve on the real 0–100 axis, control OFF). This replaces the old
                    // legacy LLFanTable path whose wrong-axis bytes (incl. 150) could leave a fan not
                    // spinning after disabling our software control.
                    MsiClawFanController.ApplyFirmwareAutoBaseline();
                    return;
                }

                // Base temperature axis = the device's OWN firmware axis, read live from the EC
                // (model-agnostic: correct on A2VM and EX with no hardcoded per-model values). The presets
                // build on it; the user can still shift it. Falls back to MsiTemps_Default if the read fails.
                int[] baseAxis = MsiClawFanController.GetFirmwareTempAxis();

                if (value == 3) // custom curve stored as "t1,..,t5;d1,..,d5"
                {
                    if (Settings.LocalSettingsHelper.TryGetValue<string>(MsiFanCurveKey, out string csv)
                        && TryParseCurve(csv, out int[] ct, out int[] cd))
                    {
                        MsiClawFanController.ApplyMsiCurve(ct, cd);
                    }
                    else
                    {
                        Logger.Warn("ApplyMsiFan: custom selected but no valid saved curve — falling back to MSI Default");
                        MsiClawFanController.ApplyMsiCurve(baseAxis, MsiClawFanController.MsiDuty_Default);
                    }
                    return;
                }

                int[] temps, duties;
                switch (value)
                {
                    case 1: // Quiet Idle: firmware axis, quieter low band
                        temps = baseAxis; duties = MsiClawFanController.MsiDuty_QuietIdle; break;
                    case 2: // Cooling / early ramp: firmware axis shifted −10 °C
                        temps = new int[baseAxis.Length]; duties = MsiClawFanController.MsiDuty_Cooling;
                        for (int i = 0; i < baseAxis.Length; i++) temps[i] = System.Math.Max(0, baseAxis[i] - 10);
                        break;
                    default: // 0 = MSI Default (firmware axis + MSI default duty)
                        temps = baseAxis; duties = MsiClawFanController.MsiDuty_Default; break;
                }
                MsiClawFanController.ApplyMsiCurve(temps, duties);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ApplyMsiFan({value}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Read the current EC fan table + control bit and push it back to the widget as
        /// "MsiFanStatus":"b0,..,b7|controlBit|readOk|fullSpeedBit|rpm" so the widget can verify against
        /// the graph AND compare our table max (=150) with the EC's true full-speed ceiling. rpm = -1
        /// when no fan sensor is available.
        /// </summary>
        internal static void ReportMsiFanStatus()
        {
            try
            {
                bool ok = MsiClawFanController.ReadStatus(out byte[] table, out bool controlOn);
                bool fullSpeed = MsiClawFanController.ReadFullSpeedBit();
                int rpm = -1;
                try { rpm = legionManager?.GetCpuFanSpeed() ?? -1; } catch { }

                // Temperature axis (Set_Thermal) read-back so the widget can verify the X-axis too.
                string thermalCsv = "";
                try
                {
                    byte[] th = MsiClawFanController.ReadThermal();
                    if (th != null && th.Length >= 7) thermalCsv = string.Join(",", th);
                }
                catch { }

                string csv = string.Join(",", table);
                string payload = $"{csv}|{(controlOn ? 1 : 0)}|{(ok ? 1 : 0)}|{(fullSpeed ? 1 : 0)}|{rpm}|{thermalCsv}";
                string json = $"{{\"MsiFanStatus\":\"{payload}\"}}";
                if (pipeServer != null && pipeServer.IsConnected)
                {
                    pipeServer.SendMessage(json);
                    Logger.Info($"ReportMsiFanStatus: sent '{payload}'");
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

        /// <summary>Force the EC full-speed override (block 152.7) on/off for the diagnostic max-fan test,
        /// then push the refreshed status (incl. RPM) back to the widget.</summary>
        internal static void SetMsiFanFullBlast(bool enable)
        {
            try
            {
                Logger.Info($"SetMsiFanFullBlast: {enable}");
                MsiClawFanController.SetFanFullSpeed(enable);
                ReportMsiFanStatus();
            }
            catch (Exception ex)
            {
                Logger.Warn($"SetMsiFanFullBlast({enable}) failed: {ex.Message}");
            }
        }

        /// <summary>Diagnostic fan-override probe: write a raw byte to an EC data block and read it back,
        /// to hunt for a PROPORTIONAL fan-duty register (the full-speed bit 152.7 proves a direct override
        /// exists; this looks for a level version). Pushes "MsiFanRegStatus":"block|wrote|readback|rpm".</summary>
        internal static void ProbeFanRegister(int block, int value)
        {
            try
            {
                if (block < 0 || block > 255 || value < 0 || value > 255)
                {
                    Logger.Warn($"ProbeFanRegister: out of range block={block} value={value}");
                    return;
                }
                MsiClawFanController.WriteDataBlock((byte)block, (byte)value);
                int readback = MsiClawFanController.ReadDataBlock((byte)block);
                int rpm = -1;
                try { rpm = legionManager?.GetCpuFanSpeed() ?? -1; } catch { }

                Logger.Info($"ProbeFanRegister: block={block} wrote={value} readback={readback} rpm={rpm}");
                if (pipeServer != null && pipeServer.IsConnected)
                    pipeServer.SendMessage($"{{\"MsiFanRegStatus\":\"{block}|{value}|{readback}|{rpm}\"}}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"ProbeFanRegister({block},{value}) failed: {ex.Message}");
            }
        }

        /// <summary>EXPERIMENTAL controller-HID probe: send an arbitrary vendor frame (hex string like
        /// "0F 00 00 3C 26"), optionally read the response, and push "ClawHidProbeResult". This is the
        /// harness for reverse-engineering the button-remap / config opcodes the same way the LED
        /// protocol was cracked (send + observe / read-back).</summary>
        internal static void ProbeClawHid(string hexFrame, bool read)
        {
            try
            {
                byte[] frame = ParseHexBytes(hexFrame);
                string result = (frame == null || frame.Length == 0)
                    ? "ERR: no valid hex bytes"
                    : Devices.MSIClaw.MSIClawHidController.SendRawFrameHex(frame, read);
                Logger.Info($"ProbeClawHid(read={read}) '{hexFrame}' -> {result}");
                PushProbeResult("ClawHidProbeResult", result);
            }
            catch (Exception ex) { Logger.Warn($"ProbeClawHid failed: {ex.Message}"); }
        }

        /// <summary>EXPERIMENTAL: read the native MSI fan config straight from the EC and push
        /// "MsiFanDetectResult". Set a fan mode in MSI Center M, then call this to learn its EC
        /// signature.</summary>
        internal static void DetectMsiFan()
        {
            try
            {
                string result = MsiClawFanController.DetectNativeReport();
                Logger.Info($"DetectMsiFan -> {result}");
                PushProbeResult("MsiFanDetectResult", result);
            }
            catch (Exception ex) { Logger.Warn($"DetectMsiFan failed: {ex.Message}"); }
        }

        /// <summary>Reads the live firmware button→keyboard map from the controller and pushes
        /// "FwButtonMapResult" for the Controller-status card to display (A2VM only).</summary>
        internal static void ReadMsiClawFwButtonMap()
        {
            try
            {
                string report = EnsureClawButtonMonitor().BuildFirmwareButtonMapReport();
                Logger.Info($"ReadMsiClawFwButtonMap ->\n{report}");
                PushProbeResult("FwButtonMapResult", report);
            }
            catch (Exception ex) { Logger.Warn($"ReadMsiClawFwButtonMap failed: {ex.Message}"); }
        }

        private static void PushProbeResult(string key, string value)
        {
            if (pipeServer != null && pipeServer.IsConnected)
                pipeServer.SendMessage("{\"" + key + "\":\"" + JsonEscape(value) + "\"}");
        }

        private static string JsonEscape(string s)
            => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");

        private static byte[] ParseHexBytes(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var tokens = s.Replace("0x", "").Replace("0X", "")
                          .Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<byte>();
            foreach (var t in tokens)
            {
                if (byte.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out byte b)) list.Add(b);
                else return null;
            }
            return list.ToArray();
        }

        /// <summary>
        /// Push the Intel thermal stack (IPF/DTT) status to the widget as
        /// "IntelThermalStatus":"&lt;state&gt;|&lt;detail&gt;" so the experimental Fan-tab controls can show
        /// whether the Intel tasks are currently running. See <see cref="MSI.IntelThermalControl"/>.
        /// </summary>
        internal static void ReportIntelThermalStatus()
        {
            PushIntelThermalStatus(MSI.IntelThermalControl.GetStatusPayload());
        }

        /// <summary>Stop the Intel thermal stack (test mode), then push the resulting status.</summary>
        internal static void StopIntelThermal()
        {
            PushIntelThermalStatus(MSI.IntelThermalControl.Stop());
        }

        /// <summary>Restore the Intel thermal stack, then push the resulting status.</summary>
        internal static void StartIntelThermal()
        {
            PushIntelThermalStatus(MSI.IntelThermalControl.Start());
        }

        private static void PushIntelThermalStatus(string payload)
        {
            try
            {
                if (pipeServer == null || !pipeServer.IsConnected) { Logger.Warn("PushIntelThermalStatus: pipe not connected"); return; }
                string escaped = (payload ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
                pipeServer.SendMessage($"{{\"IntelThermalStatus\":\"{escaped}\"}}");
                Logger.Info($"PushIntelThermalStatus: sent '{payload}'");
            }
            catch (Exception ex)
            {
                Logger.Warn($"PushIntelThermalStatus failed: {ex.Message}");
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
                int value = Settings.LocalSettingsHelper.TryGetValue<int>(MsiFanValueKey, out int v) ? v : -1;
                string curve = (value == 3 && Settings.LocalSettingsHelper.TryGetValue<string>(MsiFanCurveKey, out string c)) ? c : "";
                string json = $"{{\"MsiFanState\":\"{value}|{curve}\"}}";
                pipeServer.SendMessage(json);
                Logger.Info($"PushMsiFanStateToWidget: sent value={value} curve='{curve}'");
            }
            catch (Exception ex)
            {
                Logger.Warn($"PushMsiFanStateToWidget failed: {ex.Message}");
            }
        }

        /// <summary>Apply and persist a custom MSI fan curve sent as "t1,..,t5;d1,..,d5" (temps °C ;
        /// duty %). Sets the fan value to 3 (custom).</summary>
        internal static void ApplyMsiFanCurve(string csv)
        {
            try
            {
                Logger.Info($"ApplyMsiFanCurve: csv='{csv}'");
                if (!TryParseCurve(csv, out int[] temps, out int[] duties))
                {
                    Logger.Warn("ApplyMsiFanCurve: could not parse curve");
                    return;
                }
                Settings.LocalSettingsHelper.SetValue(MsiFanCurveKey, csv);
                Settings.LocalSettingsHelper.SetValue(MsiFanValueKey, 3); // 3 = custom
                MsiClawFanController.ApplyMsiCurve(temps, duties);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ApplyMsiFanCurve failed: {ex.Message}");
            }
        }

        /// <summary>Parses a custom MSI fan curve "t1,..,t5;d1,..,d5" into 5 temps (°C) + 5 duties (%).
        /// Temps clamped 0–120 and forced strictly increasing; duties clamped 0–100.</summary>
        private static bool TryParseCurve(string csv, out int[] temps, out int[] duties)
        {
            temps = null; duties = null;
            if (string.IsNullOrWhiteSpace(csv)) return false;
            var halves = csv.Split(';');
            if (halves.Length != 2) return false;
            var tp = halves[0].Split(',');
            var dp = halves[1].Split(',');
            if (tp.Length != 5 || dp.Length != 5) return false;

            var t = new int[5];
            var d = new int[5];
            for (int i = 0; i < 5; i++)
            {
                if (!int.TryParse(tp[i], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out int tv)) return false;
                if (!int.TryParse(dp[i], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out int dv)) return false;
                t[i] = Math.Max(0, Math.Min(120, tv));
                d[i] = Math.Max(0, Math.Min(100, dv));
            }
            // Force strictly increasing temps so the EC axis is monotonic.
            for (int i = 1; i < 5; i++)
                if (t[i] <= t[i - 1]) t[i] = t[i - 1] + 1;

            temps = t; duties = d;
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
                    // IntelFpsTier now carries a real fps — cycle the same ascending set as RTSS.
                    int[] vals = { 0, 30, 40, 60, 90, 120 };
                    int cur = XboxGamingBarHelper.Intel.IntelGpuManager.MigrateTierToFps(tier.Value), idx = 0;
                    for (int i = 0; i < vals.Length; i++) if (vals[i] == cur) { idx = i; break; }
                    int next = vals[(idx + 1) % vals.Length];
                    tier.SetValue(next);
                    Logger.Info($"FpsLimiter hotkey (Intel) → {next}");
                    rtssManager?.ShowNotification($"FPS Limit: {(next > 0 ? next + " FPS" : "Off")}", 4000);
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
                if (Settings.LocalSettingsHelper.TryGetValue<int>(MsiFanValueKey, out int value) && value >= 0)
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

        // (Removed 2026-07-17: the reactive auto-Sport latch guard — EnableMsiFanAutoSport /
        //  MsiFanAutoSportTick / EngageAutoSport / RestoreFanAfterGame and the EC-Sport power-shift
        //  coupling. We now drive TDP + fan MSI-exactly and fully decoupled: the power-shift is fixed
        //  per mode and the fan is an independent Set_Fan/212 curve. The IPF/TFN1 platform latch is a
        //  platform bug MSI suffers too — accepted residual risk, see Doku/PLAN_MSI_Exact_Fan_TDP.md.)

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

                // Custom fan curve is a per-model capability (off on the Claw 8 EX for now). Only touch
                // the EC fan on fan-capable models — otherwise leave the fan entirely to the firmware.
                if (deviceInfo.SupportsFanControl)
                {
                    // Re-apply any saved custom fan curve (EC resets across reboots).
                    RestoreMsiFanOnStartup();
                    // (Removed 2026-07-17: the reactive auto-Sport latch guard. We now drive TDP + fan
                    //  MSI-exactly and decoupled — see Doku/PLAN_MSI_Exact_Fan_TDP.md.)
                }
                else
                {
                    Logger.Info("MSIClaw: fan control not supported on this model — leaving fan to firmware (no curve restore / auto-sport)");
                }

                // Re-apply the saved battery charge limit if it was enabled (EC can reset on reboot).
                RestoreMsiChargeLimitOnStartup();

                // One-shot diagnostic: log which fan/RPM sensors LHM can see on this device.
                ProbeMsiFanSensors();

                // ── Step 0: Register MsiClawControllerMode callback ──────────────
                // MsiClawControllerModeManager.OnModeChanged is a static delegate.
                // Registering here (not at field-init time) ensures Program.MSIClaw.cs
                // is the authoritative owner; any previous value is safely overwritten.
                MsiClawControllerModeManager.OnModeChanged = OnMsiClawControllerModeChanged;

                // Firmware keyboard-remap backend toggle → switch ClawButtonMonitor between the
                // software injector (default) and writing button-bound keyboard shortcuts to the
                // controller firmware (A2VM only; SetFirmwareKeyboardMode ignores non-capable devices).
                MSI.MsiClawFwKeyboardModeManager.OnFwKeyboardModeChanged = OnMsiClawFwKeyboardModeChanged;
                // Re-assert the PERSISTED backend on start: the property loaded its value from
                // LocalSettingsHelper, but the callback only fires on live changes — without this a
                // reboot leaves the firmware backend inactive even though the toggle reads ON. No-op
                // on non-A2VM (SetFirmwareKeyboardMode ignores non-capable devices).
                try { OnMsiClawFwKeyboardModeChanged(msiClawFwKeyboardModeManager.MsiClawFwKeyboardMode.Value); }
                catch (Exception ex) { Logger.Warn($"MSIClaw: applying persisted FW keyboard mode failed: {ex.Message}"); }

                // External Gamepad Mode tile → hide/restore all handheld controllers.
                MSI.ExternalGamepadModeManager.OnChanged = OnExternalGamepadModeChanged;

                // HW-mouse killswitch tile/action → force firmware Desktop mouse mode / restore controller.
                MSI.MsiClawHwMouseManager.OnChanged = OnMsiClawHwMouseChanged;

                // Experimental VIIPER backend toggle (Debug menu) → always deactivate
                // controller emulation so the Claw HW controller is restored. The next
                // manual re-enable mounts VIIPER instead of ViGEm. Seed the flag from the
                // persisted value and react to runtime changes.
                if (settingsManager?.EmulationBackend != null)
                {
                    _viiperBackendActive = settingsManager.EmulationBackend.Value;
                    settingsManager.EmulationBackend.PropertyChanged += (s, e) =>
                        OnEmulationBackendChanged(settingsManager.EmulationBackend.Value);
                    Logger.Info($"MSIClaw: EmulationBackend seeded → {(_viiperBackendActive ? "VIIPER" : "ViGEm (Legacy)")}");
                }

                // VIIPER virtual device type (Xbox 360 / DS4 / DSE / Elite2-Steam / Switch Pro)
                // and its Steam sub-device: hot-swap the live VIIPER device when the user changes
                // the selection while the VIIPER backend is active.
                if (settingsManager?.ViiperDeviceType != null)
                    settingsManager.ViiperDeviceType.PropertyChanged += (s, e) => OnViiperDeviceTargetChanged();
                if (settingsManager?.ViiperSteamSubDevice != null)
                    settingsManager.ViiperSteamSubDevice.PropertyChanged += (s, e) => OnViiperDeviceTargetChanged();

                // Game Bar open → auto-hop the widget bar to ClawTweaks via RB taps on the
                // virtual controller (ClawTweaks sits at slot 3+ behind Microsoft's two widgets).
                WireGameBarAutoNav();

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
                    controllerEmulationManager.OnMouseAccelerationChanged = a =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseModeAcceleration(a);
                    };
                    controllerEmulationManager.OnMouseActionSlotsChanged = v =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseActionSlots(v);
                    };
                    controllerEmulationManager.OnMouseDPadActionsChanged = v =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseDPadActions(v);
                    };
                    controllerEmulationManager.OnMouseNudgeStepChanged = v =>
                    {
                        lock (clawButtonMonitorLock)
                            clawButtonMonitor?.SetMouseModeNudgeStep(v);
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
                    controllerEmulationManager.EmulationEnabledChanged += OnMSIClawMasterEmulationToggled;
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
        /// Called when the MASTER "Enable Controller Emulation" toggle (Controller tab) changes.
        ///
        /// This is the TRUE on/off for the virtual controller — distinct from the Controller/Mouse
        /// mode tile (OnMsiClawControllerModeChanged), which only switches input translation while
        /// keeping ViGEm mounted + HidHide active:
        ///   true  → start ClawButtonMonitor, honouring the current Controller/Mouse mode
        ///   false → StopMSIClawButtonMonitor(): tear down the ViGEm virtual controller and
        ///           ForceUnhideAll() so the 3 physical MSI Claw controllers reappear via HidHide.
        ///
        /// Reuses the existing StopMSIClawButtonMonitor / StartClawButtonMonitorBackground paths
        /// unchanged. Never touches MSI Center. The mode tile + MSI Center keep using
        /// OnMSIClawEmulationEnabledChanged / OnMsiCenterStateChanged respectively.
        /// </summary>
        private static void OnMSIClawMasterEmulationToggled(bool emulationEnabled)
        {
            try
            {
                // Changing the STANDARD mode supersedes any active per-game exception override.
                _perGameEffectiveVirtual = null;
                _hwExceptionSwapActive = false;
                if (emulationEnabled)
                {
                    lock (clawButtonMonitorLock)
                    {
                        if (clawButtonMonitor != null && clawButtonMonitor.IsRunning)
                        {
                            Logger.Info("MSIClaw: Master emulation toggle → ON — ClawButtonMonitor already running, no-op");
                            return;
                        }
                    }

                    bool controllerModeOn = msiClawControllerModeManager?.MsiClawControllerMode?.Value ?? true;
                    Logger.Info($"MSIClaw: Master emulation toggle → ON — starting ClawButtonMonitor (mouseMode={!controllerModeOn})");
                    StartClawButtonMonitorBackground(startInMouseMode: !controllerModeOn);
                    // Virtual mode now owns keyboard remaps in software (unless the FW toggle opted in) —
                    // recompute the effective firmware state (clears the HW-mode firmware slots).
                    ApplyEffectiveFirmwareKeyboardMode();
                }
                else
                {
                    Logger.Info("MSIClaw: Master emulation toggle → OFF (Hardware Controller) — stopping ViGEm + restoring physical controllers (HidHide ForceUnhideAll)");
                    StopMSIClawButtonMonitor();
                    // Hardware mode: firmware becomes the only remap path — force firmware keyboard on and
                    // apply the current profile's button-bound shortcuts via the firmware-only channel.
                    ApplyEffectiveFirmwareKeyboardMode();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawMasterEmulationToggled threw: {ex.Message}");
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
        /// Firmware keyboard-remap backend toggle. Routes to the (possibly not-yet-created)
        /// ClawButtonMonitor; SetFirmwareKeyboardMode is a no-op on non-A2VM devices and, when
        /// enabling, re-applies the current profile's button-bound keyboard map to the firmware,
        /// when disabling clears every firmware slot back to default.
        /// </summary>
        private static void OnMsiClawFwKeyboardModeChanged(bool firmwareOn)
        {
            Logger.Info($"MSIClaw: MsiClawFwKeyboardMode toggle → {(firmwareOn ? "Firmware" : "Software")}");
            ApplyEffectiveFirmwareKeyboardMode();
        }

        /// <summary>
        /// Computes and applies the EFFECTIVE firmware keyboard-remap state.
        ///   • Hardware Controller mode (virtual pad off): firmware keyboard is ALWAYS on — it is the
        ///     only way button-bound keyboard shortcuts can fire without the virtual pad. The firmware
        ///     command interface (PID_1901/0xFFA0) is present even with no monitor running, so the
        ///     firmware-only channel picks it up on demand (see ClawButtonMonitor.FlushFirmwareKeyboardMap).
        ///   • Virtual Controller mode: honour the separate "FW Keyboard Mode" toggle (legacy behaviour;
        ///     software injector by default, firmware if the user opted in).
        /// Called on boot, on standard-mode change, and on the FW-keyboard toggle change. No-op on
        /// non-A2VM (SetFirmwareKeyboardMode ignores non-capable devices).
        /// </summary>
        private static void ApplyEffectiveFirmwareKeyboardMode()
        {
            try
            {
                // Effective controller mode = the standard mode, unless a per-game exception is overriding
                // it right now (see _perGameEffectiveVirtual, set by the HW/Virtual exception swap).
                bool virtualMode = _perGameEffectiveVirtual ?? (controllerEmulationManager?.EmulationEnabled ?? false);
                bool toggleOn = msiClawFwKeyboardModeManager?.MsiClawFwKeyboardMode?.Value ?? false;
                bool effective = !virtualMode || toggleOn;   // HW mode ⇒ always firmware
                Logger.Info($"MSIClaw: effective FW keyboard mode = {effective} (virtualMode={virtualMode}, toggle={toggleOn})");
                var monitor = EnsureClawButtonMonitor();
                monitor.SetFirmwareKeyboardMode(effective);
                // Gamepad→gamepad firmware remaps only in Hardware mode (virtual mode uses software swaps).
                // Set this from the mode (not _running) so a switch re-flushes deterministically — this is
                // what clears the gamepad firmware remaps when returning to Virtual.
                monitor.SetFirmwareGamepadEnabled(!virtualMode);
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: ApplyEffectiveFirmwareKeyboardMode threw: {ex.Message}");
            }
        }

        // ── HW-mouse killswitch (firmware Desktop mouse mode) ────────────────────────────────
        //
        // Distinct from the software Controller/Mouse mode above: this forces the CLAW FIRMWARE into
        // its native Desktop mouse mode (stick→cursor, A→click) — a real hardware HID mouse that works
        // on the UAC secure desktop, where software SendInput cannot. It is transient and orthogonal:
        // the virtual controller state is untouched (Viiper stays mounted, monitor only suspended), so
        // turning it off restores the exact prior state. Only OUR tile/action may turn it on; anything
        // else that lands the firmware in mouse mode is treated as accidental and switched back.

        /// <summary>
        /// HW-mouse killswitch ON. Forces the firmware Desktop mouse mode without disturbing the
        /// virtual controller. No-op when the virtual controller isn't running (nothing to suspend).
        /// </summary>
        /// <summary>OnChanged callback from the killswitch tile/action. true = ON, false = OFF.</summary>
        private static void OnMsiClawHwMouseChanged(bool on)
        {
            Logger.Info($"MSIClaw: HW-mouse killswitch tile/action → {(on ? "ON" : "OFF")}");
            if (on) EnterHwMouseKillswitch();
            else    ExitHwMouseKillswitch();
        }

        internal static void EnterHwMouseKillswitch()
        {
            Labs.ClawButtonMonitor monitor;
            lock (clawButtonMonitorLock) monitor = clawButtonMonitor;
            if (monitor == null || !monitor.IsRunning)
            {
                Logger.Info("MSIClaw: HW-mouse killswitch ON requested but ClawButtonMonitor not running — ignoring");
                // Correct the tile back to OFF: there is no running controller to switch.
                msiClawHwMouseManager?.SetStateFromHelper(false);
                return;
            }
            bool ok;
            lock (_hwMouseLock)
            {
                _hwMouseIntended = true;
                ok = monitor.EnterHwMouseMode();
                if (!ok) _hwMouseIntended = false; // switch didn't land → stay in controller mode
            }
            if (!ok)
            {
                msiClawHwMouseManager?.SetStateFromHelper(false); // correct the tile back to OFF
                Logger.Warn("MSIClaw: HW-mouse killswitch ON failed — SwitchMode(Desktop) did not land; staying in controller");
                return;
            }
            msiClawHwMouseManager?.SetStateFromHelper(true);
            // Strong, always-visible cue (Game Bar may be closed): the controller is paused while active.
            rtssManager?.ShowNotification("HW Mouse ON — controller paused. Click the HW Mouse tile or press the keyboard hotkey to return.", 6000);
            Logger.Info("MSIClaw: HW-mouse killswitch ON — firmware Desktop mouse; virtual controller suspended (state preserved)");
        }

        /// <summary>
        /// HW-mouse killswitch OFF (tile/action or auto-recovery). Breaks the firmware mouse mode and
        /// re-establishes the controller. Clears the intent flag so the watcher stops treating mouse
        /// reports as ours.
        /// </summary>
        internal static void ExitHwMouseKillswitch()
        {
            // Block the watcher from re-triggering during the ~5 s re-establish (else its SwitchMode
            // fights the monitor's and the controller churns/flickers). Generous window covers the worst
            // case (XInput→settle→DInput→settle); if the mouse is still active afterwards it retries.
            _hwMouseRecoverBlockUntilTicks = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(9).Ticks;

            Labs.ClawButtonMonitor monitor;
            lock (clawButtonMonitorLock) monitor = clawButtonMonitor;
            // Restore EXACTLY the software mode that was active before the killswitch (virtual Controller
            // OR virtual Mouse), read from the authoritative Controller/Mouse tile — not always Controller.
            bool controllerModeOn = msiClawControllerModeManager?.MsiClawControllerMode?.Value ?? true;
            lock (_hwMouseLock)
            {
                _hwMouseIntended = false;
                monitor?.ExitHwMouseMode();
                // Re-assert the pre-killswitch software mode on the resumed monitor (the volatile flag is
                // read lazily by the poll loop after it re-acquires DInput, so setting it now is safe).
                monitor?.SetMouseMode(!controllerModeOn);
            }
            msiClawHwMouseManager?.SetStateFromHelper(false);
            rtssManager?.ShowNotification(controllerModeOn ? "Controller restored" : "Virtual mouse restored", 3000);
            Logger.Info($"MSIClaw: HW-mouse killswitch OFF — restored {(controllerModeOn ? "controller" : "virtual mouse")} mode (mouseMode={!controllerModeOn})");
        }

        /// <summary>
        /// Called by MsiClawHwMouseWatcher when the Claw's own mouse collection emits reports. If this
        /// is NOT our intentional killswitch and a virtual controller is actually running, the firmware
        /// mouse was triggered by something else (long-press Start, booted-in-mouse-mode) → switch back.
        /// </summary>
        /// <summary>True while the ClawButtonMonitor (virtual controller) is running — gates the HW-mouse poll.</summary>
        private static bool IsClawMonitorRunning()
        {
            lock (clawButtonMonitorLock) return clawButtonMonitor?.IsRunning == true;
        }

        internal static void RecoverFromUnintendedHwMouse()
        {
            if (_hwMouseIntended) return; // our own killswitch mouse → leave it

            // A recovery/re-establish is already in flight → don't re-trigger (would fight the monitor's
            // in-progress SwitchMode and cause the controller to churn/flicker).
            if (DateTime.UtcNow.Ticks < _hwMouseRecoverBlockUntilTicks) return;

            Labs.ClawButtonMonitor monitor;
            lock (clawButtonMonitorLock) monitor = clawButtonMonitor;
            if (monitor == null || !monitor.IsRunning) return; // no virtual controller expected → leave it

            Logger.Warn("MSIClaw: unintended HW mouse detected (Claw mouse reports without killswitch) → auto-recovering to controller");
            ExitHwMouseKillswitch();
        }

        // ── Game Bar auto-navigation (RB hop to ClawTweaks on open) ──────────────────────────
        //
        // ClawTweaks sits at widget-bar slot 3+ because Microsoft occupies the first two widgets,
        // so the user must press RB at least twice to reach us. We piggy-back on the widget's
        // existing foreground signal (settingsManager.IsForeground, Function.Foreground) — it goes
        // true the moment the Game Bar overlay comes foreground, even when another widget is shown.
        // On that false→true edge we inject RB taps on the virtual controller to hop to ClawTweaks.
        // Hardcoded for now: 2 taps, fixed timing. Only effective while controller emulation is
        // active (a virtual controller must exist for the taps to land).
        private static bool _gameBarAutoNavWired;
        private static bool _lastGameBarForeground;
        private static DateTime _lastRbNavTime = DateTime.MinValue;
        // RB hops to reach ClawTweaks = widget position − 1. Set from the widget's
        // GameBarWidgetPosition (Right MSI Button card); defaults to 0 (position 1 = auto-jump off)
        // until synced, so nothing is injected unless the user opts in by raising the position.
        private static int GameBarAutoNavRbCount = 0;
        private const int GameBarAutoNavDelayMs = 40;       // let Game Bar finish opening before hopping
        private const int GameBarAutoNavDebounceMs = 1500;  // ignore rapid re-opens

        private static void WireGameBarAutoNav()
        {
            try
            {
                if (_gameBarAutoNavWired) return;
                var sm = settingsManager;
                if (sm?.IsForeground == null)
                {
                    Logger.Warn("[GameBarAutoNav] settingsManager.IsForeground unavailable — not wired");
                    return;
                }
                _lastGameBarForeground = sm.IsForeground.Value;
                sm.IsForeground.PropertyChanged += OnGameBarForegroundChangedForAutoNav;
                _gameBarAutoNavWired = true;
                Logger.Info("[GameBarAutoNav] wired to widget foreground signal (RB auto-hop on Game Bar open)");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[GameBarAutoNav] wiring threw: {ex.Message}");
            }
        }

        private static void OnGameBarForegroundChangedForAutoNav(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                bool nowForeground = settingsManager?.IsForeground?.Value ?? false;
                bool wasForeground = _lastGameBarForeground;
                _lastGameBarForeground = nowForeground;

                // Experimental opt-in (independent of RB auto-nav): swap a non-Xbox VIIPER device
                // to xbox360 while the Game Bar is open so the overlay navigates cleanly, and back
                // on close. Runs on BOTH edges, so it must come before the rising-edge-only return.
                if (nowForeground != wasForeground)
                    HandleViiperGameBarAutoXboxSwap(nowForeground);

                // Only act on the rising edge (Game Bar just opened).
                if (!(nowForeground && !wasForeground)) return;

                // Release any latched Macro Hold-toggle (M1/M2 MacroMode=2) unconditionally — a
                // held virtual button (e.g. RB) would otherwise fight the Game Bar's own controller
                // navigation, making the overlay unusable. Independent of the RB auto-nav setting
                // below, so this always runs on Game Bar open regardless of that preference.
                {
                    Labs.ClawButtonMonitor holdMonitor;
                    lock (clawButtonMonitorLock) holdMonitor = clawButtonMonitor;
                    holdMonitor?.ReleaseActiveMacroHolds();
                }

                // Position 1 (RB hops = 0) means auto-jump OFF — do nothing at all, not even the
                // D-pad-down focus drop (InjectGameBarNav always queues a DpadDown, even for rbCount 0,
                // so we must stop short of calling it). This is the default state.
                if (GameBarAutoNavRbCount <= 0)
                {
                    Logger.Debug("[GameBarAutoNav] widget position = 1 (auto-jump off) — skipping (no RB hops, no focus drop)");
                    return;
                }

                var t = DateTime.Now;
                if ((t - _lastRbNavTime).TotalMilliseconds < GameBarAutoNavDebounceMs)
                {
                    Logger.Debug("[GameBarAutoNav] debounced (rapid re-open)");
                    return;
                }
                _lastRbNavTime = t;

                Labs.ClawButtonMonitor monitor;
                lock (clawButtonMonitorLock) monitor = clawButtonMonitor;
                bool running = monitor != null && monitor.IsRunning;
                Logger.Info($"[GameBarAutoNav] Game Bar opened — emulationActive={running} → planning RB×{GameBarAutoNavRbCount}");

                if (!running)
                {
                    Logger.Info("[GameBarAutoNav] skip: controller emulation not active (no virtual controller to hop with)");
                    return;
                }

                // Small delay so the overlay is fully up before we hop the widget bar.
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(GameBarAutoNavDelayMs);
                        monitor.InjectGameBarNav(GameBarAutoNavRbCount);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[GameBarAutoNav] inject threw: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"[GameBarAutoNav] handler threw: {ex.Message}");
            }
        }

        // True while the Game-Bar-open Xbox swap is in effect, so the matching close-swap restores
        // the user's device exactly once (and rapid foreground flicker doesn't thrash the USB bus).
        private static volatile bool _viiperGameBarXboxSwapActive;

        /// <summary>
        /// Experimental opt-in: when a NON-Xbox VIIPER device is emulated, hot-swap it to xbox360
        /// while the Xbox Game Bar is open so the overlay navigates cleanly (some non-Xbox pads spam
        /// the right trigger to the Game Bar), then swap back to the user's selected type on close.
        /// Off by default. Only fires when controller emulation is active, External Gamepad Mode is
        /// OFF, and a non-xbox360 device is actually mounted. The USBIP swap (~1–2 s) runs off-thread.
        /// </summary>
        private static void HandleViiperGameBarAutoXboxSwap(bool gameBarOpening)
        {
            try
            {
                if (settingsManager?.ViiperGameBarAutoXboxSwap?.Value != true) return; // opt-in only
                if (!_viiperBackendActive) return;

                Labs.ClawButtonMonitor monitor;
                lock (clawButtonMonitorLock) monitor = clawButtonMonitor;
                if (monitor == null) return;

                if (gameBarOpening)
                {
                    if (_viiperGameBarXboxSwapActive) return; // already swapped for this open

                    if (!monitor.IsRunning)
                    {
                        Logger.Info("[ViiperGBSwap] skip open: controller emulation not active");
                        return;
                    }
                    bool externalActive;
                    lock (_externalGamepadLock) externalActive = _externalGamepadModeActive;
                    if (externalActive)
                    {
                        Logger.Info("[ViiperGBSwap] skip open: External Gamepad Mode active");
                        return;
                    }
                    // Gate on the user's SELECTED type as well as the mounted one. If the user picked
                    // xbox360 (or nothing), never engage — this also covers the race where a just-
                    // selected xbox360 hasn't finished its hot-swap yet, so the Game Bar must not swap
                    // an Xbox 360 controller.
                    string selected = settingsManager?.ViiperDeviceType?.Value;
                    string active = monitor.ViiperActiveDeviceType;
                    if (string.IsNullOrEmpty(selected) || selected == "xbox360"
                        || !monitor.ViiperMounted || string.IsNullOrEmpty(active) || active == "xbox360")
                    {
                        Logger.Info($"[ViiperGBSwap] skip open: Xbox 360 selected/mounted (selected={selected ?? "null"}, mounted={monitor.ViiperMounted}, type={active ?? "null"})");
                        return;
                    }

                    _viiperGameBarXboxSwapActive = true;
                    Logger.Info($"[ViiperGBSwap] Game Bar opened — swapping {active} -> xbox360 for clean overlay navigation");
                    Task.Run(() =>
                    {
                        try { monitor.SwitchViiperDeviceType("xbox360", 0, 0); }
                        catch (Exception ex) { Logger.Warn($"[ViiperGBSwap] open swap threw: {ex.Message}"); }
                    });
                }
                else
                {
                    if (!_viiperGameBarXboxSwapActive) return; // nothing we swapped to restore
                    _viiperGameBarXboxSwapActive = false;

                    ResolveViiperDeviceTarget(out string type, out ushort vid, out ushort pid);
                    if (type == "xbox360")
                    {
                        // User's selection is xbox360 (e.g. they switched to it while the Game Bar was
                        // open) — the device is already xbox360 from the open-swap, so there is nothing
                        // to restore. Skip the no-op swap (and its USB churn) entirely.
                        Logger.Info("[ViiperGBSwap] Game Bar closed — selection is Xbox 360, nothing to restore");
                        return;
                    }
                    Logger.Info($"[ViiperGBSwap] Game Bar closed — restoring VIIPER device to {type}");
                    Task.Run(() =>
                    {
                        try { monitor.SwitchViiperDeviceType(type, vid, pid); }
                        catch (Exception ex) { Logger.Warn($"[ViiperGBSwap] restore swap threw: {ex.Message}"); }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ViiperGBSwap] handler threw: {ex.Message}");
            }
        }

        /// <summary>
        /// External Gamepad Mode tile handler. When ON, hides ALL the handheld's own controllers
        /// via HidHide — native MSI (PID_1901 + PID_1902) AND the virtual ViGEm controller — so only
        /// an externally connected gamepad remains visible (other gamepads with a different VID/PID
        /// are untouched). When OFF, restores the prior state.
        ///
        /// Reuses the existing ControllerSuppressionManager: hideTargetMode=4 = "external gamepad,
        /// hide everything". Not persisted — the tile always starts OFF after a helper start.
        /// </summary>
        internal static void OnExternalGamepadModeChanged(bool on)
        {
            var deviceInfo = Devices.DeviceDetector.DetectDevice();
            if (deviceInfo.DeviceType != DeviceType.MSIClaw)
            {
                Logger.Debug("ExternalGamepadMode: ignored (not an MSI Claw)");
                return;
            }

            lock (_externalGamepadLock)
            {
                if (on == _externalGamepadModeActive)
                {
                    Logger.Debug($"ExternalGamepadMode: already {(on ? "on" : "off")} — no-op");
                    return;
                }

                if (on)
                {
                    // Remember whether the virtual ViGEm controller was running so OFF restores it.
                    lock (clawButtonMonitorLock)
                        _externalGamepadWasVirtual = clawButtonMonitor != null && clawButtonMonitor.IsRunning;
                    _externalGamepadModeActive = true;

                    Logger.Info($"ExternalGamepadMode ON (wasVirtual={_externalGamepadWasVirtual}) → virtual-controller hide state WITHOUT mounting ViGEm");

                    // Reuse the proven emulation path: it switches the Claw to DInput and hides
                    // PID_1902 via HidHide exactly like the virtual-controller toggle — we just
                    // don't mount the ViGEm pad. Net result: no XInput gamepad, no virtual pad,
                    // physical hidden → only an external gamepad remains. Win+G keeps working.
                    Task.Run(() =>
                    {
                        try
                        {
                            StopMSIClawButtonMonitor();                          // clean baseline (XInput + unhidden)
                            StartClawButtonMonitorBackground(mountVigem: false); // DInput + hide PID_1902, no ViGEm
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"ExternalGamepadMode ON task threw: {ex.Message}");
                        }
                    });
                }
                else
                {
                    bool wasVirtual = _externalGamepadWasVirtual;
                    _externalGamepadModeActive = false;
                    Logger.Info($"ExternalGamepadMode OFF (wasVirtual={wasVirtual}) → restoring prior controller state");

                    Task.Run(() =>
                    {
                        try
                        {
                            StopMSIClawButtonMonitor();   // clean baseline (XInput + unhidden = HW mode)
                            if (wasVirtual)
                                StartClawButtonMonitorBackground(mountVigem: true); // restore virtual controller
                            // else: HW mode — the baseline above is already the desired state.
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"ExternalGamepadMode OFF task threw: {ex.Message}");
                        }
                    });
                }
            }
        }

        // ── Per-game HW Controller Exception ──────────────────────────────────────────
        // When a game is flagged, it uses the physical HW controller (native XInput, unhidden)
        // instead of the virtual Viiper/ViGEm pad — fixes titles that choke on the Viiper usbip
        // controller (e.g. NBA 2K26). Helper is authoritative, keyed on GameId.Name, persisted via
        // LocalSettingsHelper. The swap happens only at game start; toggling never tears down live.
        private const string HwExceptionSettingKey = "HwControllerExceptionGames";
        private static readonly object _hwExceptionLock = new object();
        private static System.Collections.Generic.HashSet<string> _hwExceptionGames;
        private static bool _hwExceptionSwapActive;   // true once we deviated from the standard mode for the running game
        // Non-null while a per-game exception overrides the standard controller mode: the game's EFFECTIVE
        // virtual-vs-hardware state (standard XOR exception). Read by ApplyEffectiveFirmwareKeyboardMode so
        // firmware remaps follow the game's actual mode, not the standard. Cleared on game end / mode change.
        private static bool? _perGameEffectiveVirtual;
        // The game key we last evaluated. RunningGame_PropertyChanged re-fires repeatedly for the
        // SAME game (RTSS re-detection, focus changes, shader-compile churn). Without this guard the
        // swap would re-run on every re-fire → the controller visibly flickers between virtual and HW.
        private static string _hwExceptionLastGameKey;

        private static System.Collections.Generic.HashSet<string> HwExceptionGames
        {
            get
            {
                lock (_hwExceptionLock)
                {
                    if (_hwExceptionGames == null)
                    {
                        _hwExceptionGames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            // Unit separator () as delimiter — game names can contain ';' or ','.
                            if (Settings.LocalSettingsHelper.TryGetValue(HwExceptionSettingKey, out string saved)
                                && !string.IsNullOrWhiteSpace(saved))
                            {
                                foreach (var g in saved.Split(''))
                                    if (!string.IsNullOrWhiteSpace(g)) _hwExceptionGames.Add(g);
                            }
                        }
                        catch (Exception ex) { Logger.Warn($"[HWExc] load failed: {ex.Message}"); }
                    }
                    return _hwExceptionGames;
                }
            }
        }

        // Normalize the exception key to EXACTLY match the per-game controller-profile key. The
        // widget stores controller profiles under ControllerProfile_Game_{currentGameName}, where
        // currentGameName = SanitizeGameName(GameId.Name) = GameId.Name.Trim(). Trimming here keeps
        // the HW exception and the per-game controller profile on the same identity, so a stray
        // leading/trailing space in GameId.Name can't split them onto different keys.
        private static string NormalizeHwExceptionKey(string gameKey) => gameKey?.Trim();

        internal static bool IsHwControllerException(string gameKey)
        {
            gameKey = NormalizeHwExceptionKey(gameKey);
            if (string.IsNullOrEmpty(gameKey)) return false;
            lock (_hwExceptionLock) return HwExceptionGames.Contains(gameKey);
        }

        /// <summary>Persist the exception flag for a game. No live controller swap — applies at next game start.</summary>
        internal static void SetHwControllerException(string gameKey, bool on)
        {
            gameKey = NormalizeHwExceptionKey(gameKey);
            if (string.IsNullOrEmpty(gameKey)) return;
            lock (_hwExceptionLock)
            {
                var set = HwExceptionGames;
                bool changed = on ? set.Add(gameKey) : set.Remove(gameKey);
                if (!changed) return;
                try { Settings.LocalSettingsHelper.SetValue(HwExceptionSettingKey, string.Join("", set)); }
                catch (Exception ex) { Logger.Warn($"[HWExc] save failed: {ex.Message}"); }
            }
            Logger.Info($"[HWExc] Exception for '{gameKey}' set to {on} (no live swap; applies at next game start)");
        }

        private static void PushHwControllerExceptionState(bool on)
        {
            try
            {
                Program.SendPipeMessage(new Shared.IPC.PipeMessage
                {
                    Function = Function.HwControllerException,
                    Command = Command.Set,
                    Content = on ? "true" : "false"
                });
            }
            catch (Exception ex) { Logger.Debug($"[HWExc] push failed: {ex.Message}"); }
        }

        /// <summary>
        /// Game-start hook (RunningGame became valid). Pushes the current game's exception state to
        /// the widget and, if the game is flagged AND controller emulation is enabled, swaps the
        /// virtual controller for the native HW controller. Only the swap-at-start path.
        /// </summary>
        internal static void ApplyHwControllerExceptionOnGameStart(string gameKey)
        {
            try
            {
                if (Devices.DeviceDetector.DetectDevice().DeviceType != DeviceType.MSIClaw) return;

                // Idempotency: RunningGame_PropertyChanged re-fires repeatedly for the same game.
                // Only evaluate (and possibly swap) once per game session — re-firing for the same
                // key is a no-op, so the controller never flickers between virtual and HW.
                if (string.Equals(gameKey, _hwExceptionLastGameKey, StringComparison.OrdinalIgnoreCase))
                    return;
                _hwExceptionLastGameKey = gameKey;

                bool exception = IsHwControllerException(gameKey);
                PushHwControllerExceptionState(exception);

                if (!exception)
                {
                    // No deviation for this game. If a previous exception game left us deviated from the
                    // standard mode, restore the standard mode now.
                    if (_hwExceptionSwapActive) RestoreVirtualControllerAfterHwException();
                    return;
                }

                // Effective mode for this game = standard XOR exception (dynamic direction):
                //   standard Virtual + exception → Hardware for this game (the classic NBA-2K26 case)
                //   standard Hardware + exception → Virtual for this game
                bool standardVirtual = Settings.LocalSettingsHelper.TryGetValue("ControllerEmulationEnabled", out bool en) && en;
                bool effectiveVirtual = standardVirtual ^ exception;   // exception is true here

                Logger.Info($"[HWExc] Game '{gameKey}' exception ON → standardVirtual={standardVirtual}, effective={(effectiveVirtual ? "Virtual" : "Hardware")}");
                _hwExceptionSwapActive = true;
                _perGameEffectiveVirtual = effectiveVirtual;
                Task.Run(() =>
                {
                    try
                    {
                        // Clean baseline first (stop monitor → native XInput, nothing hidden), then bring
                        // up the effective mode. Hardware = leave the monitor stopped; Virtual = start the
                        // virtual pad. Firmware remaps then follow the effective mode.
                        StopMSIClawButtonMonitor();
                        if (effectiveVirtual)
                            StartClawButtonMonitorBackground(mountVigem: true);
                        ApplyEffectiveFirmwareKeyboardMode();
                    }
                    catch (Exception ex) { Logger.Warn($"[HWExc] swap task threw: {ex.Message}"); }
                });
            }
            catch (Exception ex) { Logger.Warn($"[HWExc] ApplyHwControllerExceptionOnGameStart threw: {ex.Message}"); }
        }

        /// <summary>
        /// Game-end hook. If we swapped to HW for an exception game, restore the virtual controller
        /// so the next game gets the normal emulated pad.
        /// </summary>
        internal static void RestoreVirtualControllerAfterHwException()
        {
            // Always clear the idempotency key on game end, EVEN when no swap happened this session
            // (e.g. the exception was enabled mid-game). Otherwise the key stays set and the SAME
            // game re-launched would early-return in ApplyHwControllerExceptionOnGameStart and never
            // swap. Must run before the swap-active early-return below.
            _hwExceptionLastGameKey = null;

            if (!_hwExceptionSwapActive) return;
            _hwExceptionSwapActive = false;
            _perGameEffectiveVirtual = null;   // back to the standard mode

            bool standardVirtual = Settings.LocalSettingsHelper.TryGetValue("ControllerEmulationEnabled", out bool en) && en;
            Logger.Info($"[HWExc] Restoring standard controller mode ({(standardVirtual ? "Virtual" : "Hardware")}) after exception game ended");
            Task.Run(() =>
            {
                try
                {
                    StopMSIClawButtonMonitor();                              // clean baseline
                    if (standardVirtual)
                        StartClawButtonMonitorBackground(mountVigem: true);  // restore the virtual controller
                    // else: standard is Hardware → leave the monitor stopped (native XInput)
                    ApplyEffectiveFirmwareKeyboardMode();
                }
                catch (Exception ex) { Logger.Warn($"[HWExc] restore task threw: {ex.Message}"); }
            });
        }

        /// <summary>
        /// Experimental: reacts to the VIIPER backend toggle (Debug menu).
        ///
        /// Toggling the backend (either direction) ALWAYS deactivates controller emulation
        /// via the robust stop path (SwitchMode(XInput) + ForceUnhideAll), so the physical
        /// Claw controller is restored. The user re-enables controller emulation manually;
        /// <see cref="StartClawButtonMonitorBackground"/> then reads <see cref="_viiperBackendActive"/>
        /// and mounts a VIIPER virtual pad instead of ViGEm.
        ///
        /// The legacy ViiperEmulationManager skips the MSI Claw (DInput path), so this is the
        /// Claw's authoritative reaction to the backend change.
        /// </summary>
        internal static void OnEmulationBackendChanged(bool viiperOn)
        {
            var deviceInfo = Devices.DeviceDetector.DetectDevice();
            if (deviceInfo.DeviceType != DeviceType.MSIClaw)
            {
                Logger.Debug("EmulationBackend change: ignored (not an MSI Claw)");
                return;
            }

            _viiperBackendActive = viiperOn;
            Logger.Info($"EmulationBackend changed → {(viiperOn ? "VIIPER" : "ViGEm (Legacy)")}; deactivating controller emulation via the master toggle (UI + tiles sync, HW controller restored). Re-enable emulation to mount the selected backend.");

            // Drive the disable through the SAME master "Enable Controller Emulation" property
            // the user normally toggles. ControllerEmulationEnabled.SetValue(false):
            //   (a) pushes the new state to the widget so the master toggle AND the Quick-tile
            //       statuses flip to "off" (they mirror this property), and
            //   (b) runs SetEnabled(false) → OnMSIClawMasterEmulationToggled → StopMSIClawButtonMonitor()
            //       (the robust HW restore: SwitchMode(XInput) + HidHide ForceUnhideAll).
            // Off-thread so the lengthy stop (HID switch + up to 5 s HidHide settle) doesn't block
            // the pipe callback that delivered the backend change.
            Task.Run(() =>
            {
                try
                {
                    var enabledProp = controllerEmulationManager?.ControllerEmulationEnabled;
                    if (enabledProp != null && enabledProp.Value)
                        enabledProp.SetValue(false);
                    else
                        StopMSIClawButtonMonitor(); // already off in the property — just ensure the HW controller is restored
                }
                catch (Exception ex) { Logger.Warn($"EmulationBackend change deactivate task threw: {ex.Message}"); }
            });
        }

        /// <summary>
        /// Resolves the VIIPER virtual device target (libviiper type tag + optional Steam VID/PID)
        /// from the current settings. Mirrors ViiperEmulationManager.ResolveDeviceTargets so the
        /// Claw path and the Legion path agree on the same mapping.
        /// </summary>
        private static void ResolveViiperDeviceTarget(out string type, out ushort vid, out ushort pid)
        {
            type = "xbox360";
            vid = 0;
            pid = 0;

            var dt = settingsManager?.ViiperDeviceType?.Value;
            if (!string.IsNullOrEmpty(dt)) type = dt;

            bool isSteam = type == "steam-generic" || type == "steam-controller" || type == "steamdeck-generic";
            if (isSteam && settingsManager?.ViiperSteamSubDevice != null)
                Settings.ViiperSteamSubDeviceProperty.TryGetSteamVidPid(settingsManager.ViiperSteamSubDevice.Value, out vid, out pid);
        }

        /// <summary>
        /// Reacts to a VIIPER device-type / Steam-sub-device change. Hot-swaps the live VIIPER
        /// device when the VIIPER backend is active on an MSI Claw. Off-thread because the USBIP
        /// swap is a 1–2 s round-trip that must not block the pipe callback.
        /// </summary>
        private static void OnViiperDeviceTargetChanged()
        {
            if (!_viiperBackendActive) return;
            var deviceInfo = Devices.DeviceDetector.DetectDevice();
            if (deviceInfo.DeviceType != DeviceType.MSIClaw) return;

            ResolveViiperDeviceTarget(out string type, out ushort vid, out ushort pid);
            Labs.ClawButtonMonitor monitor;
            lock (clawButtonMonitorLock) monitor = clawButtonMonitor;
            if (monitor == null) return;

            Task.Run(() =>
            {
                try { monitor.SwitchViiperDeviceType(type, vid, pid); }
                catch (Exception ex) { Logger.Warn($"VIIPER device-target change task threw: {ex.Message}"); }
            });
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
        private static void StartClawButtonMonitorBackground(bool startInMouseMode = false, bool mountVigem = true)
        {
            Task.Run(() =>
            {
                // Boot-timing instrumentation: a single stopwatch threaded through every phase so the
                // log shows exactly where the ~seconds before the controller is usable are spent, and
                // an invocation counter so a re-triggered mount (LED cycling) is obvious. The "+{ms}"
                // offsets are relative to this method's entry. Cheap (one Stopwatch + Info lines).
                var bootSw = System.Diagnostics.Stopwatch.StartNew();
                int invocation = System.Threading.Interlocked.Increment(ref _startClawMonitorInvocations);
                Logger.Info($"[ClawBoot #{invocation}] StartClawButtonMonitorBackground begin (startInMouseMode={startInMouseMode}, mountVigem={mountVigem}, viiperBackendActive={_viiperBackendActive})");
                // Hold the LED-by-SoC tint while the controller is mounting (the LED HID is busy and
                // MsiLedBoot owns the LED then) — avoids the boot flicker. Released at BOOT COMPLETE.
                _clawControllerReady = false;
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

                    // ── Step 0.4: One-time HW-controller baseline self-heal ────────
                    // In rare cases the physical Claw is left in a divergent state (e.g. a previous
                    // helper crashed leaving HidHide cloaking on, or the controller didn't re-enumerate
                    // after a mode switch), so the virtual controller mounts on a bad base. The user's
                    // reliable manual fix is an emulation off→on cycle. Reproduce that ONCE — only when
                    // actually mounting a ViGEm pad AND the cheap baseline check says the HW base is not
                    // clean. Common case (base clean) = one WMI query then skip, so mount performance is
                    // unchanged. Single attempt, no retry loop; mount proceeds either way.
                    if (mountVigem && !_clawHwBaselineHealAttempted)
                    {
                        _clawHwBaselineHealAttempted = true;
                        try
                        {
                            if (IsHwControllerBaselineClean(out string baselineDiag, out bool staleStatePresent))
                            {
                                Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms baseline clean before mount ({baselineDiag}) — no self-heal needed");
                            }
                            else if (!staleStatePresent)
                            {
                                // "Not clean" only because the physical Claw hasn't enumerated yet
                                // (pid1901=0 at cold boot) — nothing stale for a Stop()/unhide to fix.
                                // Measured: the heal left clean=False anyway and cost 1.5–3.3 s. Skip it;
                                // the mount below brings the controller up the normal way.
                                Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms baseline not-yet-ready but nothing stale ({baselineDiag}) — skipping self-heal (saves the off→on cycle)");
                            }
                            else
                            {
                                Logger.Warn($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms baseline STALE ({baselineDiag}) — forcing one emulation-off cycle (+1200ms settle) to restore the physical controller");
                                StopMSIClawButtonMonitor(); // = emulation-off cleanup (ForceUnhideAll + re-enumerate)
                                System.Threading.Thread.Sleep(1200); // let HidHide settle + devices re-enumerate
                                bool cleanNow = IsHwControllerBaselineClean(out string afterDiag);
                                Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms baseline after self-heal: clean={cleanNow} ({afterDiag})");
                            }
                        }
                        catch (Exception healEx)
                        {
                            Logger.Warn($"[MSIClaw] HW controller baseline self-heal threw (continuing to mount): {healEx.Message}");
                        }
                    }

                    // ── Step 0.5: Proactive XInput mode switch ────────────────────
                    // Mirrors HC ClawA1M.Open() → SwitchMode(gamepadMode):
                    // If the controller is in hardware Desktop mode (e.g. left by a previous
                    // HC session or after ClawTweaks shutdown), the user would have no gamepad
                    // input during the ~2.5 s DInput settle in ClawButtonMonitor.Start().
                    // Switching to XInput first gives immediate gamepad behaviour.
                    // No settle needed here — ClawButtonMonitor.Start() includes its own
                    // 2.5 s settle after the XInput → DInput transition.
                    //
                    // The proactive XInput switch GUARANTEES OpenClawInterfaces() then finds no DInput
                    // joystick and must SwitchMode(DInput) + wait 2500 ms. That XInput→DInput transition is
                    // REQUIRED, not just cosmetic: the MSI Claw firmware only ARMS its rumble engine on a
                    // real mode transition. An earlier VIIPER-only optimisation skipped this switch (the
                    // Claw is often already in DInput after a warm reboot, so it saved the ~2.5 s settle and
                    // booted ~3.8 s instead of ~6.5 s) — but that left vibration DEAD until a manual
                    // emulation re-toggle, because no transition ever happened. ViGEm always did the switch,
                    // which is exactly why rumble always worked there. So we do it unconditionally now for
                    // VIIPER too: rumble correctness outweighs the ~2.5 s boot cost. (HC pattern; also gives
                    // immediate gamepad feel during the settle.)
                    Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms pre-XInput-switch (TrySwitchToXInput — required to arm firmware rumble)");
                    MSIClawHidController.TrySwitchToXInput();
                    Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms post-XInput-switch");

                    // ── Step 1: Start ClawButtonMonitor ───────────────────────────
                    // MUST happen before HidHide Enable.
                    // Start() is synchronous for the DInput mode switch: OpenClawInterfaces()
                    // sends SwitchMode(DInput) and waits ~2.5 s for PID_1902 to enumerate
                    // before returning. HidHide Enable must query PID_1902 — if called before
                    // Start() the device is still in XInput mode (PID_1901) and Enable() finds
                    // zero matching device IDs → suppression silently fails.
                    var monitor = EnsureClawButtonMonitor();

                    // External Gamepad Mode: establish the same hidden-DInput state but without
                    // mounting the virtual ViGEm controller (so only an external gamepad is seen).
                    //
                    // Experimental VIIPER backend: instead of the ViGEm pad, mount a VIIPER virtual
                    // Xbox controller fed from the very same submit point (see ClawButtonMonitor.Viiper.cs).
                    // Only when a virtual pad is actually wanted (mountVigem); External Gamepad Mode
                    // wants no virtual pad at all. When VIIPER owns the pad, ViGEm stays suppressed.
                    // VIIPER is the default backend, but it requires usbip-win2. When the driver is
                    // not (yet) installed, auto-fall back to ViGEm so a default-VIIPER install never
                    // leaves a dead controller. The user installs usbip via Onboarding, then VIIPER
                    // takes over on the next enable.
                    bool usbipReady = settingsManager?.UsbipInstalled?.Value == true;
                    bool useViiper = _viiperBackendActive && mountVigem && usbipReady;
                    if (_viiperBackendActive && mountVigem && !usbipReady)
                        Logger.Info("MSIClaw: VIIPER backend requested but usbip-win2 is not installed → falling back to ViGEm");
                    monitor.SetSuppressVigem(!mountVigem || useViiper);
                    monitor.SetViiperEnabled(useViiper);
                    if (useViiper)
                    {
                        ResolveViiperDeviceTarget(out string vType, out ushort vVid, out ushort vPid);
                        monitor.SetViiperDeviceType(vType, vVid, vPid);
                        Logger.Info($"MSIClaw: VIIPER backend active → mounting VIIPER virtual pad ({vType}, vid=0x{vVid:X4}, pid=0x{vPid:X4}) instead of ViGEm");
                    }

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
                        // Gyro engine (Adaptive/ClawTweaks vs Direct/HC 1:1) — repurposed LegionGyroMappingType.
                        monitor.SetGyroEngineMode(legionManager.LegionGyroMappingType.Value);
                        monitor.SetGyroInvertX(legionManager.LegionGyroInvertX.Value);
                        monitor.SetGyroInvertY(legionManager.LegionGyroInvertY.Value);
                        // Vibration intensity + stick deadzones (same pre-Start apply rationale as gyro)
                        monitor.SetVibrationIntensity(legionManager.LegionVibrationIntensity.Value);
                        monitor.SetLeftStickDeadzone(legionManager.LegionLeftStickDeadzone.Value);
                        monitor.SetRightStickDeadzone(legionManager.LegionRightStickDeadzone.Value);
                        Logger.Info($"MSIClaw: Gyro settings applied before Start() — target={legionManager.LegionGyroTarget.Value}, activationButton={legionManager.LegionGyroActivationButton.Value}, activationMode={legionManager.LegionGyroActivationMode.Value}");
                    }

                    Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms entering monitor.Start() (mounts {(useViiper ? "VIIPER" : mountVigem ? "ViGEm" : "no")} pad + opens Claw DInput interface)");
                    bool ok = monitor.Start();

                    if (!ok)
                    {
                        Logger.Warn($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms monitor.Start() FAILED — ViGEmBus/VIIPER unavailable or Claw DInput interface not found");
                        return;
                    }

                    Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms monitor.Start() OK — virtual pad live + DInput acquired (controller now usable)");

                    // Repair the firmware paddle map after Start: OpenClawInterfaces rewrites M1/M2 to
                    // their HC defaults on first open (clobbering a firmware paddle keyboard/gamepad
                    // remap). Re-assert now that Start is done, so e.g. an M2→Win+D firmware remap that
                    // must survive into Virtual mode (FW keyboard toggle on) is restored. No-op on
                    // non-A2VM; only M1/M2 are re-written (face slots are untouched by the HC init).
                    if (mountVigem)
                        clawButtonMonitor?.ReassertFirmwareMapAfterStart();

                    // Re-assert the FW vibration-motor ceiling (EEPROM 0x0022/0x0023) unconditionally
                    // on every boot, regardless of whether our own cached LegionVibrationIntensity
                    // value "changed" — the normal handler (Program.LegionControllerHandlers.cs) only
                    // fires WriteVibrationCeilingToFw on a genuine SetValue change, so if the user last
                    // left our slider at e.g. 100% and MSI Center M's own vibration test later wrote a
                    // DIFFERENT ceiling behind our back (e.g. 30%), our cached value never "changes"
                    // and the drift silently persists — ClawTweaks' rumble then feels weak even at
                    // 100% because the firmware itself still caps well below that. This is our own
                    // dedicated control surface (unlike CPU Boost, where external tools are respected),
                    // so unconditionally re-pushing it here is correct, not fighting the user.
                    if (mountVigem)
                        clawButtonMonitor?.WriteVibrationCeilingToFw(legionManager?.LegionVibrationIntensity?.Value ?? 100);

                    // Boot LED: the ViGEm virtual controller is now mounted → signal "ready" (green,
                    // then settle to the user's saved color). Only when a virtual pad was actually
                    // mounted (mountVigem); External Gamepad Mode mounts no ViGEm pad. No-op unless the
                    // user saved a custom LED color. This is the green stage that was never reached
                    // before (it had been wired to the Claw-suppressed ControllerEmulationManager).
                    if (mountVigem)
                    {
                        Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms signalling LED controller-ready (saved colour)");
                        Devices.MSIClaw.MsiLedBoot.SignalControllerReady();
                    }

                    // Apply current mouse sensitivity / threshold from persisted settings.
                    // Done here (after Start) so the values are ready before mouse mode
                    // is potentially activated below.
                    if (controllerEmulationManager != null)
                    {
                        int sens   = controllerEmulationManager.ControllerEmulationMouseSensitivity?.Value ?? 100;
                        int thresh = controllerEmulationManager.ControllerEmulationMouseThreshold?.Value ?? 15;
                        int accel  = controllerEmulationManager.ControllerEmulationMouseAcceleration?.Value ?? 0;
                        int leftBtn  = controllerEmulationManager.ControllerEmulationMouseLeftClickButton?.Value  ?? 6;
                        int rightBtn = controllerEmulationManager.ControllerEmulationMouseRightClickButton?.Value ?? 5;
                        int cursorStick = controllerEmulationManager.ControllerEmulationMouseCursorStick?.Value ?? 0;
                        int scrollStick = controllerEmulationManager.ControllerEmulationMouseScrollStick?.Value ?? 0;
                        string actionSlots = controllerEmulationManager.ControllerEmulationMouseActionSlots?.Value ?? "0:0,0:0,0:0,0:0";
                        string dpadActions = controllerEmulationManager.ControllerEmulationMouseDPadActions?.Value ?? "0,0,0,0";
                        int nudgeStep = controllerEmulationManager.ControllerEmulationMouseNudgeStep?.Value ?? 10;
                        monitor.SetMouseModeSensitivity(sens);
                        monitor.SetMouseModeThreshold(thresh);
                        monitor.SetMouseModeAcceleration(accel);
                        monitor.SetMouseLeftClickButton(leftBtn);
                        monitor.SetMouseRightClickButton(rightBtn);
                        monitor.SetMouseCursorStick(cursorStick);
                        monitor.SetMouseScrollStick(scrollStick);
                        monitor.SetMouseActionSlots(actionSlots);
                        monitor.SetMouseDPadActions(dpadActions);
                        monitor.SetMouseModeNudgeStep(nudgeStep);
                        Logger.Info($"MSIClaw: Mouse mode settings applied — sensitivity={sens}, threshold={thresh}, acceleration={accel}, leftBtn={leftBtn}, rightBtn={rightBtn}, cursorStick={cursorStick}, scrollStick={scrollStick}, actionSlots={actionSlots}, dpadActions={dpadActions}, nudgeStep={nudgeStep}");
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
                            int hideTarget = controllerEmulationManager?.HideTarget ?? 0;
                            // With a virtual pad mounted, verify the physical controller is REALLY hidden
                            // and self-heal the rare boot race where it leaks (= doubled input). Without a
                            // pad (External Gamepad Mode) keep the plain one-shot hide.
                            _msiClawOwnsSuppression = mountVigem
                                ? suppression.EnsureHidden(DeviceType.MSIClaw, hideTarget)
                                : suppression.Enable(DeviceType.MSIClaw, hideTarget);
                            Logger.Info($"[MSIClaw] HidHide suppression => {_msiClawOwnsSuppression}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"[MSIClaw] HidHide Enable threw: {ex.Message}");
                        }
                    }

                    Logger.Info($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms BOOT COMPLETE (HidHide enabled, physical DInput hidden)");

                    // Controller mount done → LED-by-SoC may now tint (one clean transition instead of
                    // flickering during the mount). Force a re-tint so it doesn't wait up to the poll
                    // interval after boot.
                    _clawControllerReady = true;
                    ReassertLedColorBySocIfActive();

                    // Start the unintended-HW-mouse watcher once per session (off the boot critical
                    // path — this is after BOOT COMPLETE). Detects an accidental firmware mouse and
                    // switches back to the controller; never touches our own intentional killswitch.
                    lock (_hwMouseWatcherLock)
                    {
                        if (_hwMouseWatcher == null)
                        {
                            _hwMouseWatcher = new Devices.MSIClaw.MsiClawHwMouseWatcher(RecoverFromUnintendedHwMouse, IsClawMonitorRunning);
                            _hwMouseWatcher.Start();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[ClawBoot #{invocation}] +{bootSw.ElapsedMilliseconds}ms startup threw: {ex.Message}");
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
                    controllerEmulationManager.EmulationEnabledChanged -= OnMSIClawMasterEmulationToggled;
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
            legionManager.OnMacroConfigChanged = OnMSIClawMacroConfigChanged;
            Logger.Info("MSIClaw: LegionManager.OnButtonMappingChanged + OnGamepadMappingChanged + OnMacroConfigChanged wired → ClawButtonMonitor");
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
        /// Invoked when the user sets an M1/M2 back-button mapping in the Controller tab.
        /// Routes ALL three Back-Buttons modes to ClawButtonMonitor:
        ///   buttonIndex 3 = M1 (OEM3), buttonIndex 4 = M2 (OEM4).
        ///   mappingType 0 = Gamepad → values[0] is the RemapAction index.
        ///   mappingType 1 = Keyboard → values are HID key codes (fired as a chord on press).
        ///   mappingType 2 = Mouse → values[0] is the mouse action index.
        /// Previously only Gamepad was wired here (Keyboard/Mouse were dropped), so a Keyboard or
        /// Mouse mapping on M1/M2 silently did nothing — unlike the normal "Re-Map Specific Buttons"
        /// path. ConfigureBackButtonMapping handles all three modes uniformly.
        ///
        /// EnsureClawButtonMonitor() pre-creates the monitor if not yet running so the mapping
        /// is ready when Start() is called from StartClawButtonMonitorBackground().
        /// </summary>
        private static void OnMSIClawButtonMappingChanged(int buttonIndex, int mappingType, int[] values)
        {
            string button = buttonIndex == 3 ? "M1" : buttonIndex == 4 ? "M2" : null;
            if (button == null) return; // M3 and Y1/Y2/Y3 not applicable on MSI Claw

            try
            {
                // EnsureClawButtonMonitor() creates the instance if not yet present.
                // ConfigureBackButtonMapping() hot-applies if the monitor is running, or
                // pre-configures the instance so the mapping is ready when Start() is called.
                EnsureClawButtonMonitor().ConfigureBackButtonMapping(button, mappingType, values);
                Logger.Info($"MSIClaw: {button} back-button mapping applied — mappingType={mappingType}, values=[{string.Join(",", values ?? Array.Empty<int>())}]");
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawButtonMappingChanged threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Invoked when an M1/M2 Macro mapping's inter-key delay or repeat mode (0=Once,
        /// 1=Repeat while held, 2=Hold toggle) is set/changed — sent alongside the main
        /// ButtonMapping JSON whenever Type=4 (Macro).
        /// </summary>
        private static void OnMSIClawMacroConfigChanged(string button, int delayMs, int pressMs, int mode)
        {
            try
            {
                EnsureClawButtonMonitor().ConfigureBackButtonMacro(button, delayMs, pressMs, mode);
                Logger.Info($"MSIClaw: {button} macro config applied — delayMs={delayMs}, pressMs={pressMs}, mode={mode}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawMacroConfigChanged threw: {ex.Message}");
            }
        }
    }
}
