using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using NLog;
using SharpDX.DirectInput;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.Core
{
    /// <summary>
    /// Monitors the MSI Claw controller for button combos using SharpDX.DirectInput.
    ///
    /// Ported 1:1 from HC fork DClawController.Tick() — reads ALL buttons including
    /// M1 (Buttons[15]) and M2 (Buttons[16]) which are invisible to XInput.
    ///
    /// Device discovery: VendorId=0x0DB0, ProductIds=[0x1901 XInput, 0x1902 DInput].
    /// Button mapping exactly mirrors HC DClawController.Tick():
    ///   Buttons[0]=X  [1]=A  [2]=B  [3]=Y  [4]=LB  [5]=RB  [6]=L2  [7]=R2
    ///   Buttons[8]=View  [9]=Menu  [10]=LS  [11]=RS  [15]=M1  [16]=M2
    ///   POV: 0=Up  9000=Right  18000=Down  27000=Left  diagonals supported
    /// </summary>
    internal class ControllerHotkeyMonitor : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ── DirectInput (HC DInputController pattern) ─────────────────────────────
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        private DirectInput _directInput;
        private Joystick    _joystick;
        private readonly object _joystickLock = new object();

        // MSI Claw HID identifiers — HC ClawA1M.cs
        private const int VendorId = 0x0DB0;
        private static readonly int[] ProductIds = { 0x1901, 0x1902, 0x1903 }; // XInput, DInput, Testing

        // ── Button bit constants (same as widget ClawButtonDefs) ──────────────────
        private const uint BTN_DPAD_UP    = 0x0001;
        private const uint BTN_DPAD_DOWN  = 0x0002;
        private const uint BTN_DPAD_LEFT  = 0x0004;
        private const uint BTN_DPAD_RIGHT = 0x0008;
        private const uint BTN_MENU       = 0x0010; // Start / three-lines
        private const uint BTN_VIEW       = 0x0020; // Back  / two-squares
        private const uint BTN_LS         = 0x0040;
        private const uint BTN_RS         = 0x0080;
        private const uint BTN_LB         = 0x0100;
        private const uint BTN_RB         = 0x0200;
        private const uint BTN_A          = 0x1000;
        private const uint BTN_B          = 0x2000;
        private const uint BTN_X          = 0x4000;
        private const uint BTN_Y          = 0x8000;
        private const uint BTN_M1         = 0x10000; // HC OEM3 — Buttons[15]
        private const uint BTN_M2         = 0x20000; // HC OEM4 — Buttons[16]

        // ── Tile hotkeys ──────────────────────────────────────────────────────────
        private readonly Dictionary<uint, (Action Callback, string Name)> _tileHotkeys
            = new Dictionary<uint, (Action, string)>();
        private readonly object _tileHotkeysLock = new object();
        private uint     _activeTileMask      = 0;
        private DateTime _tileComboStartTime  = DateTime.MinValue;
        private const int COMBO_HOLD_MS = 50; // HC: combos fire after 50 ms hold

        // ── Legacy combo tracking (Menu+DPad, View+ABXY) ─────────────────────────
        private uint     _lastButtons     = 0;
        private uint     _comboMask       = 0;      // full mask of active legacy combo
        private DateTime _comboStartTime  = DateTime.MinValue;

        /// <summary>Most-recently read button bitmask. Thread-safe volatile read.</summary>
        public uint CurrentButtons { get; private set; }

        // ── External feed (HC pattern: single DirectInput reader in ClawButtonMonitor) ──────
        // When HotkeyFeed is wired, ClawButtonMonitor calls FeedButtons() every poll tick.
        // The MonitorLoop then skips its own DirectInput acquisition — no second DI instance.
        private volatile bool _externalFeedActive;

        /// <summary>
        /// Receive button state from ClawButtonMonitor (1:1 HC pattern: single DirectInput
        /// reader, hotkey system reads already-computed state — no second DirectInput instance).
        /// Automatically disables the internal MonitorLoop DirectInput path.
        /// </summary>
        public void FeedButtons(uint buttons)
        {
            _externalFeedActive = true;
            CurrentButtons = buttons;
            ProcessButtonState(buttons);
        }

        // ── Thread control ────────────────────────────────────────────────────────
        private Thread _monitorThread;
        private volatile bool _running;
        private const int PollIntervalMs = 16; // ~60 Hz — same as HC

        // ── Legacy combo callbacks ─────────────────────────────────────────────────
        public Action OnMenuDPadUp    { get; set; }
        public Action OnMenuDPadDown  { get; set; }
        public Action OnMenuDPadLeft  { get; set; }
        public Action OnMenuDPadRight { get; set; }
        public Action OnViewA         { get; set; }
        public Action OnViewB         { get; set; }
        public Action OnViewX         { get; set; }
        public Action OnViewY         { get; set; }

        public bool MenuDPadUpEnabled    { get; set; }
        public bool MenuDPadDownEnabled  { get; set; }
        public bool MenuDPadLeftEnabled  { get; set; }
        public bool MenuDPadRightEnabled { get; set; }
        public bool ViewAEnabled         { get; set; }
        public bool ViewBEnabled         { get; set; }
        public bool ViewXEnabled         { get; set; }
        public bool ViewYEnabled         { get; set; }

        // ── Tile hotkey API ───────────────────────────────────────────────────────

        /// <summary>Register (or update) a tile hotkey by button bitmask (≥2 bits required).</summary>
        public void RegisterTileHotkey(uint mask, Action callback, string name)
        {
            lock (_tileHotkeysLock)
                _tileHotkeys[mask] = (callback, name);
            Logger.Info($"ControllerHotkeyMonitor: Registered tile hotkey 0x{mask:X5} ({name})");
        }

        /// <summary>Remove all tile hotkeys registered via IPC.</summary>
        public void ClearTileHotkeys()
        {
            lock (_tileHotkeysLock)
            {
                _tileHotkeys.Clear();
                _activeTileMask     = 0;
                _tileComboStartTime = DateTime.MinValue;
            }
            Logger.Info("ControllerHotkeyMonitor: All tile hotkeys cleared");
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public ControllerHotkeyMonitor() { }

        public void Start()
        {
            if (_running)
            {
                Logger.Warn("ControllerHotkeyMonitor: Already running");
                return;
            }

            _running       = true;
            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name         = "ControllerHotkeyMonitor",
                Priority     = ThreadPriority.AboveNormal
            };
            _monitorThread.Start();
            Logger.Info("ControllerHotkeyMonitor: Started (DirectInput mode)");
        }

        public void Stop()
        {
            _running = false;
            if (_monitorThread != null && _monitorThread.IsAlive)
                _monitorThread.Join(1000);

            lock (_joystickLock)
            {
                _joystick?.Unacquire();
                _joystick?.Dispose();
                _joystick = null;
                _directInput?.Dispose();
                _directInput = null;
            }
            Logger.Info("ControllerHotkeyMonitor: Stopped");
        }

        public void Dispose() => Stop();

        // ── Monitor loop ──────────────────────────────────────────────────────────

        private void MonitorLoop()
        {
            while (_running)
            {
                try
                {
                    // HC pattern: if ClawButtonMonitor is feeding us buttons directly,
                    // skip DirectInput entirely — no second DI instance on same device.
                    if (_externalFeedActive)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    bool hasJoystick;
                    lock (_joystickLock) hasJoystick = _joystick != null;

                    if (!hasJoystick)
                    {
                        TryAcquireJoystick();
                        Thread.Sleep(2000);
                        continue;
                    }

                    JoystickState state;
                    lock (_joystickLock)
                    {
                        try
                        {
                            state = _joystick.GetCurrentState();
                        }
                        catch (SharpDX.SharpDXException ex)
                        {
                            // HC DClawController: re-acquire on NotAcquired / InputLost
                            if (ex.ResultCode == SharpDX.Result.GetResultFromWin32Error(0x1C /* ERROR_NOT_ENOUGH_MEMORY */) ||
                                ex.HResult == unchecked((int)0x8007001C) ||
                                ex.HResult == unchecked((int)0x8007000B) ||
                                IsNotAcquiredOrInputLost(ex))
                            {
                                Logger.Debug($"ControllerHotkeyMonitor: DirectInput lost, re-acquiring ({ex.HResult:X8})");
                                try { _joystick.Acquire(); }
                                catch
                                {
                                    _joystick?.Dispose();
                                    _joystick = null;
                                }
                            }
                            else
                            {
                                Logger.Warn($"ControllerHotkeyMonitor: DirectInput error {ex.HResult:X8}");
                            }
                            Thread.Sleep(PollIntervalMs);
                            continue;
                        }
                    }

                    // dirty-state guard — HC DClawController.Tick()
                    if (state.RotationX == 32767 && state.RotationY == 32767 && state.RotationZ == 32767)
                    {
                        Thread.Sleep(PollIntervalMs);
                        continue;
                    }

                    uint buttons = BuildButtonMask(state);
                    ProcessButtonState(buttons);
                }
                catch (Exception ex)
                {
                    Logger.Error($"ControllerHotkeyMonitor: MonitorLoop error: {ex.Message}");
                }

                Thread.Sleep(PollIntervalMs);
            }
        }

        // ── DirectInput device acquisition (HC DInputController pattern) ──────────

        private void TryAcquireJoystick()
        {
            try
            {
                var di = new DirectInput();
                var devices = di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices);

                foreach (var deviceInstance in devices)
                {
                    if (!IsMsiClawDevice(deviceInstance.ProductGuid)) continue;

                    try
                    {
                        var joystick = new Joystick(di, deviceInstance.InstanceGuid);

                        // HC: Background + NonExclusive — works without a visible window
                        joystick.SetCooperativeLevel(
                            GetDesktopWindow(),
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive);

                        joystick.Acquire();

                        lock (_joystickLock)
                        {
                            _joystick?.Dispose();
                            _directInput?.Dispose();
                            _joystick    = joystick;
                            _directInput = di;
                        }

                        Logger.Info($"ControllerHotkeyMonitor: Acquired '{deviceInstance.InstanceName}' via DirectInput");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"ControllerHotkeyMonitor: Failed to acquire '{deviceInstance.InstanceName}': {ex.Message}");
                    }
                }

                di.Dispose();
                Logger.Debug("ControllerHotkeyMonitor: MSI Claw DirectInput device not found");
            }
            catch (Exception ex)
            {
                Logger.Error($"ControllerHotkeyMonitor: TryAcquireJoystick: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks ProductGuid bytes for MSI Claw VID=0x0DB0.
        /// DirectInput HID ProductGuid encoding: bytes[0..1]=VID, bytes[2..3]=PID (little-endian).
        /// </summary>
        private static bool IsMsiClawDevice(Guid productGuid)
        {
            byte[] b = productGuid.ToByteArray();
            // Try VID-first layout (standard HID DirectInput)
            int vid1 = b[0] | (b[1] << 8);
            int pid1 = b[2] | (b[3] << 8);
            if (vid1 == VendorId && System.Array.IndexOf(ProductIds, pid1) >= 0) return true;
            // Try PID-first layout (some drivers swap the order)
            int pid2 = b[0] | (b[1] << 8);
            int vid2 = b[2] | (b[3] << 8);
            if (vid2 == VendorId && System.Array.IndexOf(ProductIds, pid2) >= 0) return true;
            return false;
        }

        private static bool IsNotAcquiredOrInputLost(SharpDX.SharpDXException ex)
        {
            // DIERR_NOTACQUIRED = 0x8007001C, DIERR_INPUTLOST = 0x8007000B
            return ex.HResult == unchecked((int)0x8007001C) ||
                   ex.HResult == unchecked((int)0x8007000B);
        }

        // ── Button mapping — 1:1 from HC DClawController.Tick() ──────────────────

        /// <summary>
        /// Build our uint button bitmask from a DirectInput JoystickState.
        /// Button indices and POV values are copied exactly from HC DClawController.Tick().
        /// </summary>
        private static uint BuildButtonMask(JoystickState state)
        {
            uint mask = 0;

            // Face buttons — HC: Buttons[1]=A, [2]=B, [0]=X, [3]=Y
            if (state.Buttons[1]) mask |= BTN_A;
            if (state.Buttons[2]) mask |= BTN_B;
            if (state.Buttons[0]) mask |= BTN_X;
            if (state.Buttons[3]) mask |= BTN_Y;

            // Shoulder buttons — HC: [4]=LB, [5]=RB
            if (state.Buttons[4]) mask |= BTN_LB;
            if (state.Buttons[5]) mask |= BTN_RB;

            // System buttons — HC: [8]=View(Back), [9]=Menu(Start)
            if (state.Buttons[8])  mask |= BTN_VIEW;
            if (state.Buttons[9])  mask |= BTN_MENU;

            // Stick clicks — HC: [10]=LS, [11]=RS
            if (state.Buttons[10]) mask |= BTN_LS;
            if (state.Buttons[11]) mask |= BTN_RS;

            // OEM buttons — HC: Buttons[15]=M1 (OEM3), Buttons[16]=M2 (OEM4)
            if (state.Buttons.Length > 15 && state.Buttons[15]) mask |= BTN_M1;
            if (state.Buttons.Length > 16 && state.Buttons[16]) mask |= BTN_M2;

            // D-Pad via POV — HC DClawController.Tick() exact values
            int pov = state.PointOfViewControllers[0];
            if (pov == 0     || pov == 4500  || pov == 31500) mask |= BTN_DPAD_UP;
            if (pov == 9000  || pov == 4500  || pov == 13500) mask |= BTN_DPAD_RIGHT;
            if (pov == 18000 || pov == 13500 || pov == 22500) mask |= BTN_DPAD_DOWN;
            if (pov == 27000 || pov == 31500 || pov == 22500) mask |= BTN_DPAD_LEFT;

            return mask;
        }

        // ── Button state processing ───────────────────────────────────────────────

        private void ProcessButtonState(uint buttons)
        {
            CurrentButtons = buttons;

            if (buttons != _lastButtons)
            {
                // Debug: full bitmask change. Enable by setting NLog.config minlevel=Debug.
                // File: %LOCALAPPDATA%\Packages\MSIClaw.ClawTweaks_7eszav2039cvc\LocalCache\ClawTweaks\Helper\NLog.config
                Logger.Debug($"ControllerHotkeyMonitor: Buttons 0x{_lastButtons:X5} -> 0x{buttons:X5}");

                if ((buttons & BTN_MENU) != 0 && (_lastButtons & BTN_MENU) == 0)
                    Logger.Info($"ControllerHotkeyMonitor: Menu pressed (0x{buttons:X5})");
                if ((buttons & BTN_VIEW) != 0 && (_lastButtons & BTN_VIEW) == 0)
                    Logger.Info($"ControllerHotkeyMonitor: View pressed (0x{buttons:X5})");
                if ((buttons & BTN_M1) != 0 && (_lastButtons & BTN_M1) == 0)
                    Logger.Info($"ControllerHotkeyMonitor: M1 pressed (0x{buttons:X5})");
                if ((buttons & BTN_M2) != 0 && (_lastButtons & BTN_M2) == 0)
                    Logger.Info($"ControllerHotkeyMonitor: M2 pressed (0x{buttons:X5})");
            }

            // ── Legacy combos (Menu+DPad, View+ABXY) ──────────────────────────────
            CheckLegacyCombos(buttons);

            // ── Tile hotkeys ───────────────────────────────────────────────────────
            if (_tileHotkeys.Count > 0)
            {
                Dictionary<uint, (Action Callback, string Name)> snapshot;
                lock (_tileHotkeysLock)
                    snapshot = new Dictionary<uint, (Action, string)>(_tileHotkeys);

                if (buttons != 0)
                    Logger.Debug($"ControllerHotkeyMonitor: TileCheck buttons=0x{buttons:X5} against {snapshot.Count} hotkey(s): " +
                        string.Join(", ", System.Linq.Enumerable.Select(snapshot, kv => $"0x{kv.Key:X5}({kv.Value.Name})")));

                bool anyTileActive = false;
                foreach (var kvp in snapshot)
                {
                    uint mask        = kvp.Key;
                    bool allBitsHeld = (buttons & mask) == mask;
                    bool enoughBits  = CountBits(mask) >= 2;
                    Logger.Debug($"ControllerHotkeyMonitor: TileCheck 0x{mask:X5} ({kvp.Value.Name}): allBitsHeld={allBitsHeld} enoughBits={enoughBits}");
                    if (!allBitsHeld || !enoughBits) continue;

                    anyTileActive = true;

                    if (_activeTileMask != mask)
                    {
                        Logger.Info($"ControllerHotkeyMonitor: Tile combo 0x{mask:X5} ({kvp.Value.Name}) detected, starting hold timer");
                        _activeTileMask     = mask;
                        _tileComboStartTime = DateTime.Now;
                    }
                    else if (_tileComboStartTime != DateTime.MinValue)
                    {
                        double heldMs = (DateTime.Now - _tileComboStartTime).TotalMilliseconds;
                        if (heldMs >= COMBO_HOLD_MS)
                        {
                            Logger.Info($"ControllerHotkeyMonitor: Tile combo 0x{mask:X5} ({kvp.Value.Name}) triggered");
                            _tileComboStartTime = DateTime.MinValue;

                            var callback = kvp.Value.Callback;
                            var name     = kvp.Value.Name;
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                try { callback(); }
                                catch (Exception ex) { Logger.Error($"ControllerHotkeyMonitor: Tile '{name}' callback error: {ex.Message}"); }
                            });
                        }
                    }
                    break; // fire only the first matching combo per frame
                }

                if (!anyTileActive)
                {
                    _activeTileMask     = 0;
                    _tileComboStartTime = DateTime.MinValue;
                }
            }

            _lastButtons = buttons;
        }

        private void CheckLegacyCombos(uint buttons)
        {
            // Menu + DPad combos
            if ((buttons & BTN_MENU) != 0)
            {
                TryLegacyCombo(buttons, BTN_MENU | BTN_DPAD_UP,    MenuDPadUpEnabled,    OnMenuDPadUp,    "Menu+DPadUp");
                TryLegacyCombo(buttons, BTN_MENU | BTN_DPAD_DOWN,  MenuDPadDownEnabled,  OnMenuDPadDown,  "Menu+DPadDown");
                TryLegacyCombo(buttons, BTN_MENU | BTN_DPAD_LEFT,  MenuDPadLeftEnabled,  OnMenuDPadLeft,  "Menu+DPadLeft");
                TryLegacyCombo(buttons, BTN_MENU | BTN_DPAD_RIGHT, MenuDPadRightEnabled, OnMenuDPadRight, "Menu+DPadRight");
            }

            // View + ABXY combos
            if ((buttons & BTN_VIEW) != 0)
            {
                TryLegacyCombo(buttons, BTN_VIEW | BTN_A, ViewAEnabled, OnViewA, "View+A");
                TryLegacyCombo(buttons, BTN_VIEW | BTN_B, ViewBEnabled, OnViewB, "View+B");
                TryLegacyCombo(buttons, BTN_VIEW | BTN_X, ViewXEnabled, OnViewX, "View+X");
                TryLegacyCombo(buttons, BTN_VIEW | BTN_Y, ViewYEnabled, OnViewY, "View+Y");
            }

            // Reset legacy combo tracking when neither modifier is held
            if ((buttons & (BTN_MENU | BTN_VIEW)) == 0)
            {
                _comboMask      = 0;
                _comboStartTime = DateTime.MinValue;
            }
        }

        private void TryLegacyCombo(uint buttons, uint comboMask, bool enabled, Action callback, string name)
        {
            if (!enabled) return;
            if (callback == null)
            {
                Logger.Warn($"ControllerHotkeyMonitor: {name} enabled but callback is null");
                return;
            }

            bool held = (buttons & comboMask) == comboMask;
            if (!held) return;

            if (_comboMask != comboMask)
            {
                Logger.Info($"ControllerHotkeyMonitor: {name} detected, starting hold timer");
                _comboMask      = comboMask;
                _comboStartTime = DateTime.Now;
            }
            else if (_comboStartTime != DateTime.MinValue)
            {
                double heldMs = (DateTime.Now - _comboStartTime).TotalMilliseconds;
                if (heldMs >= COMBO_HOLD_MS)
                {
                    Logger.Info($"ControllerHotkeyMonitor: {name} triggered");
                    _comboStartTime = DateTime.MinValue;

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { callback(); }
                        catch (Exception ex) { Logger.Error($"ControllerHotkeyMonitor: {name} error: {ex.Message}"); }
                    });
                }
            }
        }

        // ── Settings ──────────────────────────────────────────────────────────────

        public void LoadSettings()
        {
            try
            {
                if (LocalSettingsHelper.TryGetValue<int>("Hotkey_MenuDpadUp_Action",    out var up))    MenuDPadUpEnabled    = up    > 0;
                if (LocalSettingsHelper.TryGetValue<int>("Hotkey_MenuDpadDown_Action",  out var down))  MenuDPadDownEnabled  = down  > 0;
                if (LocalSettingsHelper.TryGetValue<int>("Hotkey_MenuDpadLeft_Action",  out var left))  MenuDPadLeftEnabled  = left  > 0;
                if (LocalSettingsHelper.TryGetValue<int>("Hotkey_MenuDpadRight_Action", out var right)) MenuDPadRightEnabled = right > 0;
                if (LocalSettingsHelper.TryGetValue<int>("Hotkey_MenuA_Action",         out var a))     ViewAEnabled         = a     > 0;
                if (LocalSettingsHelper.TryGetValue<int>("Hotkey_MenuB_Action",         out var b))     ViewBEnabled         = b     > 0;
                if (LocalSettingsHelper.TryGetValue<int>("Hotkey_MenuX_Action",         out var x))     ViewXEnabled         = x     > 0;
                if (LocalSettingsHelper.TryGetValue<int>("Hotkey_MenuY_Action",         out var y))     ViewYEnabled         = y     > 0;

                Logger.Info($"ControllerHotkeyMonitor: Settings loaded - Menu+DPad: Up={MenuDPadUpEnabled} Down={MenuDPadDownEnabled} Left={MenuDPadLeftEnabled} Right={MenuDPadRightEnabled}");
                Logger.Info($"ControllerHotkeyMonitor: Settings loaded - View+ABXY: A={ViewAEnabled} B={ViewBEnabled} X={ViewXEnabled} Y={ViewYEnabled}");
            }
            catch (Exception ex)
            {
                Logger.Error($"ControllerHotkeyMonitor: LoadSettings error: {ex.Message}");
            }
        }

        // ── Utilities ─────────────────────────────────────────────────────────────

        private static int CountBits(uint v)
        {
            int c = 0;
            while (v != 0) { c += (int)(v & 1); v >>= 1; }
            return c;
        }
    }
}
