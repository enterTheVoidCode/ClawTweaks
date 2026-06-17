using System;
using System.Runtime.InteropServices;
using System.Threading;
using NLog;
using HidSharp;
using SharpDX.DirectInput;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices.Libraries.Legion;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Monitor for MSI Claw controller input via DInput HID interface.
    /// Mirrors HandheldCompanion's DClawController + DInputController approach:
    ///   1. Switch controller to DirectInput mode (PID 0x1902)
    ///   2. Acquire the DirectInput joystick (SharpDX.DirectInput, like HC)
    ///   3. Poll joystick.GetCurrentState() in a loop at ~125 Hz
    ///   4. Forward all inputs (buttons, sticks, triggers, d-pad, M1/M2) to ViGEm
    ///
    /// Input reading: 1:1 from HC DClawController.Tick() —
    ///   state.Buttons[0]=X, [1]=A, [2]=B, [3]=Y, [4]=LB, [5]=RB,
    ///   [8]=Back, [9]=Start, [10]=LS, [11]=RS, [15]=M1, [16]=M2
    ///   state.X/Y = left stick, state.Z/RotationZ = right stick,
    ///   state.RotationX/Y = triggers, state.PointOfViewControllers[0] = d-pad
    ///
    /// Command sending: HidSharp stream.Write() on PID 0x1901 / UsagePage 0xFFA0,
    ///   mirrors MSIClawHidController.TrySwitchToXInput().
    /// </summary>
    internal partial class ClawButtonMonitor : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ── MSI Claw HID identifiers ──────────────────────────────────────────────
        private const ushort CLAW_VID        = 0x0DB0;
        private const ushort CLAW_PID_XINPUT = 0x1901; // command iface in XInput mode: UsagePage 0xFFA0 Usage 0x0001
        private const ushort CLAW_PID_DINPUT = 0x1902; // DInput joystick + command iface in DInput mode: UsagePage 0xFFF0 Usage 0x0040

        // Command interface HID filter — 1:1 from HC ClawA1M hidFilters:
        //   { PID_XINPUT, new HidFilter(0xFFA0, 0x0001) }   → XInput mode (PID_1901)
        //   { PID_DINPUT, new HidFilter(0xFFF0, 0x0040) }   → DInput mode (PID_1902)
        //
        // After SwitchMode(DInput), the USB device re-enumerates as PID_1902. The
        // command interface moves from PID_1901/0xFFA0 to PID_1902/0xFFF0.
        // FindCommandDevice() must search both so commands work in both modes.
        private const int CMD_USAGE_PAGE_XINPUT = 0xFFA0; // PID_1901 command interface
        private const int CMD_USAGE_PAGE_DINPUT = 0xFFF0; // PID_1902 command interface

        // ── HC command bytes (1:1 from HC ClawA1M) ────────────────────────────────
        private const byte REPORT_ID       = 0x0F;
        private const byte CMD_SYNC_ROM    = 0x22; // CommandType.SyncToROM
        private const byte CMD_SWITCH_MODE = 0x24; // CommandType.SwitchMode
        private const byte MODE_XINPUT     = 0x01; // GamepadMode.XInput  → PID_1901 (restores Phase 1 baseline)
        private const byte MODE_DINPUT     = 0x02; // GamepadMode.DirectInput → PID_1902
        private const byte MODE_DESKTOP    = 0x04; // GamepadMode.Desktop (NOT used for Stop: Desktop mode keeps PID_1902)

        // GetM12 parameters per firmware. HC uses 0x217/0x219 for Claw 8 AI (A2VM).
        private static readonly byte[] M1_NEW = { 0x00, 0xBA }; // fw >= 0x166
        private static readonly byte[] M2_NEW = { 0x01, 0x63 };
        private static readonly byte[] M1_DEF = { 0x00, 0x7A }; // fw 0x163
        private static readonly byte[] M2_DEF = { 0x01, 0x1F };

        // ── XInput button flags (for ViGEm Xbox360 state) ────────────────────────
        private const ushort XI_DPAD_UP    = 0x0001;
        private const ushort XI_DPAD_DOWN  = 0x0002;
        private const ushort XI_DPAD_LEFT  = 0x0004;
        private const ushort XI_DPAD_RIGHT = 0x0008;
        private const ushort XI_START      = 0x0010;
        private const ushort XI_BACK       = 0x0020;
        private const ushort XI_LS         = 0x0040;
        private const ushort XI_RS         = 0x0080;
        private const ushort XI_LB         = 0x0100;
        private const ushort XI_RB         = 0x0200;
        private const ushort XI_A          = 0x1000;
        private const ushort XI_B          = 0x2000;
        private const ushort XI_X          = 0x4000;
        private const ushort XI_Y          = 0x8000;

        // ── Gyro constants (1:1 from ControllerEmulationManager) ─────────────────
        private const float GyroMousePixelsPerDeg  = 24.0f;       // MousePixelsPerDegree
        private const float GyroMouseSensPower     = 1.35f;       // MouseSensitivityPower
        private const int   GyroMouseMaxPixelFrame = 220;         // MouseMaxPixelsPerFrame
        private const float GyroMouseMaxDegPerSec  = 720.0f;      // MouseMaxDegPerSecond
        private const float GyroStickMaxDps        = 220.0f;      // StickDegreesPerSecondAtFullDeflection
        private const float GyroOneEuroMinCutoff   = 6.0f;        // OneEuroMinCutoff — raised for less lag at normal speed
        private const float GyroOneEuroBeta        = 0.25f;       // OneEuroBeta — speed-adaptive cutoff; lifts cutoff during motion so the stick stops trailing (kills low-speed stickyness). 0 = pure 6 Hz low-pass = lag at all speeds.
        private const float GyroOneEuroDerivCutoff = 1.5f;        // OneEuroDerivativeCutoff
        private const float GyroDeltaDefault       = 1.0f / 250.0f; // DefaultDeltaSeconds
        private const float GyroDeltaMin           = 0.002f;      // MinDeltaSeconds
        private const float GyroDeltaMax           = 0.05f;       // MaxDeltaSeconds

        // ── Mouse P/Invoke ────────────────────────────────────────────────────────
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);

        // SendInput with MOUSEINPUT — modern replacement for mouse_event.
        // Browsers and modern apps treat SendInput events as real hardware input,
        // enabling smooth-scroll animations that mouse_event does not trigger.
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int      dx;
            public int      dy;
            public uint     mouseData;
            public uint     dwFlags;
            public uint     time;
            public IntPtr   dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT_MOUSE
        {
            public uint       type;      // INPUT_MOUSE = 0
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [In] INPUT_MOUSE[] pInputs, int cbSize);

        private static void SendMouseWheel(int delta)
        {
            var inp = new INPUT_MOUSE[1];
            inp[0].type = 0; // INPUT_MOUSE
            inp[0].mi.dwFlags = MOUSEEVENTF_WHEEL;
            inp[0].mi.mouseData = unchecked((uint)delta);
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT_MOUSE)));
        }

        private const uint MOUSEEVENTF_MOVE = 0x0001;

        // ── State ─────────────────────────────────────────────────────────────────

        // Command interface (PID 0x1901, UsagePage 0xFFA0): HidSharp device for mode-switch commands.
        // Commands sent via HidSharp stream.Write() — same path as MSIClawHidController.TrySwitchToXInput().
        private HidDevice _cmdDevice;
        // Rumble is written to the SAME vendor HID interface the LED controller uses
        // (MSIClawHidController.FindClawHidDeviceInternal — matched by usage page AND usage), which
        // is openable+writable. The old path used _cmdDevice (FindCommandDevice matches the usage
        // page only) and resolved to an interface (…&mi_01) that HidSharp could not open at all
        // ("Unable to open HID class device"), so rumble never reached the Claw.
        private HidDevice _rumbleDevice;

        // DInput joystick (PID 0x1902): SharpDX.DirectInput, 1:1 from HC DInputController.
        // DirectInput is disposed via using-block in FindAndAcquireJoystick();
        // _joystick retains a COM reference keeping the underlying IDirectInput8 alive.
        private Joystick _joystick;

        // Monitoring thread
        private Thread _thread;
        private volatile bool _running;

        // True once the M1/M2/SyncToROM firmware block has been sent in the current emulation session.
        // The mapping is persisted to ROM, so a transient re-open (HidHide cycle-port → InputLost →
        // re-acquire) doesn't need to resend it. Reset in Start() so a fresh off→on cycle reconfigures.
        private bool _firmwareConfigSent;

        // ViGEm virtual Xbox 360 controller
        private ViGEmController _vigem;
        private bool _ownsVigem;
        // When true, Start() establishes the full physical hide state (DInput switch, the caller
        // hides PID_1902 via HidHide) but does NOT mount the virtual ViGEm controller. Used by
        // External Gamepad Mode: same clean "virtual controller" state, just without our virtual
        // pad, so only an external gamepad remains. All _vigem uses are already null-guarded.
        private volatile bool _suppressVigem;

        // ── Vibration (rumble passthrough) ────────────────────────────────────────
        // The game drives the virtual ViGEm controller's force-feedback; ViGEm raises
        // RumbleReceived(large, small) which we forward to the PHYSICAL Claw via a direct
        // HID report on the vendor command interface (_cmdDevice) — 1:1 from HC
        // DClawController.WriteVibration: report {0x05,0x01,0x00,0x00, small, large, 0…}.
        // Scaled by the user's stepless intensity (0.0–1.0). Written on the ViGEm callback
        // thread, so guarded by _rumbleLock against the monitor thread's _cmdDevice use.
        private volatile float _vibrationIntensity = 1.0f;   // 0.0–1.0 (UI: 0–100 %)
        private readonly object _rumbleLock = new object();
        private byte _prevRumbleLarge = 0;
        private byte _prevRumbleSmall = 0;
        private const byte RUMBLE_REPORT_ID = 0x05;          // HC DClawController rumble report ID

        // ── Xbox Guide button (momentary tap on the virtual ViGEm Xbox 360 controller) ──
        // The "Xbox Button" app action / remap target opens Steam Big Picture / the in-game
        // overlay. TriggerGuideTap() (called from any thread — poll edges, pipe handler) just
        // sets a release deadline; the poll loop (ServiceGuideTap) presses/releases Guide on
        // its OWN thread so all ViGEm writes stay single-threaded. SubmitXboxState never touches
        // Guide, so the press persists across frames until ServiceGuideTap releases it.
        private long _guideHoldUntilTicks;           // UtcNow.Ticks release deadline; 0 = inactive
        private bool _guideActive;                   // poll-thread owned: is Guide currently pressed
        private const int GuideTapDurationMs = 90;   // momentary tap length

        // ── Game Bar auto-navigation injection (widget hop + focus drop) ─────────────
        // On Game Bar open we inject a short scripted button sequence onto the virtual controller:
        //   RB × N   → advance the Game Bar widget bar N widgets right onto ClawTweaks (slot 3+,
        //              because Microsoft occupies the first two), then
        //   DpadDown × 1 → drop focus from the widget bar INTO the widget body, so RT/LT tab
        //                  navigation works immediately (replicates the manual press the user did).
        // Each entry is a group of momentary taps; groups run sequentially via a queue. The pressed
        // button mask is OR'd into the outgoing state on the poll thread (ServiceInjection). All taps
        // are normal XInput buttons. Timing is tuned so Game Bar registers distinct events.
        private sealed class InjectGroup
        {
            public ushort Mask;       // XInput button(s) to hold during a press phase
            public int Remaining;     // press phases still to perform in this group
            public int PressMs;       // hold duration per tap
            public int GapMs;         // release gap after each tap
        }
        private readonly object _injectLock = new object();
        private readonly System.Collections.Generic.Queue<InjectGroup> _injectQueue =
            new System.Collections.Generic.Queue<InjectGroup>();
        private InjectGroup _injectCurrent;          // group in progress (poll-thread owned)
        private bool _injectPressing;                // current phase: pressed vs gap
        private long _injectPhaseUntilTicks;         // UtcNow.Ticks end of current phase; 0 = start next
        private const int RbTapPressMs   = 35;       // RB hold per tap
        private const int RbTapGapMs     = 20;       // RB release gap (distinct hops)
        private const int DpadTapPressMs = 35;       // D-pad hold
        private const int DpadTapGapMs   = 60;       // slightly longer so the hop settles before focusing in

        // ── Stick deadzones (radial inner deadzone, percent of full deflection) ───
        // Applied to the outgoing ViGEm stick values in ProcessDirectInputState. 0 = off.
        // Written from the UI/profile thread; read from the poll thread (volatile).
        private volatile int _leftStickDeadzone  = 0;        // 0–50 %
        private volatile int _rightStickDeadzone = 0;        // 0–50 %

        // M1 / M2 configuration (M1 ≈ Legion L, M2 ≈ Legion R)
        private bool _m1Enabled, _m2Enabled;
        private LegionButtonAction _m1Action, _m2Action;
        private string _m1Keys = "", _m2Keys = "";
        private string _m1Cmd  = "", _m2Cmd  = "";
        private Action<string> _shortcutCb;
        private Action<string> _cmdCb;
        private Action          _focusCb;

        // Edge-detection for M1/M2
        private bool _prevM1, _prevM2;

        /// <summary>
        /// Optional callback: receives the full button bitmask (same bit constants as
        /// ControllerHotkeyMonitor) every poll tick while ClawButtonMonitor is running.
        /// Set by Program startup to feed ControllerHotkeyMonitor without a second
        /// DirectInput instance (mirrors HC single-reader pattern from DClawController.Tick).
        /// </summary>
        public Action<uint> HotkeyFeed { get; set; }

        // ── Gyro state ────────────────────────────────────────────────────────────
        // Written from UI/profile thread via SetGyro*(); read from MonitorLoop thread.
        // Volatile ensures writes are visible to the poll thread without a lock.
        private volatile int  _gyroTarget;              // 0=Disabled,1=LeftStick,2=RightStick,3=Mouse
        private volatile int  _gyroActivationMode;      // 0=Hold, 1=Toggle
        private volatile int  _gyroActivationButton;    // 0=None,1=LB,2=LT,3=RB,4=RT
        private volatile int  _gyroSensitivityX = 70;   // 0-100 (from LegionGyroSensitivityX); default 70 = tuned sweet spot (×20 → factor 1400)
        private volatile int  _gyroSensitivityY = 70;   // 0-100 (from LegionGyroSensitivityY); default 70 = tuned sweet spot (×20 → factor 1400)
        private volatile int  _gyroDeadzone = 1;        // degrees/sec (from LegionGyroDeadzone); default 1 (tuned)
        private volatile bool _gyroInvertX;
        private volatile bool _gyroInvertY;
        // Gyro engine (stick path): 0 = Adaptive (ClawTweaks — One-Euro speed-adaptive smoothing),
        // 1 = Direct (HandheldCompanion 1:1 — no software low-pass, raw linear + anti-deadzone).
        // Repurposes the (Claw-irrelevant) LegionGyroMappingType channel; see SetGyroEngineMode.
        private volatile int  _gyroEngineMode;

        // Adapter — read on monitor thread; written under _gyroTarget checks from UI thread.
        // Reference reads/writes are atomic on .NET; capture to local before use.
        private ClawGyroSourceAdapter _gyroAdapter;

        // Activation gate — accessed only from monitor thread (ProcessDirectInputState).
        private bool  _gyroToggleActive;
        private bool  _prevGyroButtonPressed;

        // Logging for gyro activation state changes (monitor thread only — no lock needed).
        private bool  _lastGyroActiveState;
        private bool  _lastGyroActiveLogged;

        // One-Euro filter state — shared between stick and mouse paths (only one active).
        // Accessed only from monitor thread.
        private bool  _gyroFilterInit;
        private float _gyroFiltH, _gyroFiltV, _gyroDerivH, _gyroDerivV;
        private long  _gyroLastSampleTicks;

        // Sub-pixel carry for mouse mode — monitor thread only.
        private float _gyroCarryX, _gyroCarryY;

        // XInput gamepad action remapping for M1/M2 (mirrors HC LayoutManager.MapController()).
        // Disabled = no XInput remap applied (button acts as callback-only or passes through).
        // Updated at runtime via ConfigureXInputRemap(); read from the poll thread — declared
        // volatile so writes from the UI thread are immediately visible to the poll thread.
        private volatile RemapAction _m1RemapAction = RemapAction.Disabled;
        private volatile RemapAction _m2RemapAction = RemapAction.Disabled;

        // Back-button (M1/M2) remap MODE + non-gamepad targets. The Back-Buttons UI offers three
        // modes — Gamepad (Type 0, uses _mXRemapAction above), Keyboard (Type 1) and Mouse (Type 2) —
        // but only Gamepad was ever wired here, so Keyboard/Mouse on M1/M2 silently did nothing (the
        // dispatch dropped everything but the gamepad action). These fields carry the Keyboard chord
        // token and Mouse action so the poll loop can fire them on the press edge, mirroring the
        // normal-button keyboard path in ClawButtonMonitor.GamepadSwap (BuildGamepadSwaps Type==1).
        // Written from the UI thread, read from the poll thread → volatile.
        private volatile int _m1MappingType;          // 0=Gamepad, 1=Keyboard, 2=Mouse
        private volatile int _m2MappingType;
        private volatile string _m1KeyboardToken;     // "+"-joined injector chord, e.g. "LShift+F10"
        private volatile string _m2KeyboardToken;
        private volatile int _m1MouseAction = -1;     // mouse action index (see FireBackButtonMouse); -1 = none
        private volatile int _m2MouseAction = -1;

        // Whether to use new-firmware M1/M2 bytes (Claw 8 AI A2VM, fw >= 0x166)
        private bool _useNewFirmware = true;

        // ── Mouse mode (Controller ↔ Mouse tile) ─────────────────────────────────
        // When true, the DInput poll loop translates right stick → cursor movement,
        // left stick Y → scroll wheel, and LB/RB → mouse buttons via mouse_event(),
        // instead of forwarding to ViGEm. Physical controller stays hidden (HidHide
        // active, DInput mode unchanged) — identical to how HC fork handles desktop mode.
        // Written from UI thread; read from monitor thread.
        private volatile bool _mouseModeEnabled;

        // Mouse mode sub-pixel carry (monitor thread only)
        private float _mouseCarryX, _mouseCarryY;
        // Mouse mode scroll accumulator (monitor thread only)
        private float _mouseScrollAccum;
        // Mouse mode button state tracking for edge detection (monitor thread only)
        private bool _mouseLbWasDown, _mouseRbWasDown;

        // mouse_event flags reused from gyro path
        private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
        private const uint MOUSEEVENTF_WHEEL      = 0x0800;

        // Mouse mode tuning — configurable via SetMouseModeSensitivity / SetMouseModeThreshold.
        private const float MouseModeScrollRate     = 0.08f;
        private const float MouseSensitivityScale   = 20.0f / 100.0f;
        private volatile int   _mouseModeSensitivity = 100;
        private volatile int   _mouseModeThreshold   = 15;

        // Mouse mode button/stick mapping — configurable via Set* methods.
        // Button indices: 0=None,1=A,2=B,3=X,4=Y,5=LB,6=RB,7=LS,8=RS,9=LT,10=RT
        // Stick indices:  0=Right,1=Left (cursor); 0=Left,1=Right (scroll)
        private volatile int _mouseLeftClickButton  = 6; // default RB
        private volatile int _mouseRightClickButton = 5; // default LB
        private volatile int _mouseCursorStick      = 0; // default Right Stick
        private volatile int _mouseScrollStick      = 0; // default Left Stick

        public bool IsRunning => _running;
        public bool HasAnyButtonConfigured => _m1Enabled || _m2Enabled;

        /// <summary>
        /// Always true: ClawButtonMonitor is the primary emulation path for MSI Claw and always
        /// needs a ViGEm virtual Xbox 360 controller to forward all physical gamepad state
        /// (buttons, sticks, triggers, d-pad) — regardless of M1/M2 configuration.
        /// </summary>
        public bool NeedsViGEm => !_suppressVigem;

        /// <summary>
        /// Suppress (true) or allow (false) mounting the virtual ViGEm controller. Must be set
        /// before Start(). External Gamepad Mode sets this true so the monitor establishes the
        /// hidden-DInput state without a virtual pad.
        /// </summary>
        public void SetSuppressVigem(bool suppress) => _suppressVigem = suppress;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Configure M1 or M2 back-paddle. "M1" → Legion L equivalent, "M2" → Legion R equivalent.
        /// Matches the same signature as LegionButtonMonitor.ConfigureButton for drop-in use.
        /// </summary>
        public void ConfigureButton(string button, bool enabled, int action, string shortcutOrCommand,
            Action<string> shortcutCallback, Action<string> commandCallback = null,
            Action focusGoTweaksCallback = null)
        {
            _shortcutCb = shortcutCallback;
            _cmdCb      = commandCallback;
            _focusCb    = focusGoTweaksCallback;

            var atype  = (LegionButtonAction)action;
            string keys = atype == LegionButtonAction.RunCommand ? "" : (shortcutOrCommand ?? "");
            string cmd  = atype == LegionButtonAction.RunCommand ? (shortcutOrCommand ?? "") : "";

            bool isM1 = string.Equals(button, "M1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(button, "L",  StringComparison.OrdinalIgnoreCase);

            if (isM1)
            { _m1Enabled = enabled; _m1Action = atype; _m1Keys = keys; _m1Cmd = cmd; }
            else
            { _m2Enabled = enabled; _m2Action = atype; _m2Keys = keys; _m2Cmd = cmd; }

            Logger.Info($"ClawButtonMonitor: Configured {button} — enabled={enabled}, action={atype}");

            if (_running && NeedsViGEm && _vigem == null)
                EnsureViGEm();
        }

        /// <summary>
        /// Configure XInput gamepad button remapping for M1 or M2.
        ///
        /// Mirrors HC LayoutManager.MapController() software remap path: the action index from
        /// the Legion tab UI (matching RemapActionHelper.IndexToAction) is converted to a
        /// RemapAction and applied in the poll loop while the button is held, overlaying the
        /// mapped input onto the outgoing ViGEm state.
        ///
        /// Called from Program.MSIClaw.cs via LegionManager.OnButtonMappingChanged callback.
        /// Thread-safe: field writes are declared volatile; read by the poll thread.
        /// </summary>
        public void ConfigureXInputRemap(string button, int actionIndex)
        {
            RemapAction action = RemapActionHelper.GetByIndex(actionIndex);
            bool isM1 = string.Equals(button, "M1", StringComparison.OrdinalIgnoreCase);
            if (isM1) _m1RemapAction = action;
            else      _m2RemapAction = action;
            Logger.Info($"ClawButtonMonitor: {button} XInput remap → {action} (actionIndex={actionIndex})");
        }

        /// <summary>
        /// Configure an M1/M2 back-button mapping for all three Back-Buttons modes:
        /// Gamepad (mappingType 0 → values[0] = RemapAction index), Keyboard (1 → values = HID key
        /// codes), Mouse (2 → values[0] = mouse action index). Sets every per-button field so a mode
        /// switch fully replaces the previous target (no stale gamepad action left behind when the
        /// user switches to Keyboard, etc.). Keyboard/Mouse fire on the press edge in the poll loop.
        /// </summary>
        public void ConfigureBackButtonMapping(string button, int mappingType, int[] values)
        {
            bool isM1 = string.Equals(button, "M1", StringComparison.OrdinalIgnoreCase);

            RemapAction gamepadAction = RemapAction.Disabled;
            string keyboardToken = null;
            int mouseAction = -1;

            switch (mappingType)
            {
                case 1: // Keyboard — values are HID usage codes; reuse the GamepadSwap token builder
                    keyboardToken = BuildKeyboardToken(values);
                    break;
                case 2: // Mouse — values[0] is the mouse action index
                    mouseAction = (values != null && values.Length > 0) ? values[0] : -1;
                    break;
                default: // 0 = Gamepad
                    int actionIndex = (values != null && values.Length > 0) ? values[0] : 0;
                    gamepadAction = RemapActionHelper.GetByIndex(actionIndex);
                    break;
            }

            if (isM1)
            {
                _m1MappingType = mappingType;
                _m1RemapAction = gamepadAction;
                _m1KeyboardToken = keyboardToken;
                _m1MouseAction = mouseAction;
            }
            else
            {
                _m2MappingType = mappingType;
                _m2RemapAction = gamepadAction;
                _m2KeyboardToken = keyboardToken;
                _m2MouseAction = mouseAction;
            }

            Logger.Info($"ClawButtonMonitor: {button} back-button mapping → type={mappingType} " +
                        $"gamepad={gamepadAction} keyboard='{keyboardToken}' mouse={mouseAction}");
        }

        /// <summary>
        /// Fires a mouse action for an M1/M2 mouse mapping. Clicks press on the button-down edge and
        /// release on the button-up edge (drag-capable); scroll fires one tick on the down edge only.
        /// Mouse action index matches the widget's LegionGamepadMouseComboBox order:
        /// 0=Left,1=Right,2=Middle,3=ScrollUp,4=ScrollDown,5=ScrollLeft,6=ScrollRight.
        /// </summary>
        private void FireBackButtonMouse(int mouseAction, bool pressed)
        {
            switch (mouseAction)
            {
                case 0: mouse_event(pressed ? MOUSEEVENTF_LEFTDOWN   : MOUSEEVENTF_LEFTUP,   0, 0, 0, IntPtr.Zero); break;
                case 1: mouse_event(pressed ? MOUSEEVENTF_RIGHTDOWN  : MOUSEEVENTF_RIGHTUP,  0, 0, 0, IntPtr.Zero); break;
                case 2: mouse_event(pressed ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0, 0, 0, IntPtr.Zero); break;
                case 3: if (pressed) SendMouseWheel(120);  break; // Scroll Up
                case 4: if (pressed) SendMouseWheel(-120); break; // Scroll Down
                // 5/6 (horizontal scroll) not wired — rare; left as no-op so routing stays clean.
                default: break;
            }
        }

        /// <summary>
        /// Enable or disable software mouse mode.
        ///
        /// When enabled, the DInput poll loop translates physical stick/button inputs into
        /// Windows mouse events (right stick → cursor, left stick Y → scroll, LB/RB → clicks)
        /// instead of forwarding to ViGEm. The physical controller stays hidden and DInput
        /// mode is unchanged — no firmware mode switch or HidHide reset needed.
        ///
        /// This matches HC fork's desktop-mode virtual approach: the ViGEm virtual controller
        /// remains plugged in (ControllerHotkeyMonitor and hotkeys stay functional), but
        /// mouse events are produced by software from the DInput readings.
        ///
        /// Thread-safe: _mouseModeEnabled is volatile.
        /// </summary>
        public void SetMouseMode(bool enabled)
        {
            _mouseModeEnabled = enabled;
            if (!enabled)
            {
                // Release any stuck mouse buttons when leaving mouse mode
                try
                {
                    mouse_event(MOUSEEVENTF_LEFTUP,  0, 0, 0, IntPtr.Zero);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
                }
                catch { }
                _mouseLbWasDown   = false;
                _mouseRbWasDown   = false;
                _mouseCarryX      = 0f;
                _mouseCarryY      = 0f;
                _mouseScrollAccum = 0f;
            }
            Logger.Info($"ClawButtonMonitor: MouseMode → {(enabled ? "on" : "off")}");
        }

        /// <summary>
        /// Set mouse mode cursor sensitivity (1–400, widget slider range).
        /// sensitivity=100 → 20 px/tick at full stick deflection.
        /// Thread-safe: field is volatile.
        /// </summary>
        public void SetMouseModeSensitivity(int sensitivity)
        {
            _mouseModeSensitivity = Math.Max(1, Math.Min(400, sensitivity));
            Logger.Info($"ClawButtonMonitor: MouseMode sensitivity → {_mouseModeSensitivity}");
        }

        /// <summary>
        /// Set mouse mode stick deadzone threshold (0–20, widget slider range).
        /// threshold=15 → 15% of full deflection ignored before cursor starts moving.
        /// Thread-safe: field is volatile.
        /// </summary>
        public void SetMouseModeThreshold(int threshold)
        {
            _mouseModeThreshold = Math.Max(0, Math.Min(20, threshold));
            Logger.Info($"ClawButtonMonitor: MouseMode threshold → {_mouseModeThreshold}%");
        }

        public void SetMouseLeftClickButton(int button)
        {
            _mouseLeftClickButton = Math.Max(0, Math.Min(10, button)); // 0-10: None…RS,LT,RT
            Logger.Info($"ClawButtonMonitor: MouseMode left-click button → {_mouseLeftClickButton}");
        }

        public void SetMouseRightClickButton(int button)
        {
            _mouseRightClickButton = Math.Max(0, Math.Min(10, button)); // 0-10: None…RS,LT,RT
            Logger.Info($"ClawButtonMonitor: MouseMode right-click button → {_mouseRightClickButton}");
        }

        public void SetMouseCursorStick(int stick)
        {
            _mouseCursorStick = Math.Max(0, Math.Min(1, stick));
            Logger.Info($"ClawButtonMonitor: MouseMode cursor stick → {(_mouseCursorStick == 0 ? "Right" : "Left")}");
        }

        public void SetMouseScrollStick(int stick)
        {
            _mouseScrollStick = Math.Max(0, Math.Min(1, stick));
            Logger.Info($"ClawButtonMonitor: MouseMode scroll stick → {(_mouseScrollStick == 0 ? "Left" : "Right")}");
        }

        /// <summary>
        /// Set the gyro output target. 0=Disabled, 1=LeftStick, 2=RightStick, 3=Mouse.
        /// Starts or stops the ClawGyroSourceAdapter accordingly.
        /// Mirrors LegionGyroTargetProperty → LegionManager.SetGyroTarget().
        /// </summary>
        public void SetGyroTarget(int target)
        {
            _gyroTarget = target;
            UpdateGyroAdapter();
        }

        /// <summary>0=Hold (button must be held), 1=Toggle (rising edge flips active state).</summary>
        public void SetGyroActivationMode(int mode) => _gyroActivationMode = mode;

        /// <summary>0=None (always active), 1=LB, 2=LT, 3=RB, 4=RT.</summary>
        public void SetGyroActivationButton(int button) => _gyroActivationButton = button;

        /// <summary>Horizontal sensitivity 0-100 (used as gain scale for mouse/stick output).</summary>
        public void SetGyroSensitivityX(int val) => _gyroSensitivityX = val;

        /// <summary>Vertical sensitivity 0-100 (used as gain scale for mouse/stick output).</summary>
        public void SetGyroSensitivityY(int val) => _gyroSensitivityY = val;

        /// <summary>Invert horizontal gyro output.</summary>
        public void SetGyroInvertX(bool val) => _gyroInvertX = val;

        /// <summary>Invert vertical gyro output.</summary>
        public void SetGyroInvertY(bool val) => _gyroInvertY = val;

        /// <summary>Deadzone in degrees/sec (applied before filter and output).</summary>
        public void SetGyroDeadzone(int val) => _gyroDeadzone = val;

        /// <summary>
        /// Gyro engine for the stick path. 0 = Adaptive (ClawTweaks — One-Euro speed-adaptive
        /// smoothing, tames rest jitter), 1 = Direct (HandheldCompanion 1:1 — no software low-pass,
        /// raw linear mapping + anti-deadzone, maximally responsive). Fed from the repurposed
        /// LegionGyroMappingType setting (Claw firmware ignores it, so the channel is free).
        /// </summary>
        public void SetGyroEngineMode(int mode)
        {
            _gyroEngineMode = mode;
            Logger.Info($"ClawButtonMonitor: GyroEngineMode → {(mode == 1 ? "Direct (HC 1:1)" : "Adaptive (ClawTweaks)")}");
        }

        /// <summary>
        /// Set the controller vibration intensity (0–100 %). Scales every rumble report sent
        /// to the physical Claw. 0 = no vibration, 100 = full. Thread-safe (volatile).
        /// Mirrors HC IController.SetVibrationStrength (value/100.0) used in DClawController.WriteVibration.
        /// </summary>
        public void SetVibrationIntensity(int percent)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            _vibrationIntensity = clamped / 100.0f;
            Logger.Info($"ClawButtonMonitor: VibrationIntensity → {clamped}%");
        }

        /// <summary>Left stick radial inner deadzone (0–50 %). Thread-safe (volatile).</summary>
        public void SetLeftStickDeadzone(int percent)
        {
            _leftStickDeadzone = Math.Max(0, Math.Min(50, percent));
            Logger.Info($"ClawButtonMonitor: LeftStickDeadzone → {_leftStickDeadzone}%");
        }

        /// <summary>Right stick radial inner deadzone (0–50 %). Thread-safe (volatile).</summary>
        public void SetRightStickDeadzone(int percent)
        {
            _rightStickDeadzone = Math.Max(0, Math.Min(50, percent));
            Logger.Info($"ClawButtonMonitor: RightStickDeadzone → {_rightStickDeadzone}%");
        }

        /// <summary>
        /// Applies a radial inner deadzone to a stick: if the vector magnitude is below the
        /// deadzone fraction it is zeroed; otherwise the remaining range is rescaled to full
        /// 0..1 so the stick still reaches full deflection (standard radial deadzone, like HC).
        /// </summary>
        private static void ApplyStickDeadzone(ref short x, ref short y, int deadzonePercent)
        {
            if (deadzonePercent <= 0) return;

            const float MaxAxis = 32767f;
            float fx = x / MaxAxis;
            float fy = y / MaxAxis;
            float mag = (float)Math.Sqrt(fx * fx + fy * fy);
            float dz = deadzonePercent / 100.0f;

            if (mag <= dz)
            {
                x = 0;
                y = 0;
                return;
            }

            // Rescale magnitude from (dz..1) to (0..1) along the same direction.
            float scaled = (mag - dz) / (1.0f - dz);
            if (scaled > 1.0f) scaled = 1.0f;
            float factor = scaled / mag;
            x = ClampAxis(fx * factor * MaxAxis);
            y = ClampAxis(fy * factor * MaxAxis);
        }

        private static short ClampAxis(float v)
        {
            if (v >  32767f) return  32767;
            if (v < -32767f) return -32767;
            return (short)v;
        }

        /// <summary>
        /// Start monitoring. Returns false only if ViGEmBus is unavailable.
        ///
        /// ClawButtonMonitor is the primary emulation path for MSI Claw — it always starts
        /// regardless of whether M1/M2 paddles are configured, because it also forwards all
        /// standard gamepad inputs (A/B/X/Y, sticks, triggers, d-pad) to the ViGEm virtual
        /// Xbox 360 controller.
        /// </summary>
        public bool Start()
        {
            if (_running) return true;

            // Fresh emulation session → resend the firmware block once on the first OpenClawInterfaces.
            _firmwareConfigSent = false;

            // Drop any cached rumble interface so it re-resolves for this session. After a reboot the
            // HidHide cycle-port re-enumeration shuffles HID enumeration order, and the resolver must
            // re-pick the DInput controller (PID 0x1902) rather than a stale/wrong cached handle.
            _rumbleDevice = null;

            // Normally create ViGEm — ClawButtonMonitor is the primary emulation path for MSI Claw.
            // External Gamepad Mode suppresses the mount: it wants the hidden-DInput state without
            // a virtual controller, so only an external gamepad is seen. All _vigem uses below are
            // null-guarded, so the read loop simply doesn't forward.
            if (!_suppressVigem)
            {
                if (!EnsureViGEm())
                    return false;
            }
            else if (_viiperEnabled)
            {
                // Experimental VIIPER backend: mount a VIIPER virtual Xbox pad instead of ViGEm,
                // fed from the same submit point. Fail Start() on mount error so the robust stop
                // path restores the physical controller (no hidden HW with no replacement).
                if (!EnsureViiper())
                    return false;
                Logger.Info("ClawButtonMonitor: VIIPER virtual Xbox pad mounted (ViGEm suppressed)");
            }
            else
            {
                Logger.Info("ClawButtonMonitor: ViGEm mount suppressed (External Gamepad Mode) — DInput hide only, no virtual controller");
            }

            bool found = OpenClawInterfaces();
            if (!found)
                Logger.Warn("ClawButtonMonitor: Claw DInput interface not found initially; will retry in MonitorLoop");

            _running = true;
            // Highest priority: this loop reads the controller and drives the virtual mouse /
            // ViGEm output. At Normal priority it stutters under load (it was the only input
            // thread without an elevated priority). Mirrors HC's input-thread handling.
            _thread = new Thread(MonitorLoop) { IsBackground = true, Name = "ClawButtonMonitor", Priority = ThreadPriority.Highest };
            _thread.Start();

            // Start gyro adapter if a target is already configured (e.g. profile was loaded before Start).
            UpdateGyroAdapter();

            Logger.Info("ClawButtonMonitor: Started");
            return true;
        }

        /// <summary>Stop monitoring and restore controller to XInput mode (Phase 1 baseline).</summary>
        public void Stop()
        {
            if (!_running) return;

            _running = false;

            if (_thread != null && _thread.IsAlive)
                _thread.Join(1500);
            _thread = null;

            // Restore controller to XInput mode (PID_1901 = Phase 1 baseline).
            //
            // Always re-discover the command interface — after SwitchMode(DInput)
            // the USB device re-enumerates from PID_1901 to PID_1902. The command
            // interface moves from PID_1901/0xFFA0 to PID_1902/0xFFF0.
            // The cached _cmdDevice from Start() points to the old PID_1901 path
            // which is now gone; FindCommandDevice() searches both usage pages.
            //
            // Why MODE_XINPUT, not MODE_DESKTOP:
            //   HC's ClawA1M.Close() sends Desktop mode (0x04) on DEVICE-level shutdown,
            //   but HC's DClawController.Stop() sends NO mode switch at all — mode
            //   transitions are device-level, not controller-level, in HC.
            //   ClawTweaks restores Phase 1 (XInput / PID_1901) on emulation disable
            //   so Joy.cpl and games see the original controller.
            //   Desktop mode (0x04) does NOT trigger a PID_1902 → PID_1901 re-enumeration;
            //   XInput mode (0x01) does.
            // Retry loop — legacy ControllerEmulationManager.Disable() fires before this
            // handler (event subscription order) and cycles USB for COL01, temporarily
            // disconnecting ALL PID_1902 collections including our COL02 command interface.
            // Our handler fires ~75ms later while the device is still re-enumerating.
            // Poll until the command interface reappears, then send the mode switch.
            const int STOP_RETRY_INTERVAL_MS = 500;
            const int STOP_MAX_WAIT_MS = 5000;
            int waited = 0;
            _cmdDevice = FindCommandDevice();
            bool switched = _cmdDevice != null && SendSwitchMode(MODE_XINPUT);

            while (!switched && waited < STOP_MAX_WAIT_MS)
            {
                Thread.Sleep(STOP_RETRY_INTERVAL_MS);
                waited += STOP_RETRY_INTERVAL_MS;
                _cmdDevice = FindCommandDevice();
                if (_cmdDevice != null)
                    switched = SendSwitchMode(MODE_XINPUT);
            }

            if (switched)
                Logger.Info($"ClawButtonMonitor: Stop() — SwitchMode(XInput=0x01) sent (after {waited}ms wait); device will re-enumerate as PID_1901");
            else
                Logger.Warn($"ClawButtonMonitor: Stop() — SwitchMode(XInput) failed after {waited}ms; firmware may stay in DInput mode");

            StopGyroAdapter();

            // Stop any active rumble on the physical Claw before tearing down (zero both motors).
            // Done while _cmdDevice is still valid and _running is briefly forced true so the
            // dedupe guard does not skip the zero-write.
            try
            {
                lock (_rumbleLock)
                {
                    var dev = _cmdDevice;
                    if (dev != null && (_prevRumbleLarge != 0 || _prevRumbleSmall != 0))
                    {
                        byte[] stopReport = new byte[64];
                        stopReport[0] = RUMBLE_REPORT_ID;
                        stopReport[1] = 0x01;
                        try { using (var s = dev.Open()) s.Write(stopReport); } catch { }
                    }
                    _prevRumbleLarge = 0;
                    _prevRumbleSmall = 0;
                }
            }
            catch { }

            if (_vigem != null)
                _vigem.RumbleReceived -= OnRumbleReceived;

            CleanupHandles();

            if (_vigem != null && _ownsVigem)
                _vigem.Dispose();
            _vigem = null;
            _ownsVigem = false;

            // Tear down the VIIPER virtual device (no-op when the legacy ViGEm path was used).
            TeardownViiper();

            Logger.Info("ClawButtonMonitor: Stopped");
        }

        public void Dispose()
        {
            Stop();
        }

        // ── Private: device opening ───────────────────────────────────────────────

        /// <summary>
        /// Two-phase interface opening, matching HC's DClawController / DInputController order:
        ///
        /// Phase 1 — find command interface (PID 0x1901, UsagePage 0xFFA0) via HidSharp.
        ///   Present in ALL firmware modes.
        ///
        /// Phase 2 — acquire DInput joystick (PID 0x1902) via SharpDX.DirectInput.
        ///   If not found → send SwitchMode(DInput), wait 2500 ms, retry.
        ///   1:1 from HC DInputController.AttachDetails().
        /// </summary>
        private bool OpenClawInterfaces()
        {
            // Boot-timing instrumentation: breaks the fixed open-interface cost into its parts so the
            // log shows how much is the 1:1-HC M1/M2/SyncToROM block (3×500 ms) vs the conditional
            // 2500 ms DInput settle, and crucially whether the joystick was ALREADY in DInput (settle
            // skipped) or had to be switched (settle paid). Offsets are relative to this call's entry.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CleanupHandles();

            // ── Phase 1: command interface via HidSharp ───────────────────────────
            _cmdDevice = FindCommandDevice();
            if (_cmdDevice == null && _firmwareConfigSent)
            {
                // Transient re-open right after the HidHide cycle-port: the command HID (PID_1902)
                // briefly disappears while it re-enumerates, so the FIRST FindCommandDevice misses it.
                // Poll for it here instead of returning false and bouncing back to the MonitorLoop's
                // coarse 2000 ms RETRY_INTERVAL_MS — the HID normally returns within a few hundred ms,
                // so this collapses the post-boot "controller briefly drops" window. Only on a re-open
                // (_firmwareConfigSent): the very first open must fail fast if the Claw is truly absent.
                const int CmdPollStepMs = 100;
                const int CmdPollMaxMs  = 2500;
                int cmdWaited = 0;
                while (_cmdDevice == null && cmdWaited < CmdPollMaxMs && _running)
                {
                    Thread.Sleep(CmdPollStepMs);
                    cmdWaited += CmdPollStepMs;
                    _cmdDevice = FindCommandDevice();
                }
                if (_cmdDevice != null)
                    Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms command interface returned after {cmdWaited}ms poll (post-cycle-port re-enumeration)");
            }
            if (_cmdDevice == null)
            {
                Logger.Warn("ClawButtonMonitor: MSI Claw command interface not found (searched PID_1901/0xFFA0 and PID_1902/0xFFF0)");
                return false;
            }
            Logger.Info($"ClawButtonMonitor: Found command interface: {_cmdDevice.DevicePath}");

            // ── M1/M2 firmware configuration (send before SwitchMode, 1:1 HC order) ──
            // Only on the FIRST open of an emulation session. SyncToROM persists the mapping, so a
            // transient re-open (HidHide cycle-port → InputLost → re-acquire) doesn't need to resend
            // it — that just wasted ~1.5 s and widened the dead window mid-boot. firstOpen also selects
            // the joystick-acquire strategy below (real mode switch vs. brief re-enumeration poll).
            bool firstOpen = !_firmwareConfigSent;
            if (firstOpen)
            {
                byte[] m1 = _useNewFirmware ? M1_NEW : M1_DEF;
                byte[] m2 = _useNewFirmware ? M2_NEW : M2_DEF;
                SendRawCmd(BuildGetM12Cmd(true,  m1));  Thread.Sleep(500);
                SendRawCmd(BuildGetM12Cmd(false, m2));  Thread.Sleep(500);
                SendRawCmd(BuildSyncToRomCmd());         Thread.Sleep(500);
                _firmwareConfigSent = true;
                Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms M1/M2/SyncToROM done (fixed 1500ms HC block, first open)");
            }
            else
            {
                Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms M1/M2/SyncToROM SKIPPED (already in ROM — transient re-open)");
            }

            // ── Phase 2: acquire DInput joystick via SharpDX.DirectInput ─────────
            // 1:1 from HC DInputController.AttachDetails(): iterate GetDevices(Gamepad),
            // match by VID/PID in the interface path, then Acquire().
            if (!FindAndAcquireJoystick())
            {
                if (firstOpen)
                {
                    // First open and 0x1902 not enumerated → the controller is in XInput (the boot path's
                    // proactive TrySwitchToXInput put it there). Do the real SwitchMode(DInput) + settle.
                    // This XInput→DInput transition is ALSO what arms the firmware rumble engine — a
                    // SwitchMode(DInput) sent while already in DInput is a no-op and leaves rumble dead,
                    // which is why the Claw must be in XInput before this point (boot path guarantees it).
                    Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms DInput joystick NOT present → SwitchMode(DInput) + 2500ms settle (real XInput→DInput transition, arms rumble)");
                    SendSwitchMode(MODE_DINPUT);
                    Thread.Sleep(2500);

                    if (!FindAndAcquireJoystick())
                    {
                        Logger.Warn($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms DInput joystick (PID 0x1902) STILL not found after switch+settle");
                        CleanupHandles();
                        return false;
                    }
                    Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms DInput joystick acquired after mode switch");
                }
                else
                {
                    // Transient re-open: the device is ALREADY in DInput mode (the cycle-port just
                    // re-enumerated PID_1902), so a 2500 ms SwitchMode is wrong here. Poll briefly for
                    // the device to come back — it normally reappears within a few hundred ms. Only if
                    // it really doesn't return do we fall back to the full switch as a safety net.
                    const int ReopenPollStepMs = 100;
                    const int ReopenPollMaxMs  = 2000;
                    bool reacquired = false;
                    int waited = 0;
                    while (waited < ReopenPollMaxMs && _running)
                    {
                        Thread.Sleep(ReopenPollStepMs);
                        waited += ReopenPollStepMs;
                        if (FindAndAcquireJoystick()) { reacquired = true; break; }
                    }

                    if (reacquired)
                    {
                        Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms DInput joystick re-acquired after {waited}ms re-enumeration poll (no mode switch, no firmware resend)");
                    }
                    else
                    {
                        Logger.Warn($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms DInput joystick did not return within {ReopenPollMaxMs}ms on re-open → falling back to SwitchMode(DInput) + 2500ms settle");
                        SendSwitchMode(MODE_DINPUT);
                        Thread.Sleep(2500);
                        if (!FindAndAcquireJoystick())
                        {
                            Logger.Warn($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms DInput joystick (PID 0x1902) STILL not found after fallback switch+settle");
                            CleanupHandles();
                            return false;
                        }
                        Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms DInput joystick acquired after fallback mode switch");
                    }
                }
            }
            else
            {
                Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms DInput joystick ALREADY available → settle SKIPPED");
            }

            Logger.Info($"ClawButtonMonitor: [open] +{sw.ElapsedMilliseconds}ms interfaces ready");
            return true;
        }

        /// <summary>
        /// Find and acquire the MSI Claw DInput joystick (PID 0x1902) via SharpDX.DirectInput.
        ///
        /// 1:1 from HC DInputController.AttachDetails():
        ///   new DirectInput() → GetDevices(Gamepad) → match by VID/PID in InterfacePath
        ///   → new Joystick(di, guid) → Acquire().
        ///
        /// HC pattern: DirectInput created in using-block, Joystick kept as field.
        /// Disposing the DirectInput managed wrapper is safe because the Joystick COM object
        /// holds its own reference to IDirectInput8 (COM ref counting).
        /// </summary>
        private bool FindAndAcquireJoystick()
        {
            try
            {
                using (var di = new DirectInput())
                {
                    // HC: directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices)
                    foreach (var devInfo in di.GetDevices(
                        SharpDX.DirectInput.DeviceType.Gamepad,
                        DeviceEnumerationFlags.AllDevices))
                    {
                        try
                        {
                            var js = new Joystick(di, devInfo.InstanceGuid);
                            string path = js.Properties.InterfacePath ?? "";

                            // Match by VID 0x0DB0 + PID 0x1902 in the interface path
                            if (path.IndexOf("vid_0db0", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                path.IndexOf("pid_1902", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // HC: joystick.Acquire() in Plug()
                                js.Acquire();
                                _joystick = js;
                                Logger.Info($"ClawButtonMonitor: Acquired DInput joystick: {path}");
                                return true;
                            }
                            js.Dispose();
                        }
                        catch { /* device access denied or gone — skip */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"ClawButtonMonitor: FindAndAcquireJoystick failed: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Find the MSI Claw command interface via HidSharp.
        ///
        /// 1:1 from HC ClawA1M hidFilters — two modes, two PIDs:
        ///   XInput mode (PID_1901): command interface at UsagePage 0xFFA0, Usage 0x0001
        ///   DInput mode  (PID_1902): command interface at UsagePage 0xFFF0, Usage 0x0040
        ///
        /// After SwitchMode(DInput) + USB re-enumeration, PID_1901 disappears and the
        /// command interface is on PID_1902 with UsagePage 0xFFF0. Both are searched so
        /// commands (SwitchMode, SyncToROM, M1/M2) work regardless of current mode.
        /// </summary>
        private static HidDevice FindCommandDevice()
        {
            try
            {
                foreach (var dev in DeviceList.Local.GetHidDevices())
                {
                    if (dev.VendorID != CLAW_VID) continue;
                    try
                    {
                        var desc = dev.GetReportDescriptor();
                        if (desc == null) continue;
                        foreach (var item in desc.DeviceItems)
                            foreach (uint encoded in item.Usages.GetAllValues())
                            {
                                int usagePage = (int)((encoded >> 16) & 0xFFFF);
                                if (usagePage == CMD_USAGE_PAGE_XINPUT || usagePage == CMD_USAGE_PAGE_DINPUT)
                                    return dev;
                            }
                    }
                    catch { /* restricted interface — skip */ }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"ClawButtonMonitor: FindCommandDevice enumeration failed: {ex.Message}");
            }
            return null;
        }

        private void CleanupHandles()
        {
            _cmdDevice = null; // HidSharp ref; stream opened/closed per-call, nothing to clean up

            try { _joystick?.Unacquire(); } catch { }
            try { _joystick?.Dispose();   } catch { }
            _joystick = null;
        }

        // ── HC initialization sequence (1:1 from ClawA1M.Open) ───────────────────

        private bool SendSwitchMode(byte mode)
        {
            byte[] cmd = new byte[64];
            cmd[0] = REPORT_ID; cmd[1] = 0x00; cmd[2] = 0x00; cmd[3] = 0x3C;
            cmd[4] = CMD_SWITCH_MODE; cmd[5] = mode; cmd[6] = 0x00;
            return SendRawCmd(cmd);
        }

        /// <summary>
        /// Send a command to the MSI Claw command interface via HidSharp stream.Write().
        /// Mirrors MSIClawHidController.TrySwitchToXInput() — open stream, write, close.
        /// HidSharp's Write() maps to WriteFile() internally, which works even when
        /// OutputReportByteLength = 0 (the MSI Claw command interface has no HID output reports).
        /// </summary>
        private bool SendRawCmd(byte[] cmd)
        {
            if (_cmdDevice == null) return false;
            try
            {
                using (var stream = _cmdDevice.Open())
                {
                    stream.Write(cmd);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"ClawButtonMonitor: SendRawCmd failed: {ex.Message}");
                return false;
            }
        }

        private static byte[] BuildGetM12Cmd(bool isM1, byte[] addr)
        {
            byte[] cmd = new byte[64];
            cmd[0] = REPORT_ID; cmd[1] = 0x00; cmd[2] = 0x00; cmd[3] = 0x3C;
            cmd[4] = 0x21; cmd[5] = 0x01;
            cmd[6] = addr[0]; cmd[7] = addr[1];
            cmd[8] = 0x05; cmd[9] = 0x01; cmd[10] = 0x00; cmd[11] = 0x00; cmd[12] = 0x11;
            return cmd;
        }

        private static byte[] BuildSyncToRomCmd()
        {
            byte[] cmd = new byte[64];
            cmd[0] = REPORT_ID; cmd[1] = 0x00; cmd[2] = 0x00; cmd[3] = 0x3C;
            cmd[4] = CMD_SYNC_ROM;
            return cmd;
        }

        // ── Monitoring loop ───────────────────────────────────────────────────────

        /// <summary>
        /// DirectInput polling loop, ~125 Hz.
        /// Replaces the old overlapped-I/O + HidP approach.
        ///
        /// HC polls joystick.GetCurrentState() in its Tick() method.
        /// We replicate this as a tight polling loop with Thread.Sleep(8).
        ///
        /// Exception handling 1:1 from HC DClawController.Tick():
        ///   NotAcquired → re-acquire
        ///   InputLost   → cleanup and re-init on next iteration
        /// </summary>
        private void MonitorLoop()
        {
            const int POLL_INTERVAL_MS   = 8;    // ~125 Hz (HC drives at its Tick rate)
            const int RETRY_INTERVAL_MS  = 2000;

            while (_running)
            {
                // Ensure joystick is open and acquired
                if (_joystick == null || _joystick.IsDisposed)
                {
                    if (!OpenClawInterfaces())
                    {
                        for (int i = 0; i < RETRY_INTERVAL_MS / 50 && _running; i++)
                            Thread.Sleep(50);
                        continue;
                    }
                }

                try
                {
                    // HC: JoystickState state = joystick.GetCurrentState();
                    JoystickState state = _joystick.GetCurrentState();

                    // HC dirty-state check: first state after connect can be all 32767.
                    // Skip it (1:1 from HC DClawController.Tick).
                    if (state.RotationX == 32767 && state.RotationY == 32767 && state.RotationZ == 32767)
                    {
                        Thread.Sleep(POLL_INTERVAL_MS);
                        continue;
                    }

                    ProcessDirectInputState(state);
                }
                catch (SharpDX.SharpDXException ex)
                {
                    // HC DClawController.Tick exception handling:
                    if (ex.ResultCode == ResultCode.NotAcquired)
                    {
                        // HC: Plug() → joystick.Acquire()
                        try { _joystick?.Acquire(); }
                        catch (Exception reEx)
                        {
                            Logger.Warn($"ClawButtonMonitor: Re-acquire failed: {reEx.Message}");
                            CleanupHandles();
                        }
                    }
                    else if (ex.ResultCode == ResultCode.InputLost)
                    {
                        // HC: AttachDetails() → re-find joystick
                        Logger.Warn("ClawButtonMonitor: InputLost — device disconnected, re-initialising");
                        CleanupHandles();
                    }
                    else
                    {
                        Logger.Warn($"ClawButtonMonitor: DirectInput SharpDXException ({ex.ResultCode}): {ex.Message}");
                        CleanupHandles();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"ClawButtonMonitor: MonitorLoop exception: {ex.Message}");
                    CleanupHandles();
                    for (int i = 0; i < RETRY_INTERVAL_MS / 50 && _running; i++)
                        Thread.Sleep(50);
                }

                Thread.Sleep(POLL_INTERVAL_MS);
            }
        }

        // ── Report parsing (1:1 from HC DClawController.Tick) ────────────────────

        /// <summary>
        /// Maps JoystickState to ViGEm Xbox360 state.
        /// Button indices, axis mapping and POV decoding all 1:1 from HC DClawController.Tick().
        /// </summary>
        private void ProcessDirectInputState(JoystickState state)
        {
            ushort xiBtns = 0;

            // Buttons — 1:1 from HC DClawController.Tick():
            //   B1=A=[1], B2=B=[2], B3=X=[0], B4=Y=[3],
            //   L1=LB=[4], R1=RB=[5], Back=[8], Start=[9], LS=[10], RS=[11]
            if (state.Buttons[1])  xiBtns |= XI_A;
            if (state.Buttons[2])  xiBtns |= XI_B;
            if (state.Buttons[0])  xiBtns |= XI_X;
            if (state.Buttons[3])  xiBtns |= XI_Y;
            if (state.Buttons[4])  xiBtns |= XI_LB;
            if (state.Buttons[5])  xiBtns |= XI_RB;
            if (state.Buttons[8])  xiBtns |= XI_BACK;
            if (state.Buttons[9])  xiBtns |= XI_START;
            if (state.Buttons[10]) xiBtns |= XI_LS;
            if (state.Buttons[11]) xiBtns |= XI_RS;

            // D-pad via POV — 1:1 from HC DClawController.Tick().
            // DirectInput reports hundredths of degrees; -1 = centered.
            int pov = state.PointOfViewControllers[0];
            if (pov == 0     || pov == 4500  || pov == 31500) xiBtns |= XI_DPAD_UP;
            if (pov == 9000  || pov == 4500  || pov == 13500) xiBtns |= XI_DPAD_RIGHT;
            if (pov == 18000 || pov == 13500 || pov == 22500) xiBtns |= XI_DPAD_DOWN;
            if (pov == 27000 || pov == 31500 || pov == 22500) xiBtns |= XI_DPAD_LEFT;

            // Axes — 1:1 from HC InputUtils.MapRange:
            //   LeftStickX:  MapRange(state.X,         0,     65535, -32768, 32767)
            //   LeftStickY:  MapRange(state.Y,         65535, 0,     -32768, 32767) ← inverted
            //   RightStickX: MapRange(state.Z,         0,     65535, -32768, 32767)
            //   RightStickY: MapRange(state.RotationZ, 65535, 0,     -32768, 32767) ← inverted
            //   L2 trigger:  MapRange(state.RotationX, 0,     65535, 0,      255)
            //   R2 trigger:  MapRange(state.RotationY, 0,     65535, 0,      255)
            short leftX  = MapToStick(state.X,         invert: false);
            short leftY  = MapToStick(state.Y,         invert: true);
            short rightX = MapToStick(state.Z,         invert: false);
            short rightY = MapToStick(state.RotationZ, invert: true);
            byte  ltrig  = MapToTrigger(state.RotationX);
            byte  rtrig  = MapToTrigger(state.RotationY);

            // M1 / M2 — 1:1 from HC: OEM3 = state.Buttons[15], OEM4 = state.Buttons[16]
            bool m1 = state.Buttons.Length > 15 && state.Buttons[15];
            bool m2 = state.Buttons.Length > 16 && state.Buttons[16];

            // Apply M1/M2 XInput gamepad remapping (mirrors HC LayoutManager.MapController()).
            // While M1/M2 is held, overlay the mapped action onto the outgoing ViGEm state.
            // Physical inputs (sticks/triggers/buttons) are already in xiBtns/ltrig/rtrig/axes;
            // the remap only adds bits / forces axes — it does NOT clear physical inputs.
            ushort xiBtnsOut = xiBtns;
            byte   ltrigOut  = ltrig;
            byte   rtrigOut  = rtrig;
            short  leftXOut  = leftX;
            short  leftYOut  = leftY;
            short  rightXOut = rightX;
            short  rightYOut = rightY;

            // Apply the generic "Re-Map Specific Buttons" swaps (A↔B etc.) to the outgoing
            // ViGEm state. ControllerEmulationManager (which has its own swap pass) is
            // suppressed for MSI Claw, so the swap must live here in the active poll loop.
            // Done BEFORE the M1/M2 overlay so M1/M2-injected actions stack on the swapped
            // base; uses the Out vars only, so the raw xiBtns fed to hotkeys stays physical.
            ApplyGamepadSwaps(ref xiBtnsOut, ref ltrigOut, ref rtrigOut,
                ref leftXOut, ref leftYOut, ref rightXOut, ref rightYOut);

            // Stick deadzones — radial inner deadzone on the physical stick values before the
            // M1/M2 remap overlay and gyro merge, so gyro/mouse build on the deadzoned base.
            ApplyStickDeadzone(ref leftXOut,  ref leftYOut,  _leftStickDeadzone);
            ApplyStickDeadzone(ref rightXOut, ref rightYOut, _rightStickDeadzone);

            if (m1 && _m1RemapAction != RemapAction.Disabled)
                ApplyXInputRemapAction(_m1RemapAction,
                    ref xiBtnsOut, ref ltrigOut, ref rtrigOut,
                    ref leftXOut, ref leftYOut, ref rightXOut, ref rightYOut);

            if (m2 && _m2RemapAction != RemapAction.Disabled)
                ApplyXInputRemapAction(_m2RemapAction,
                    ref xiBtnsOut, ref ltrigOut, ref rtrigOut,
                    ref leftXOut, ref leftYOut, ref rightXOut, ref rightYOut);

            // Gyro processing — MSI Claw path.
            // ControllerEmulationManager is suppressed for MSIClaw (SetSuppressedByViiper),
            // so gyro must live here in the DInput poll loop (1:1 from HC MotionManager gate logic).
            int gyroTarget = _gyroTarget;
            var gyroAdapterLocal = _gyroAdapter;
            if (gyroTarget != 0 && gyroAdapterLocal != null)
            {
                try
                {
                    if (gyroAdapterLocal.TryGetLatestSample(out GyroSample gyroSample))
                    {
                        bool gyroActive = IsGyroActive(state);
                        if (gyroActive)
                        {
                            if (gyroTarget == 3) // Mouse
                            {
                                ApplyGyroMouse(gyroSample);
                            }
                            else // 1=LeftStick, 2=RightStick
                            {
                                ApplyGyroToStick(gyroSample, out short gx, out short gy);
                                if (gyroTarget == 1)
                                    MergeGyroStick(ref leftXOut, ref leftYOut, gx, gy);
                                else
                                    MergeGyroStick(ref rightXOut, ref rightYOut, gx, gy);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"ClawButtonMonitor: Gyro processing exception: {ex.Message}");
                }
            }

            // ── Mouse mode — translate stick/button inputs to Windows mouse events ──
            // Physical controller stays in DInput mode, HidHide stays active, ViGEm stays
            // plugged in (so hotkeys keep working). This is the HC fork virtual approach.
            if (_mouseModeEnabled)
            {
                // Configurable button → left/right click
                bool leftClickDown  = GetMouseModeButton(_mouseLeftClickButton,  xiBtnsOut, ltrigOut, rtrigOut);
                bool rightClickDown = GetMouseModeButton(_mouseRightClickButton, xiBtnsOut, ltrigOut, rtrigOut);

                if (leftClickDown && !_mouseLbWasDown)
                    mouse_event(MOUSEEVENTF_LEFTDOWN,  0, 0, 0, IntPtr.Zero);
                else if (!leftClickDown && _mouseLbWasDown)
                    mouse_event(MOUSEEVENTF_LEFTUP,    0, 0, 0, IntPtr.Zero);
                _mouseLbWasDown = leftClickDown;

                if (rightClickDown && !_mouseRbWasDown)
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
                else if (!rightClickDown && _mouseRbWasDown)
                    mouse_event(MOUSEEVENTF_RIGHTUP,   0, 0, 0, IntPtr.Zero);
                _mouseRbWasDown = rightClickDown;

                // Configurable cursor stick
                short cursorX = _mouseCursorStick == 0 ? rightXOut : leftXOut;
                short cursorY = _mouseCursorStick == 0 ? rightYOut : leftYOut;
                float fx = cursorX / 32767f;
                float fy = -(cursorY / 32767f);

                float deadzone = _mouseModeThreshold / 100.0f;
                float sensitivity = _mouseModeSensitivity * MouseSensitivityScale;
                if (Math.Abs(fx) < deadzone) fx = 0f;
                if (Math.Abs(fy) < deadzone) fy = 0f;

                if (fx != 0f || fy != 0f)
                {
                    _mouseCarryX += fx * sensitivity;
                    _mouseCarryY += fy * sensitivity;
                    int dx = (int)_mouseCarryX;
                    int dy = (int)_mouseCarryY;
                    _mouseCarryX -= dx;
                    _mouseCarryY -= dy;
                    if (dx != 0 || dy != 0)
                        mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, IntPtr.Zero);
                }
                else
                {
                    _mouseCarryX = 0f;
                    _mouseCarryY = 0f;
                }

                // Configurable scroll stick Y — fractional WHEEL_DELTA for smooth browser scrolling.
                // Accumulate sub-unit carry and send delta each tick the stick is active.
                // Fires from tick 1 (no 104ms startup delay), sends small continuous values
                // that modern browsers smooth-scroll rather than stepping in full notches.
                short scrollRaw = _mouseScrollStick == 0 ? leftYOut : rightYOut;
                float scrollY = scrollRaw / 32767f;
                if (Math.Abs(scrollY) < deadzone)
                {
                    _mouseScrollAccum = 0f;
                }
                else
                {
                    // Fractional WHEEL_DELTA via SendInput: fires from tick 1 (no startup delay),
                    // sends small continuous values that browsers smooth-scroll.
                    // Win32 apps (Explorer) require a prior click to accept focus before scrolling.
                    _mouseScrollAccum += scrollY * MouseModeScrollRate * 120f;
                    int delta = (int)_mouseScrollAccum;
                    if (delta != 0)
                    {
                        SendMouseWheel(delta);
                        _mouseScrollAccum -= delta;
                    }
                }

                // Suppress the button(s) mapped to a mouse click from the ViGEm submit, so a
                // click button only produces the Windows mouse event and NOT a phantom gamepad
                // press. This matters above all for A: Xbox-A is the Game Bar's "confirm/activate"
                // button, so a phantom A would be eaten by the overlay's gamepad nav (the reason
                // A "wouldn't map" while B/X/Y/LB/RB did). Triggers are already zeroed below, and
                // hotkeys read the physical xiBtns feed, so neither is affected.
                xiBtnsOut &= (ushort)~(GetMouseModeButtonMask(_mouseLeftClickButton)
                                       | GetMouseModeButtonMask(_mouseRightClickButton));

                // Game Bar auto-nav injection (RB hops + focus drop) — overlay onto the button mask.
                { ushort injMask = ServiceInjection(); if (injMask != 0) xiBtnsOut |= injMask; }

                // Still submit to ViGEm so hotkeys keep working, but zero out the
                // sticks and triggers so games don't receive phantom gamepad input.
                if (_vigem != null && _vigem.IsPluggedIn)
                    _vigem.SubmitXboxState(xiBtnsOut, 0, 0, 0, 0, 0, 0);
                // Experimental VIIPER backend: same assembled state, same zeroing.
                if (_viiperEnabled)
                    SubmitViiperState(xiBtnsOut, 0, 0, 0, 0, 0, 0);
            }
            else
            {
                // Game Bar auto-nav injection (RB hops + focus drop) — overlay onto the button mask.
                { ushort injMask = ServiceInjection(); if (injMask != 0) xiBtnsOut |= injMask; }

                // Normal controller mode — forward everything to ViGEm
                if (_vigem != null && _vigem.IsPluggedIn)
                    _vigem.SubmitXboxState(xiBtnsOut, ltrigOut, rtrigOut, leftXOut, leftYOut, rightXOut, rightYOut);
                // Experimental VIIPER backend: same assembled state goes to the VIIPER device.
                if (_viiperEnabled)
                    SubmitViiperState(xiBtnsOut, ltrigOut, rtrigOut, leftXOut, leftYOut, rightXOut, rightYOut);
            }

            // Service the Xbox Guide momentary tap AFTER SubmitXboxState (which doesn't touch
            // Guide), so a requested tap presses/holds/releases cleanly on this poll thread.
            ServiceGuideTap();

            // Feed ControllerHotkeyMonitor — 1:1 HC pattern: single DirectInput reader,
            // hotkey system reads from already-computed ButtonState (no second DI instance).
            // Bit layout matches ControllerHotkeyMonitor.BTN_* constants exactly (same as XInput wButtons).
            var hotkeyFeedLocal = HotkeyFeed;
            if (hotkeyFeedLocal != null)
            {
                uint hotkeyMask = (uint)xiBtns
                    | (m1 ? 0x10000u : 0u)   // BTN_M1
                    | (m2 ? 0x20000u : 0u);   // BTN_M2
                hotkeyFeedLocal(hotkeyMask);
            }

            // M1/M2 edge-detection logging (press/release only — not per-frame).
            // Info level so it appears in the log at the default minlevel="Info" NLog config.
            // Shows: was the button detected? what XInput remap is active? is callback path enabled?
            if (m1 != _prevM1) Logger.Info($"ClawButtonMonitor: M1 {(m1 ? "↓ pressed" : "↑ released")} — remapAction={_m1RemapAction}, callbackEnabled={_m1Enabled}");
            if (m2 != _prevM2) Logger.Info($"ClawButtonMonitor: M2 {(m2 ? "↓ pressed" : "↑ released")} — remapAction={_m2RemapAction}, callbackEnabled={_m2Enabled}");

            // M1/M2 → "Xbox Button" gamepad remap (LegionButtonM1/M2ComboBox index 25): fire a
            // momentary Guide tap on the press edge (Guide can't ride the SubmitXboxState button
            // mask, so ApplyXInputRemapAction is a no-op for it — handled here instead).
            if (m1 && !_prevM1 && _m1RemapAction == RemapAction.XboxGuide) TriggerGuideTap();
            if (m2 && !_prevM2 && _m2RemapAction == RemapAction.XboxGuide) TriggerGuideTap();

            // M1/M2 Keyboard target (Type==1): fire the configured chord once on the press edge,
            // mirroring the normal-button keyboard path (GamepadSwap IsKeyboard). Uses the same
            // KeyboardChordCallback (→ SendKeyboardShortcutViaInputInjector) wired by Program.MSIClaw.
            if (m1 && !_prevM1 && _m1MappingType == 1 && !string.IsNullOrEmpty(_m1KeyboardToken))
            {
                try { KeyboardChordCallback?.Invoke(_m1KeyboardToken); }
                catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: M1 keyboard chord '{_m1KeyboardToken}' failed: {ex.Message}"); }
            }
            if (m2 && !_prevM2 && _m2MappingType == 1 && !string.IsNullOrEmpty(_m2KeyboardToken))
            {
                try { KeyboardChordCallback?.Invoke(_m2KeyboardToken); }
                catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: M2 keyboard chord '{_m2KeyboardToken}' failed: {ex.Message}"); }
            }

            // M1/M2 Mouse target (Type==2): clicks press/release with the button edge; scroll on down.
            if (_m1MappingType == 2 && _m1MouseAction >= 0)
            {
                if (m1 && !_prevM1) FireBackButtonMouse(_m1MouseAction, true);
                if (!m1 && _prevM1) FireBackButtonMouse(_m1MouseAction, false);
            }
            if (_m2MappingType == 2 && _m2MouseAction >= 0)
            {
                if (m2 && !_prevM2) FireBackButtonMouse(_m2MouseAction, true);
                if (!m2 && _prevM2) FireBackButtonMouse(_m2MouseAction, false);
            }

            // M1/M2 edge detection and callback dispatch (for non-XInput actions: shortcuts/commands/Guide)
            if (m1  && !_prevM1) OnButtonDown(true);
            if (!m1 &&  _prevM1) OnButtonUp(true);
            if (m2  && !_prevM2) OnButtonDown(false);
            if (!m2 &&  _prevM2) OnButtonUp(false);
            _prevM1 = m1;
            _prevM2 = m2;
        }

        /// <summary>
        /// MapRange(raw, 0, 65535, -32768, 32767)  or  MapRange(raw, 65535, 0, -32768, 32767).
        /// 1:1 from HC InputUtils.MapRange used in DClawController.Tick().
        /// </summary>
        // Button index: 0=None,1=A,2=B,3=X,4=Y,5=LB,6=RB,7=LS,8=RS,9=LT,10=RT
        // LT/RT: ltrig/rtrig are DirectInput RotationX/Y mapped to 0–255 via MapToTrigger().
        // Threshold 30 matches HC fork's XInput TriggerThreshold constant exactly.
        private const byte MouseTriggerThreshold = 30;
        private static bool GetMouseModeButton(int buttonIndex, ushort xiBtns, byte ltrig, byte rtrig)
        {
            return buttonIndex switch
            {
                1  => (xiBtns & XI_A)  != 0,
                2  => (xiBtns & XI_B)  != 0,
                3  => (xiBtns & XI_X)  != 0,
                4  => (xiBtns & XI_Y)  != 0,
                5  => (xiBtns & XI_LB) != 0,
                6  => (xiBtns & XI_RB) != 0,
                7  => (xiBtns & XI_LS) != 0,
                8  => (xiBtns & XI_RS) != 0,
                9  => ltrig  > MouseTriggerThreshold, // LT — HC TriggerThreshold = 30
                10 => rtrig  > MouseTriggerThreshold, // RT — HC TriggerThreshold = 30
                _  => false,
            };
        }

        /// <summary>
        /// XInput button-mask for a mouse-click button index, used to strip the click button from
        /// the ViGEm submit in mouse mode (so it doesn't double as a phantom gamepad press).
        /// None (0) and the analog triggers LT/RT (9/10) return 0 — triggers are already zeroed on
        /// submit, so there's nothing to mask there.
        /// </summary>
        private static ushort GetMouseModeButtonMask(int buttonIndex)
        {
            return buttonIndex switch
            {
                1  => XI_A,
                2  => XI_B,
                3  => XI_X,
                4  => XI_Y,
                5  => XI_LB,
                6  => XI_RB,
                7  => XI_LS,
                8  => XI_RS,
                _  => (ushort)0,
            };
        }

        private static short MapToStick(int raw, bool invert)
        {
            // Non-inverted: v = raw - 32768
            // Inverted:     v = 32767 - raw  (= -(raw - 32767) ≈ -(raw - 32768))
            int v = invert ? (32767 - raw) : (raw - 32768);
            if (v >  32767) v =  32767;
            if (v < -32767) v = -32767;
            return (short)v;
        }

        /// <summary>
        /// MapRange(raw, 0, 65535, 0, 255).
        /// 1:1 from HC InputUtils.MapRange used in DClawController.Tick().
        /// </summary>
        private static byte MapToTrigger(int raw)
        {
            // 65535 / 257 ≈ 255; shifting right by 8 is the fast equivalent.
            int clamped = Math.Max(0, Math.Min(65535, raw));
            return (byte)(clamped >> 8);
        }

        // ── XInput remap application (mirrors ControllerEmulationManager.ApplyGamepadRemapAction) ──

        /// <summary>
        /// Apply a RemapAction to the pending ViGEm XInput state parameters.
        ///
        /// 1:1 mirror of ControllerEmulationManager.ApplyGamepadRemapAction() operating on
        /// ClawButtonMonitor's individual parameters rather than XINPUT_GAMEPAD struct fields.
        /// Called from ProcessDirectInputState() while M1/M2 is held and a remap is configured.
        /// </summary>
        private static void ApplyXInputRemapAction(RemapAction action,
            ref ushort xiBtns, ref byte ltrig, ref byte rtrig,
            ref short leftX, ref short leftY, ref short rightX, ref short rightY)
        {
            switch (action)
            {
                case RemapAction.LeftStickClick:  xiBtns |= XI_LS;           break;
                case RemapAction.LeftStickUp:     leftY   = short.MaxValue;  break;
                case RemapAction.LeftStickDown:   leftY   = short.MinValue;  break;
                case RemapAction.LeftStickLeft:   leftX   = short.MinValue;  break;
                case RemapAction.LeftStickRight:  leftX   = short.MaxValue;  break;
                case RemapAction.RightStickClick: xiBtns |= XI_RS;           break;
                case RemapAction.RightStickUp:    rightY  = short.MaxValue;  break;
                case RemapAction.RightStickDown:  rightY  = short.MinValue;  break;
                case RemapAction.RightStickLeft:  rightX  = short.MinValue;  break;
                case RemapAction.RightStickRight: rightX  = short.MaxValue;  break;
                case RemapAction.DpadUp:          xiBtns |= XI_DPAD_UP;      break;
                case RemapAction.DpadDown:        xiBtns |= XI_DPAD_DOWN;    break;
                case RemapAction.DpadLeft:        xiBtns |= XI_DPAD_LEFT;    break;
                case RemapAction.DpadRight:       xiBtns |= XI_DPAD_RIGHT;   break;
                case RemapAction.A:               xiBtns |= XI_A;            break;
                case RemapAction.B:               xiBtns |= XI_B;            break;
                case RemapAction.X:               xiBtns |= XI_X;            break;
                case RemapAction.Y:               xiBtns |= XI_Y;            break;
                case RemapAction.LeftBumper:      xiBtns |= XI_LB;           break;
                case RemapAction.LeftTrigger:     ltrig   = byte.MaxValue;   break;
                case RemapAction.RightBumper:     xiBtns |= XI_RB;           break;
                case RemapAction.RightTrigger:    rtrig   = byte.MaxValue;   break;
                case RemapAction.View:            xiBtns |= XI_BACK;         break;
                case RemapAction.Menu:            xiBtns |= XI_START;        break;
                // RemapAction.XboxGuide: Guide is not part of the SubmitXboxState button mask
                // (it's set separately via ViGEmController.SetGuide). Fired as a momentary tap on
                // the press edge by the caller (TriggerGuideTap), so it's a no-op here.
                // RemapAction.Disabled, DesktopButton, PageButton: no-op (not XInput actions)
            }
        }

        // ── M1/M2 press/release dispatch (mirrors LegionButtonMonitor) ────────────

        private void OnButtonDown(bool isM1)
        {
            bool enabled  = isM1 ? _m1Enabled  : _m2Enabled;
            if (!enabled) return;
            var  action   = isM1 ? _m1Action   : _m2Action;
            string keys   = isM1 ? _m1Keys     : _m2Keys;
            string cmd    = isM1 ? _m1Cmd      : _m2Cmd;
            string name   = isM1 ? "M1"        : "M2";

            Logger.Debug($"ClawButtonMonitor: {name} pressed — action={action}");

            switch (action)
            {
                case LegionButtonAction.XboxGuide:
                    if (ControllerEmulation.Viiper.ViiperInputForwarder.TryHandleGuidePressFromLabs())
                        break;
                    _vigem?.SetGuide(true);
                    break;

                case LegionButtonAction.KeyboardShortcut:
                    if (!string.IsNullOrEmpty(keys))
                        try { _shortcutCb?.Invoke(keys); } catch (Exception ex) { Logger.Error($"ClawButtonMonitor: shortcut ex: {ex.Message}"); }
                    break;

                case LegionButtonAction.RunCommand:
                    if (!string.IsNullOrEmpty(cmd))
                        try { _cmdCb?.Invoke(cmd); } catch (Exception ex) { Logger.Error($"ClawButtonMonitor: cmd ex: {ex.Message}"); }
                    break;

                case LegionButtonAction.FocusGoTweaks:
                    try { _focusCb?.Invoke(); } catch (Exception ex) { Logger.Error($"ClawButtonMonitor: focus ex: {ex.Message}"); }
                    break;
            }
        }

        private void OnButtonUp(bool isM1)
        {
            bool enabled = isM1 ? _m1Enabled : _m2Enabled;
            if (!enabled) return;
            var action   = isM1 ? _m1Action  : _m2Action;

            if (action == LegionButtonAction.XboxGuide)
            {
                if (!ControllerEmulation.Viiper.ViiperInputForwarder.TryHandleGuidePressFromLabs())
                    _vigem?.SetGuide(false);
            }
        }

        // ── Xbox Guide tap ──────────────────────────────────────────────────────────

        /// <summary>
        /// Request a momentary Xbox Guide press on the virtual ViGEm Xbox 360 controller (opens
        /// Steam Big Picture / the in-game Steam overlay). Safe to call from any thread — it only
        /// extends a release deadline; the poll loop (ServiceGuideTap) performs the actual ViGEm
        /// SetGuide(true)/SetGuide(false) on its own thread, keeping all ViGEm writes single-threaded.
        /// Re-tapping while still held simply extends the window (no double-press).
        /// </summary>
        public void TriggerGuideTap()
        {
            System.Threading.Interlocked.Exchange(ref _guideHoldUntilTicks,
                DateTime.UtcNow.AddMilliseconds(GuideTapDurationMs).Ticks);
            Logger.Info("ClawButtonMonitor: Xbox Guide tap requested");
        }

        /// <summary>
        /// Poll-thread service for the Guide tap: presses Guide while within the deadline window and
        /// releases it once the window elapses. Called every poll iteration after SubmitXboxState
        /// (which does not touch Guide, so the press persists across frames until released here).
        /// </summary>
        private void ServiceGuideTap()
        {
            long until = System.Threading.Interlocked.Read(ref _guideHoldUntilTicks);
            bool shouldPress = until != 0 && DateTime.UtcNow.Ticks < until;

            if (shouldPress && !_guideActive)
            {
                if (_vigem != null && _vigem.IsPluggedIn)
                {
                    _vigem.SetGuide(true);
                    _guideActive = true;
                }
                else
                {
                    // No virtual controller (e.g. External Gamepad Mode) — drop the request.
                    System.Threading.Interlocked.Exchange(ref _guideHoldUntilTicks, 0);
                }
            }
            else if (!shouldPress && _guideActive)
            {
                _vigem?.SetGuide(false);
                _guideActive = false;
                System.Threading.Interlocked.Exchange(ref _guideHoldUntilTicks, 0);
            }
        }

        // ── RB auto-navigation tap injection ────────────────────────────────────────

        /// <summary>
        /// Enqueue the full Game Bar auto-navigation: <paramref name="rbCount"/> RB hops to land on
        /// ClawTweaks, then one D-pad Down to drop focus into the widget body (so RT/LT tab nav works
        /// right away). Safe from any thread — only enqueues; the poll loop drives the timed taps.
        /// </summary>
        public void InjectGameBarNav(int rbCount)
        {
            lock (_injectLock)
            {
                if (rbCount > 0)
                    _injectQueue.Enqueue(new InjectGroup { Mask = XI_RB, Remaining = rbCount, PressMs = RbTapPressMs, GapMs = RbTapGapMs });
                _injectQueue.Enqueue(new InjectGroup { Mask = XI_DPAD_DOWN, Remaining = 1, PressMs = DpadTapPressMs, GapMs = DpadTapGapMs });
            }
            Logger.Info($"ClawButtonMonitor: Game Bar nav injection requested (RB×{rbCount} + DpadDown×1)");
        }

        /// <summary>Back-compat: inject just RB taps (no focus drop).</summary>
        public void InjectRbTaps(int count)
        {
            if (count <= 0) return;
            lock (_injectLock)
                _injectQueue.Enqueue(new InjectGroup { Mask = XI_RB, Remaining = count, PressMs = RbTapPressMs, GapMs = RbTapGapMs });
            Logger.Info($"ClawButtonMonitor: RB tap injection requested (count={count})");
        }

        /// <summary>
        /// Poll-thread driver for the injection queue. Returns the button mask to OR into the
        /// outgoing state this frame (0 = nothing). Walks each group press→gap→press…, then the
        /// next group, then idle.
        /// </summary>
        private ushort ServiceInjection()
        {
            lock (_injectLock)
            {
                if (_injectCurrent == null)
                {
                    if (_injectQueue.Count == 0) return 0;
                    _injectCurrent = _injectQueue.Dequeue();
                    _injectPressing = false;
                    _injectPhaseUntilTicks = 0;
                }

                long now = DateTime.UtcNow.Ticks;
                InjectGroup g = _injectCurrent;

                if (_injectPhaseUntilTicks == 0)
                {
                    // Begin a press phase.
                    _injectPressing = true;
                    _injectPhaseUntilTicks = now + g.PressMs * TimeSpan.TicksPerMillisecond;
                    return g.Mask;
                }

                if (now < _injectPhaseUntilTicks)
                {
                    return _injectPressing ? g.Mask : (ushort)0; // still within the current phase
                }

                // Current phase elapsed → flip.
                if (_injectPressing)
                {
                    _injectPressing = false;
                    g.Remaining--;
                    _injectPhaseUntilTicks = now + g.GapMs * TimeSpan.TicksPerMillisecond;
                    return 0;
                }

                // Gap finished → next tap in this group, or advance to the next group.
                if (g.Remaining <= 0)
                {
                    _injectCurrent = null;       // pick up the next group on the following tick
                    _injectPhaseUntilTicks = 0;
                    return 0;
                }
                _injectPressing = true;
                _injectPhaseUntilTicks = now + g.PressMs * TimeSpan.TicksPerMillisecond;
                return g.Mask;
            }
        }

        // ── ViGEm helper ──────────────────────────────────────────────────────────

        private bool EnsureViGEm()
        {
            if (_vigem != null) return true;
            _vigem = new ViGEmController();
            _ownsVigem = true;
            if (!_vigem.Connect() || !_vigem.PlugIn())
            {
                Logger.Error("ClawButtonMonitor: Failed to create ViGEm virtual controller");
                _vigem.Dispose(); _vigem = null; _ownsVigem = false;
                return false;
            }
            // Forward game force-feedback to the physical Claw (the "missing link":
            // previously the virtual controller was created but RumbleReceived was never wired,
            // so the Claw never vibrated).
            _vigem.RumbleReceived += OnRumbleReceived;
            Logger.Info("ClawButtonMonitor: ViGEm Xbox360 controller plugged in (rumble forwarding enabled)");
            return true;
        }

        // ── Vibration: ViGEm feedback → physical Claw HID ─────────────────────────

        /// <summary>
        /// ViGEm rumble feedback callback. Forwards the motor magnitudes to the physical Claw
        /// via a direct HID report (1:1 from HC DClawController). Fires on a ViGEm callback
        /// thread. Dedupes unchanged values (HC prevLarge/prevSmall pattern).
        /// </summary>
        /// <summary>
        /// Fires a short test rumble pulse at the current intensity, independent of any game FFB,
        /// so the user can verify vibration + intensity without launching a game. Also a diagnostic:
        /// if this pulse is felt, the HID write path works and the issue is the game's FFB not
        /// reaching the virtual controller; if not, the write path itself is the problem (see log).
        /// </summary>
        public void TestVibration()
        {
            try
            {
                Logger.Info($"ClawButtonMonitor: test vibration pulse (running={_running}, intensity={(int)(_vibrationIntensity * 100)}%)");
                WriteRumble(200, 200);
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try { await System.Threading.Tasks.Task.Delay(350); } catch { }
                    WriteRumble(0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"ClawButtonMonitor: TestVibration failed: {ex.Message}");
            }
        }

        private bool _rumbleEverReceived;
        private void OnRumbleReceived(byte largeMotor, byte smallMotor)
        {
            try
            {
                // Confirm (once) that the virtual controller's force-feedback actually reaches us —
                // if this never logs, the game's rumble isn't arriving at this ViGEm controller.
                if (!_rumbleEverReceived)
                {
                    _rumbleEverReceived = true;
                    Logger.Info($"ClawButtonMonitor: first rumble feedback received (large={largeMotor}, small={smallMotor})");
                }
                WriteRumble(largeMotor, smallMotor);
            }
            catch (Exception ex)
            {
                Logger.Debug($"ClawButtonMonitor: OnRumbleReceived failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes the scaled rumble report to the physical Claw's vendor command interface.
        /// Report layout 1:1 from HC DClawController.WriteVibration:
        ///   { 0x05, 0x01, 0x00, 0x00, small*intensity, large*intensity, 0… }
        /// (small motor at index 4, large motor at index 5). Sent over the SAME _cmdDevice
        /// used for SwitchMode / M1-M2 / LED (the vendor interface, present in DInput mode at
        /// 0xFFF0). Guarded by _rumbleLock because the monitor thread also uses _cmdDevice.
        /// </summary>
        private void WriteRumble(byte large, byte small)
        {
            lock (_rumbleLock)
            {
                if (!_running) { Logger.Info("ClawButtonMonitor: WriteRumble skipped — monitor not running"); return; }
                if (large == _prevRumbleLarge && small == _prevRumbleSmall) return; // dedupe

                _prevRumbleLarge = large;
                _prevRumbleSmall = small;

                // Resolve (and cache) the GAMEPAD HID interface (Generic Desktop / Joystick|Gamepad).
                // HC writes the 0x05 rumble report to the controller HID, NOT the vendor/LED interface
                // (which rejects 0x05 with "Incorrect function").
                var dev = _rumbleDevice;
                if (dev == null)
                {
                    dev = _rumbleDevice = FindGamepadHidDevice();
                }
                if (dev == null) { Logger.Info("ClawButtonMonitor: WriteRumble skipped — gamepad HID interface not found"); return; }

                float intensity = _vibrationIntensity;
                byte scaledSmall = (byte)(small * intensity);
                byte scaledLarge = (byte)(large * intensity);

                // Size the report to the device's output report length (HC/hidapi pad to this).
                int outLen = 0;
                try { outLen = dev.GetMaxOutputReportLength(); } catch { }
                if (outLen <= 0) outLen = 64;

                byte[] report = new byte[outLen];
                report[0] = RUMBLE_REPORT_ID; // 0x05
                report[1] = 0x01;
                report[4] = scaledSmall;
                report[5] = scaledLarge;

                // hidapi-faithful: open the gamepad HID with SHARED read/write (HidSharp's exclusive
                // Open() fails with "Unable to open" on this interface) and WriteFile the report.
                bool ok = SharedHidWrite(dev.DevicePath, report);
                if (ok)
                {
                    Logger.Debug($"ClawButtonMonitor: rumble written large={large}->{scaledLarge} small={small}->{scaledSmall} (intensity={(int)(intensity * 100)}%, len={outLen})");
                }
                else
                {
                    Logger.Warn($"ClawButtonMonitor: WriteRumble shared write failed (path={dev.DevicePath}, err={Marshal.GetLastWin32Error()})");
                    _rumbleDevice = null; // re-find next time (device may have re-enumerated)
                }
            }
        }

        // ── Rumble HID plumbing (hidapi-faithful shared write) ────────────────────

        /// <summary>
        /// Finds the MSI Claw GAMEPAD HID interface (Generic Desktop usage page 0x01,
        /// usage 0x04 Joystick or 0x05 Game Pad) — the interface that accepts the 0x05
        /// rumble report (mirrors HC's controller HID, not the vendor/LED interface).
        ///
        /// Critically, it PREFERS the DInput controller (PID 0x1902) — the same device the monitor
        /// reads input from in this mode and the one whose firmware actually actuates the 0x05 rumble.
        /// The XInput interface (PID 0x1901) also exposes a Generic-Desktop Game Pad usage, so a naive
        /// "first match" would non-deterministically pick it after a reboot (HidHide's cycle-port
        /// re-enumeration shuffles the device order) — the write then "succeeds" but the Claw never
        /// buzzes. Two-pass: PID 0x1902 first, any Claw gamepad as fallback.
        /// </summary>
        private static HidDevice FindGamepadHidDevice()
        {
            try
            {
                HidDevice fallback = null;
                foreach (var d in DeviceList.Local.GetHidDevices())
                {
                    if (d.VendorID != CLAW_VID) continue;
                    try
                    {
                        var desc = d.GetReportDescriptor();
                        if (desc == null) continue;
                        bool isGamepad = false;
                        foreach (var item in desc.DeviceItems)
                            foreach (uint enc in item.Usages.GetAllValues())
                            {
                                int page  = (int)((enc >> 16) & 0xFFFF);
                                int usage = (int)(enc & 0xFFFF);
                                if (page == 0x01 && (usage == 0x04 || usage == 0x05)) { isGamepad = true; break; }
                            }
                        if (!isGamepad) continue;

                        if (d.ProductID == CLAW_PID_DINPUT)
                        {
                            Logger.Info($"ClawButtonMonitor: rumble interface = DInput PID 0x{CLAW_PID_DINPUT:X4} ({d.DevicePath})");
                            return d; // the active DInput controller — actuates rumble
                        }
                        if (fallback == null) fallback = d; // e.g. XInput PID 0x1901 — only if no DInput found
                    }
                    catch { /* restricted interface — skip */ }
                }

                if (fallback != null)
                    Logger.Warn($"ClawButtonMonitor: rumble interface = fallback PID 0x{fallback.ProductID:X4} (DInput PID 0x{CLAW_PID_DINPUT:X4} not found — rumble may not actuate) ({fallback.DevicePath})");
                return fallback;
            }
            catch (Exception ex)
            {
                Logger.Warn($"ClawButtonMonitor: FindGamepadHidDevice failed: {ex.Message}");
            }
            return null;
        }

        // Win32 HID open/write — hidapi opens with GENERIC_READ|GENERIC_WRITE + FILE_SHARE_READ|WRITE,
        // which lets it write to the gamepad HID even while the system holds it (HidSharp's exclusive
        // Open() fails with "Unable to open HID class device"). Replicate that here.
        private const uint GENERIC_READ  = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ  = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>Opens a HID path with shared read/write (hidapi-style) and writes one report.</summary>
        private static bool SharedHidWrite(string devicePath, byte[] data)
        {
            if (string.IsNullOrEmpty(devicePath)) return false;
            IntPtr h = CreateFileW(devicePath, GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID_HANDLE_VALUE)
            {
                // Retry write-only — some interfaces deny read access.
                h = CreateFileW(devicePath, GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == INVALID_HANDLE_VALUE) return false;
            }
            try
            {
                return WriteFile(h, data, (uint)data.Length, out _, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(h);
            }
        }

        // ── Gyro adapter lifecycle ────────────────────────────────────────────────

        /// <summary>
        /// Start or stop ClawGyroSourceAdapter based on the current _gyroTarget.
        /// Called from SetGyroTarget() and Start(). Safe to call when not running
        /// (will be a no-op if _running is false).
        /// </summary>
        private void UpdateGyroAdapter()
        {
            if (!_running || _gyroTarget == 0)
            {
                StopGyroAdapter();
                return;
            }
            if (_gyroAdapter != null) return; // already running
            var adapter = new ClawGyroSourceAdapter();
            if (!adapter.Start())
            {
                adapter.Dispose();
                Logger.Warn("ClawButtonMonitor: ClawGyroSourceAdapter failed to start");
                return;
            }
            _gyroAdapter = adapter;
            Logger.Info("ClawButtonMonitor: ClawGyroSourceAdapter started");
        }

        private void StopGyroAdapter()
        {
            var adapter = _gyroAdapter;
            _gyroAdapter = null;
            if (adapter == null) return;
            try { adapter.Stop(); } catch { }
            try { adapter.Dispose(); } catch { }
            // Reset all filter / activation state so next Start() is clean.
            _gyroToggleActive = false;
            _prevGyroButtonPressed = false;
            _gyroCarryX = _gyroCarryY = 0;
            _gyroFilterInit = false;
            _gyroLastSampleTicks = 0;
            Logger.Info("ClawButtonMonitor: ClawGyroSourceAdapter stopped");
        }

        // ── Gyro activation gate (HC MotionManager.IsActive / MotionMode port) ───

        /// <summary>
        /// Returns true when gyro output should be applied this frame.
        /// Ported 1:1 from HC MotionManager gate logic:
        ///   _gyroActivationMode 0 = Hold  (HC MotionMode.Off  → button held → active)
        ///   _gyroActivationMode 1 = Toggle (HC MotionMode.Toggle → rising edge → flip)
        ///   _gyroActivationButton 0 = no button → always active when target != Disabled
        /// </summary>
        private bool IsGyroActive(JoystickState state)
        {
            int button = _gyroActivationButton;
            if (button == 0)
            {
                LogGyroActiveChange(true, state, button);
                return true; // No button configured → always active (HC: no activation button)
            }

            bool pressed = IsGyroButtonPressed(state, button);

            bool result;
            if (_gyroActivationMode == 1) // Toggle: rising edge flips active state
            {
                if (pressed && !_prevGyroButtonPressed)
                    _gyroToggleActive = !_gyroToggleActive;
                _prevGyroButtonPressed = pressed;
                result = _gyroToggleActive;
            }
            else // 0 = Hold: active while button is held
            {
                _prevGyroButtonPressed = pressed;
                result = pressed;
            }

            LogGyroActiveChange(result, state, button);
            return result;
        }

        /// <summary>
        /// Log gyro activation state changes and periodic diagnostics.
        /// Fires on transition (active↔inactive) so we can diagnose button detection issues.
        /// </summary>
        private void LogGyroActiveChange(bool active, JoystickState state, int button)
        {
            if (active != _lastGyroActiveState || !_lastGyroActiveLogged)
            {
                // Build a button-state snapshot for diagnostic purposes.
                // Shows raw RotationX/Y (trigger analog) and Buttons[4..7] (LB/RB/LT-digital/RT-digital).
                string lb  = (state.Buttons.Length > 4 && state.Buttons[4]) ? "LB↓" : "LB↑";
                string rb  = (state.Buttons.Length > 5 && state.Buttons[5]) ? "RB↓" : "RB↑";
                string lt2 = (state.Buttons.Length > 6 && state.Buttons[6]) ? "LT_dig↓" : "LT_dig↑";
                string rt2 = (state.Buttons.Length > 7 && state.Buttons[7]) ? "RT_dig↓" : "RT_dig↑";
                Logger.Info(
                    $"ClawButtonMonitor Gyro: active={active} (was={_lastGyroActiveState}) " +
                    $"activationButton={button} activationMode={_gyroActivationMode} " +
                    $"RotationX={state.RotationX} RotationY={state.RotationY} " +
                    $"{lb} {rb} {lt2} {rt2}");
                _lastGyroActiveState  = active;
                _lastGyroActiveLogged = true;
            }
        }

        /// <summary>
        /// DInput button mapping for MSI Claw activation buttons.
        /// Values match LegionGyroActivationButton enum for MSIClaw:
        ///   1=LB (Buttons[4]), 2=LT (RotationX analog), 3=RB (Buttons[5]), 4=RT (RotationY analog).
        /// Threshold of 8192 on analog triggers mirrors HC's XINPUT_TRIGGER_THRESHOLD (30/255 × 65535 ≈ 7710).
        /// </summary>
        private static bool IsGyroButtonPressed(JoystickState state, int button)
        {
            switch (button)
            {
                case 1: return state.Buttons.Length > 4 && state.Buttons[4];  // LB
                case 2: return state.RotationX > 8192;                         // LT (analog, range 0-65535)
                case 3: return state.Buttons.Length > 5 && state.Buttons[5];  // RB
                case 4: return state.RotationY > 8192;                         // RT (analog)
                default: return false;
            }
        }

        // ── Gyro-to-stick (simplified HC ApplyStickFromGyro port) ────────────────

        /// <summary>
        /// Converts a gyro sample to a virtual stick deflection.
        /// Axis mapping 1:1 from HC MotionManager.LocalSpace:
        ///   output = new Vector2(defaultGyroscope.Z, defaultGyroscope.X)
        ///   → horizontal = gyro.Z, vertical = gyro.X (negated for natural feel).
        ///   After ClawGyroSourceAdapter remapping:
        ///     gyroZ = -physical AngularVelocityY  (HC defaultGyroscope.Z)
        ///     gyroX = physical AngularVelocityX   (HC defaultGyroscope.X)
        ///
        /// Sensitivity scaling. HC's effective factor is GetSensitivityX() (= MotionSensivityX × 1000,
        /// default 1.0 → 1000) × ApplyCustomSensitivity() (flat default curve = 0.5) = 500, i.e. full
        /// deflection at ~65 dps. We deliberately run hotter: _gyroSensitivityX × 20 → factor 2000 at
        /// the default slider value of 100 (~4× HC). Fast motion is what the user wants strong, and
        /// the user-facing sensitivity slider lets them dial it; we do NOT lower this to HC's exact
        /// value because that would also slow the fast-motion range the user is happy with.
        ///   Output = Clamp((h × factor), short.MinValue, short.MaxValue)
        ///
        /// The low-speed "stickiness" is fixed by the radial anti-deadzone below
        /// (ApplyGyroAntiDeadzoneRadial, HC default 15 %), not by the raw sensitivity.
        /// </summary>
        private void ApplyGyroToStick(GyroSample sample, out short outputX, out short outputY)
        {
            // HC LocalSpace: horizontal = gyro.Z, vertical = gyro.X
            // After ClawGyroSourceAdapter: gyroZ = -physical AngularVelocityY = HC gyro.Z
            float h = sample.GyroZDegPerSecond;
            float v = sample.GyroXDegPerSecond;

            if (_gyroInvertX) h = -h;
            if (_gyroInvertY) v = -v;

            // Deadzone (1:1 from HC ApplyDeadzone)
            float dz = Math.Max(0.0f, _gyroDeadzone);
            h = GyroApplyDeadzone(h, dz);
            v = GyroApplyDeadzone(v, dz);

            // Delta time for One-Euro filter
            long nowTicks = sample.TimestampTicksUtc > 0 ? sample.TimestampTicksUtc : DateTime.UtcNow.Ticks;
            float dt = GyroDeltaDefault;
            if (_gyroLastSampleTicks > 0)
            {
                float raw = (nowTicks - _gyroLastSampleTicks) / (float)TimeSpan.TicksPerSecond;
                if (raw > 0 && raw < 1.0f) dt = Math.Max(GyroDeltaMin, Math.Min(GyroDeltaMax, raw));
            }
            _gyroLastSampleTicks = nowTicks;

            // One-Euro filter (1:1 from HC ApplyOneEuroAxis) — Adaptive engine only.
            // Direct engine (HC 1:1) skips the software low-pass entirely: HC's MotionManager
            // stick path applies no extra smoothing, so gyro maps straight through. Resetting
            // _gyroFilterInit means a later switch back to Adaptive re-seeds the filter cleanly.
            if (_gyroEngineMode == 1)
            {
                _gyroFilterInit = false;
            }
            else if (!_gyroFilterInit)
            {
                _gyroFiltH = h; _gyroFiltV = v;
                _gyroDerivH = 0; _gyroDerivV = 0;
                _gyroFilterInit = true;
            }
            else
            {
                h = GyroOneEuro(h, ref _gyroFiltH, ref _gyroDerivH, dt);
                v = GyroOneEuro(v, ref _gyroFiltV, ref _gyroDerivV, dt);
            }

            // Sensitivity scaling, doubled vs. the original HC port so the slider range is twice as
            // strong: new 50 == old 100 (factor 1000), new 100 == old 200 (factor 2000). Lets the
            // user reach much higher gyro gain; the default also moves to 100.
            float scaleX = Math.Max(0.1f, _gyroSensitivityX * 20.0f);
            float scaleY = Math.Max(0.1f, _gyroSensitivityY * 20.0f);

            // Sign convention:
            //   h = sample.GyroZDegPerSecond = -physical.GyroY  (negated once in ClawGyroSourceAdapter)
            //   Applying -h here would double-negate → wrong default direction (user must enable InvertX to fix).
            //   Use h directly — ClawGyroSourceAdapter already carries the correct sign for horizontal.
            // → vertical: v = sample.GyroXDegPerSecond = +physical.GyroX, no extra negation needed
            float fx = h * scaleX; // was: -h (double-negation bug, fixed 2026-05-27)
            float fy = v * scaleY;

            // Anti-deadzone — ported 1:1 from HC InputUtils.ApplyAntiDeadzone(Vector2, int),
            // which HC applies downstream of the gyro output in AxisActions
            // (GyroActions.DefaultAxisAntiDeadZone = 15). Lifts any non-zero gyro vector to at
            // least 15 % deflection so slow motion clears the *game's* own right-stick deadzone
            // instead of dying inside it. This was the one HC default missing from this path and
            // is the direct cause of the "sticky at low speed, only registers when moved fast"
            // feel: fast motion already produced a large-enough deflection, slow motion didn't.
            // The _gyroDeadzone gate above keeps rest-state IMU jitter from being lifted.
            ApplyGyroAntiDeadzoneRadial(ref fx, ref fy, GyroStickAntiDeadZonePercent);

            outputX = GyroClampToInt16(fx);
            outputY = GyroClampToInt16(fy);
        }

        // HC GyroActions.DefaultAxisAntiDeadZone = 15. Hardcoded (not a slider) so every install —
        // fresh or existing — picks up HC's low-speed responsiveness immediately, with no settings
        // migration and regardless of the (currently disabled) v2 anti-deadzone UI.
        private const int GyroStickAntiDeadZonePercent = 15;

        /// <summary>
        /// Radial anti-deadzone, ported 1:1 from HC <c>InputUtils.ApplyAntiDeadzone(Vector2, int)</c>.
        /// Operates on float stick components in the [-short.MaxValue, short.MaxValue] range: scales
        /// the stick vector so its smallest non-zero magnitude is <paramref name="percent"/>% of full
        /// deflection, preserving direction.
        /// </summary>
        private static void ApplyGyroAntiDeadzoneRadial(ref float x, ref float y, int percent)
        {
            if (percent <= 0 || (x == 0.0f && y == 0.0f))
            {
                return;
            }

            float nx = x / short.MaxValue;
            float ny = y / short.MaxValue;
            float len = (float)Math.Sqrt((nx * nx) + (ny * ny));
            if (len <= 0.0f)
            {
                x = 0.0f;
                y = 0.0f;
                return;
            }

            float dz = percent / 100.0f;
            float mul = (((1.0f - dz) * len) + dz) / len;
            x = nx * mul * short.MaxValue;
            y = ny * mul * short.MaxValue;
        }

        // ── Gyro-to-mouse (HC ApplyMouseFromGyro port) ───────────────────────────

        /// <summary>
        /// Converts a gyro sample to a relative mouse move.
        /// 1:1 port of HC MotionManager.LocalSpace + ApplyMouseFromGyro():
        ///   output = new Vector2(defaultGyroscope.Z, defaultGyroscope.X)
        ///   → horizontal = gyro.Z, vertical = gyro.X (negated for natural feel).
        ///   After ClawGyroSourceAdapter remapping:
        ///     gyroZ = -physical AngularVelocityY  (HC defaultGyroscope.Z)
        ///     gyroX = physical AngularVelocityX   (HC defaultGyroscope.X)
        ///   LegionGyroSensitivityX/Y used as gainX/Y (0-100 → 0.0-1.0).
        ///   LegionGyroDeadzone used as threshold (degrees/sec).
        /// </summary>
        private void ApplyGyroMouse(GyroSample sample)
        {
            // HC LocalSpace: horizontal = gyro.Z, vertical = gyro.X
            // After ClawGyroSourceAdapter: gyroZ = -physical AngularVelocityY = HC gyro.Z
            float h = sample.GyroZDegPerSecond;
            float v = sample.GyroXDegPerSecond;

            if (_gyroInvertX) h = -h;
            if (_gyroInvertY) v = -v;

            // Deadzone (HC: ApplyDeadzone with mouseThreshold)
            float threshold = Math.Max(0.0f, _gyroDeadzone);
            h = GyroApplyDeadzone(h, threshold);
            v = GyroApplyDeadzone(v, threshold);
            h = Math.Max(-GyroMouseMaxDegPerSec, Math.Min(GyroMouseMaxDegPerSec, h));
            v = Math.Max(-GyroMouseMaxDegPerSec, Math.Min(GyroMouseMaxDegPerSec, v));

            // Delta time
            long sampleTicks = sample.TimestampTicksUtc > 0 ? sample.TimestampTicksUtc : DateTime.UtcNow.Ticks;
            float dt = GyroDeltaDefault;
            if (_gyroLastSampleTicks > 0)
            {
                long deltaTicks = sampleTicks - _gyroLastSampleTicks;
                if (deltaTicks > 0 && deltaTicks < TimeSpan.TicksPerSecond)
                {
                    float raw = deltaTicks / (float)TimeSpan.TicksPerSecond;
                    dt = Math.Max(GyroDeltaMin, Math.Min(GyroDeltaMax, raw));
                }
            }
            _gyroLastSampleTicks = sampleTicks;

            // One-Euro filter (1:1 from HC ApplyMouseFromGyro)
            if (!_gyroFilterInit)
            {
                _gyroFiltH = h; _gyroFiltV = v;
                _gyroDerivH = 0; _gyroDerivV = 0;
                _gyroFilterInit = true;
            }
            else
            {
                h = GyroOneEuro(h, ref _gyroFiltH, ref _gyroDerivH, dt);
                v = GyroOneEuro(v, ref _gyroFiltV, ref _gyroDerivV, dt);
            }

            // Sensitivity and gain (HC: sensitivityScale × gainXScale × MousePixelsPerDegree).
            // Doubled vs. the original /100 so the slider range is twice as strong: new 50 == old 100
            // (gain 1.0), new 100 == old 200 (gain 2.0). Matches the gyro→stick rescale + new default 100.
            float sensX = Math.Max(0.05f, _gyroSensitivityX / 50.0f);
            float sensY = Math.Max(0.05f, _gyroSensitivityY / 50.0f);
            float scaleX = (float)Math.Pow(sensX, GyroMouseSensPower);
            float scaleY = (float)Math.Pow(sensY, GyroMouseSensPower);

            float moveX = (h * dt * GyroMousePixelsPerDeg * scaleX) + _gyroCarryX;
            float moveY = ((-v) * dt * GyroMousePixelsPerDeg * scaleY) + _gyroCarryY; // pitch negated

            int dx = (int)Math.Round(moveX);
            int dy = (int)Math.Round(moveY);

            bool clampedX = false, clampedY = false;
            if (dx >  GyroMouseMaxPixelFrame) { dx =  GyroMouseMaxPixelFrame; clampedX = true; }
            else if (dx < -GyroMouseMaxPixelFrame) { dx = -GyroMouseMaxPixelFrame; clampedX = true; }
            if (dy >  GyroMouseMaxPixelFrame) { dy =  GyroMouseMaxPixelFrame; clampedY = true; }
            else if (dy < -GyroMouseMaxPixelFrame) { dy = -GyroMouseMaxPixelFrame; clampedY = true; }

            _gyroCarryX = clampedX ? 0.0f : (moveX - dx);
            _gyroCarryY = clampedY ? 0.0f : (moveY - dy);

            if (dx != 0 || dy != 0)
                mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, IntPtr.Zero);
        }

        // ── Gyro helpers (1:1 from ControllerEmulationManager static helpers) ─────

        /// <summary>
        /// Vector-magnitude-clamped stick merge. 1:1 from HC MergeStickVectors().
        /// Gyro delta is added to the physical stick value; result is clamped to int16 range.
        /// </summary>
        private static void MergeGyroStick(ref short baseX, ref short baseY, short gyroX, short gyroY)
        {
            float sumX = baseX + gyroX;
            float sumY = baseY + gyroY;
            float mag = (float)Math.Sqrt(sumX * sumX + sumY * sumY);
            if (mag > short.MaxValue && mag > 0.0f)
            {
                float scale = short.MaxValue / mag;
                sumX *= scale;
                sumY *= scale;
            }
            baseX = GyroClampToInt16(sumX);
            baseY = GyroClampToInt16(sumY);
        }

        private static short GyroClampToInt16(float value)
        {
            if (value >  short.MaxValue) return  short.MaxValue;
            if (value <  short.MinValue) return  short.MinValue;
            return (short)Math.Round(value);
        }

        /// <summary>Dead-band with smooth recovery. 1:1 from HC ApplyDeadzone().</summary>
        private static float GyroApplyDeadzone(float value, float deadzone)
        {
            float mag = Math.Abs(value);
            if (mag <= deadzone) return 0.0f;
            return Math.Sign(value) * (mag - deadzone);
        }

        /// <summary>
        /// One-Euro low-pass filter for a single axis.
        /// 1:1 from HC ApplyOneEuroAxis() — adaptive cutoff based on derivative magnitude.
        /// </summary>
        private static float GyroOneEuro(float raw, ref float filt, ref float deriv, float dt)
        {
            float dx = (raw - filt) / Math.Max(0.0005f, dt);
            float dAlpha = GyroOneEuroAlpha(GyroOneEuroDerivCutoff, dt);
            deriv += (dx - deriv) * dAlpha;
            float dynCutoff = GyroOneEuroMinCutoff + GyroOneEuroBeta * Math.Abs(deriv);
            float alpha = GyroOneEuroAlpha(dynCutoff, dt);
            filt += (raw - filt) * alpha;
            return filt;
        }

        private static float GyroOneEuroAlpha(float cutoff, float dt)
        {
            if (cutoff <= 0.0f) return 1.0f;
            float dt2 = Math.Max(0.0005f, dt);
            float tau = 1.0f / (2.0f * (float)Math.PI * cutoff);
            return 1.0f / (1.0f + tau / dt2);
        }
    }
}
