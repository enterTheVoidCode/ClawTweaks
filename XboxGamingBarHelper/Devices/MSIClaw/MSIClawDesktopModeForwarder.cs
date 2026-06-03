using System;
using System.Runtime.InteropServices;
using System.Threading;
using NLog;
using XboxGamingBarHelper.ControllerEmulation.Viiper;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Software mouse emulation for MSI Claw "Mouse mode".
    ///
    /// Translates physical XInput controller input into Windows mouse events via
    /// mouse_event (same approach as ClawButtonMonitor gyro-mouse):
    ///   - Right stick  → cursor movement (MOUSEEVENTF_MOVE)
    ///   - Left  stick Y → vertical scroll wheel (MOUSEEVENTF_WHEEL)
    ///   - LB (left  shoulder) → right mouse button
    ///   - RB (right shoulder) → left  mouse button
    ///
    /// Adapted from HC fork LayoutManager desktop preset.
    /// HC default used LT/RT for clicks — remapped to LB/RB per user spec.
    ///
    /// Start sequence:
    ///   1. TrySwitchToXInput() — firmware mode switch back to XInput (PID 0x1901).
    ///      ClawButtonMonitor leaves the controller in DInput mode (PID 0x1902);
    ///      XInput API cannot read DInput-mode devices.
    ///   2. Settle delay (600 ms) for Windows re-enumeration.
    ///   3. Find physical XInput slot (0-3).
    ///   4. Poll loop at ~125 Hz, translating stick/button state to mouse input.
    ///
    /// Stopped (and mouse buttons released) when toggled back to Controller mode
    /// or on helper shutdown.
    /// </summary>
    internal sealed class MSIClawDesktopModeForwarder : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Poll rate matching HC fork (~125 Hz)
        private const int PollIntervalMs = 8;

        // Mode-switch settle: same value as MSIClawViGEmForwarder
        private const int SwitchModeSettleMs = 600;

        // Deadzone: 15% of ±32767 (~4915) — filters stick drift
        private const float DeadzoneThreshold = 0.15f;

        // Mouse cursor: pixels per poll tick at full stick deflection.
        // 20 px × 125 Hz = 2500 px/sec ≈ 0.5 s to cross a 1280-wide screen.
        private const float MouseSensitivity = 20.0f;

        // Scroll: notches per poll tick at full stick deflection (WHEEL_DELTA = 120).
        // 0.08 × 125 Hz ≈ 10 notches/sec at full deflection; ~5 notches/sec at half.
        private const float ScrollRate = 0.08f;

        // Re-probe interval when XInput slot is unknown (~1 s at 125 Hz)
        private const int ReprobeTicks = 125;

        private Thread _pollThread;
        private volatile bool _running;
        private bool _disposed;

        /// <summary>True while the poll thread is active.</summary>
        public bool IsRunning => _running;

        // ── Win32 mouse P/Invoke (same pattern as ClawButtonMonitor gyro-mouse) ──────

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_MOVE      = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN  = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP    = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP   = 0x0010;
        private const uint MOUSEEVENTF_WHEEL     = 0x0800;

        // ── Start / Stop ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Switches physical controller to XInput mode and starts the mouse poll thread.
        /// Must be called from a background thread — mode switch blocks ~600 ms.
        /// </summary>
        /// <returns>True if the poll thread started successfully.</returns>
        public bool Start()
        {
            if (_running) return true;

            // Step 1: Switch physical controller from DInput → XInput mode.
            // ClawButtonMonitor operates in DInput mode (PID 0x1902); XInput API
            // only reads XInput-mode controllers (PID 0x1901).
            bool switched = MSIClawHidController.TrySwitchToXInput();
            Logger.Info($"[MSIClawDesktopModeForwarder] SwitchMode(XInput) = {switched}");
            // Always wait the settle time — even if TrySwitchToXInput returned false,
            // ClawButtonMonitor may have just switched to DInput→XInput and the device
            // is still re-enumerating. Without this wait, FindXInputSlot() runs before
            // PID_1901 appears and returns -1 (no controller found).
            Thread.Sleep(SwitchModeSettleMs);

            // Step 2: Locate physical XInput slot (0-3).
            int slot = FindXInputSlot();
            if (slot < 0)
            {
                Logger.Warn("[MSIClawDesktopModeForwarder] No XInput controller found after mode switch — mouse mode unavailable");
                return false;
            }

            // Step 3: Start poll thread.
            _running = true;
            _pollThread = new Thread(() => PollLoop(slot))
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,   // drives the virtual mouse — keep smooth under load
                Name = "MSIClawMousePoll",
            };
            _pollThread.Start();

            Logger.Info($"[MSIClawDesktopModeForwarder] Started — XInput slot {slot}");
            return true;
        }

        /// <summary>
        /// Stops the poll thread and releases any held mouse buttons so the
        /// desktop does not end up with stuck LMB/RMB state.
        /// </summary>
        public void Stop()
        {
            if (!_running && _pollThread == null) return;

            _running = false;

            if (_pollThread != null)
            {
                _pollThread.Join(1000);
                _pollThread = null;
            }

            // Defensive release: send UP events regardless of current button state
            // so no mouse button remains stuck after the forwarder stops.
            try
            {
                mouse_event(MOUSEEVENTF_LEFTUP,  0, 0, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
            }
            catch { }

            Logger.Info("[MSIClawDesktopModeForwarder] Stopped");
        }

        // ── Poll loop ─────────────────────────────────────────────────────────────────

        private void PollLoop(int slot)
        {
            bool lbWasDown   = false;
            bool rbWasDown   = false;
            float scrollAccum = 0f;
            int reprobeTick  = 0;

            while (_running)
            {
                try
                {
                    // Re-probe if controller disconnected
                    if (slot < 0)
                    {
                        if (++reprobeTick >= ReprobeTicks)
                        {
                            reprobeTick = 0;
                            slot = FindXInputSlot();
                            if (slot >= 0)
                                Logger.Info($"[MSIClawDesktopModeForwarder] Controller reconnected on slot {slot}");
                        }
                        Thread.Sleep(PollIntervalMs);
                        continue;
                    }

                    ViiperXInputState state = default;
                    uint result = ViiperXInput.GetState((uint)slot, ref state);

                    if (result == ViiperXInput.ErrorSuccess)
                    {
                        reprobeTick = 0;
                        var gp = state.Gamepad;

                        // ── LB → right click, RB → left click ─────────────────────
                        bool lbDown = (gp.Buttons & ViiperXInput.LB) != 0;
                        bool rbDown = (gp.Buttons & ViiperXInput.RB) != 0;

                        if (lbDown && !lbWasDown)
                            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
                        else if (!lbDown && lbWasDown)
                            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
                        lbWasDown = lbDown;

                        if (rbDown && !rbWasDown)
                            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                        else if (!rbDown && rbWasDown)
                            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
                        rbWasDown = rbDown;

                        // ── Right stick → cursor movement ─────────────────────────
                        float fx = gp.ThumbRX / 32767f;
                        float fy = -gp.ThumbRY / 32767f; // invert Y: stick up → cursor up

                        if (Math.Abs(fx) < DeadzoneThreshold) fx = 0f;
                        if (Math.Abs(fy) < DeadzoneThreshold) fy = 0f;

                        if (fx != 0f || fy != 0f)
                        {
                            int dx = (int)(fx * MouseSensitivity);
                            int dy = (int)(fy * MouseSensitivity);
                            if (dx != 0 || dy != 0)
                                mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, IntPtr.Zero);
                        }

                        // ── Left stick Y → vertical scroll wheel ──────────────────
                        // Positive Y = stick pushed up = scroll up (WHEEL_DELTA positive).
                        float scrollY = gp.ThumbLY / 32767f;

                        if (Math.Abs(scrollY) < DeadzoneThreshold)
                        {
                            // Stick at rest: clear accumulator so partial scroll doesn't linger
                            scrollAccum = 0f;
                        }
                        else
                        {
                            scrollAccum += scrollY * ScrollRate;
                            int notches = (int)scrollAccum;
                            if (notches != 0)
                            {
                                // WHEEL_DELTA = 120; pass as uint (Windows reads as signed DWORD)
                                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)(notches * 120)), IntPtr.Zero);
                                scrollAccum -= notches;
                            }
                        }
                    }
                    else if (result == ViiperXInput.ErrorDeviceNotConnected)
                    {
                        // Release stuck buttons if controller disconnects mid-hold
                        if (lbWasDown) { mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero); lbWasDown = false; }
                        if (rbWasDown) { mouse_event(MOUSEEVENTF_LEFTUP,  0, 0, 0, IntPtr.Zero); rbWasDown = false; }
                        slot = -1;
                        reprobeTick = 0;
                        Logger.Info("[MSIClawDesktopModeForwarder] Controller disconnected — will re-probe");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"[MSIClawDesktopModeForwarder] PollLoop: {ex.Message}");
                }

                Thread.Sleep(PollIntervalMs);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans XInput slots 0-3 and returns the first connected slot.
        /// Returns -1 if no physical controller is found.
        /// </summary>
        private static int FindXInputSlot()
        {
            for (uint i = 0; i <= 3; i++)
            {
                ViiperXInputState state = default;
                uint result = ViiperXInput.GetState(i, ref state);
                if (result == ViiperXInput.ErrorSuccess)
                    return (int)i;
            }
            return -1;
        }

        // ── IDisposable ───────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
