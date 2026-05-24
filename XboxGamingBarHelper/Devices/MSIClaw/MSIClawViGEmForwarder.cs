using System;
using System.Threading;
using NLog;
using Shared.Enums;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.ControllerEmulation.Viiper;
using XboxGamingBarHelper.Labs;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Virtual Xbox 360 controller for the MSI Claw via ViGEmBus.
    ///
    /// Adapted from HC fork ClawA1M + XInputController:
    ///  1. Sends SwitchMode(XInput) via vendor HID so the physical controller
    ///     enumerates as a standard XInput device (PID=0x1901).
    ///  2. Creates a ViGEm virtual Xbox 360 controller (Nefarius.ViGEm.Client).
    ///  3. Hides the physical controller via HidHide so games only see ViGEm.
    ///  4. Polls XInputGetStateEx at ~125 Hz and forwards the full state to ViGEm,
    ///     including the Guide button (bit 0x0400 from undocumented ordinal #100).
    ///  5. Forwards vibration from ViGEm back to the physical controller.
    ///
    /// Win+G (MSI Claw Quick-Settings button) is NOT intercepted here — it passes
    /// through to Windows as normal so Game Bar opens via the standard shortcut.
    ///
    /// Replaces VIIPER for MSI Claw (VIIPER requires usbip-win2 which is not needed
    /// with ViGEmBus). VIIPER is explicitly skipped for MSI Claw in ViiperEmulationManager.
    /// </summary>
    internal sealed class MSIClawViGEmForwarder : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // HC poll rate: ~125 Hz (8 ms per poll)
        private const int PollIntervalMs = 8;

        // After SwitchMode the controller re-enumerates; wait for Windows to recognize it.
        private const int SwitchModeSettleMs = 600;

        // After ViGEm PlugIn, wait for Windows to assign an XInput slot.
        private const int ViGEmSettleMs = 250;

        // Re-probe interval when physical slot is unknown (in poll ticks → ~1 s).
        private const int ReprobeTicks = 125;

        private ViGEmController _viGEm;
        private Thread _pollThread;
        private volatile bool _running;
        private int _physicalXInputIndex = -1;
        private bool _lastGuide;
        private bool _ownsSuppression;
        private ControllerSuppressionManager _suppressionManager;
        private bool _disposed;

        /// <summary>True once the forwarder is running (poll thread active, ViGEm connected).</summary>
        public bool IsRunning => _running;

        // ── Start / Stop ─────────────────────────────────────────────────

        /// <summary>
        /// Starts the forwarder.  Call from a background thread — SwitchMode and HidHide
        /// can block for up to ~600 ms during mode switch + re-enumeration.
        /// </summary>
        /// <param name="suppression">Shared HidHide manager; may be null.</param>
        /// <param name="deviceType">Device type used by HidHide to select what to hide.</param>
        /// <param name="hideTarget">HideTarget value (0 = all gamepads, 1 = VID:PID match, …).</param>
        public bool Start(ControllerSuppressionManager suppression, DeviceType deviceType, int hideTarget)
        {
            if (_running) return true;

            _suppressionManager = suppression;

            // ── Step 1: Put physical controller into XInput mode ──────────
            // HC ClawA1M.SwitchMode(GamepadMode.XInput): sends HID command
            // {15,0,0,60,36,1,0} padded to 64 bytes via the vendor interface.
            // Without this the controller stays in Desktop/Mouse mode and
            // XInputGetStateEx returns ERROR_DEVICE_NOT_CONNECTED.
            bool switched = MSIClawHidController.TrySwitchToXInput();
            Logger.Info($"[MSIClawViGEmForwarder] SwitchMode(XInput) = {switched}");
            if (switched)
            {
                // Wait for Windows to recognize the re-enumerated XInput device
                Thread.Sleep(SwitchModeSettleMs);
            }

            // ── Step 2: HidHide — hide physical controller BEFORE ViGEm ──
            // Critical ordering: hide the physical controller first so that
            // Steam (and other XInput clients) never get a chance to open a
            // handle on the physical device. If we hid after ViGEm plug-in,
            // Steam would already have a connection to the physical controller
            // and would keep showing it as the primary gamepad instead of ViGEm.
            // ClawTweaks is registered in HidHide's allowlist, so our own
            // XInputGetState calls still reach the physical device.
            if (suppression != null)
            {
                try
                {
                    _ownsSuppression = suppression.Enable(deviceType, hideTarget);
                    Logger.Info($"[MSIClawViGEmForwarder] HidHide suppression => {_ownsSuppression}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[MSIClawViGEmForwarder] HidHide Enable threw: {ex.Message}");
                }
            }

            // ── Step 3: Create ViGEm virtual Xbox 360 controller ─────────
            _viGEm = new ViGEmController();
            if (!_viGEm.Connect())
            {
                Logger.Error("[MSIClawViGEmForwarder] ViGEmBus connect failed — ViGEmBus driver not installed?");
                _viGEm.Dispose();
                _viGEm = null;
                if (_ownsSuppression && _suppressionManager != null)
                    try { _suppressionManager.Disable(); } catch { }
                _ownsSuppression = false;
                return false;
            }
            if (!_viGEm.PlugIn(ViGEmController.VirtualGamepadType.Xbox360))
            {
                Logger.Error("[MSIClawViGEmForwarder] ViGEm PlugIn(Xbox360) failed");
                _viGEm.Dispose();
                _viGEm = null;
                if (_ownsSuppression && _suppressionManager != null)
                    try { _suppressionManager.Disable(); } catch { }
                _ownsSuppression = false;
                return false;
            }
            _viGEm.RumbleReceived += OnRumbleReceived;

            // Let Windows assign an XInput slot to the new virtual device
            Thread.Sleep(ViGEmSettleMs);

            // ── Step 4: Locate physical XInput slot ───────────────────────
            // Scan slots 0-3 and skip the ViGEm virtual controller's own slot
            // so we forward from physical → virtual and not virtual → virtual.
            // ClawTweaks is in HidHide's allowlist so XInputGetState still works
            // for the now-hidden physical device.
            int viGEmSlot = _viGEm.VirtualXboxUserIndex ?? -1;
            _physicalXInputIndex = FindPhysicalXInputSlot(viGEmSlot);
            Logger.Info($"[MSIClawViGEmForwarder] ViGEm slot={viGEmSlot}, physical slot={_physicalXInputIndex}");

            // ── Step 5: Start poll thread ─────────────────────────────────
            // Physical controller hidden, ViGEm slot known — begin forwarding.
            _running = true;
            _pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "MSIClawViGEmPoll",
            };
            _pollThread.Start();

            Logger.Info($"[MSIClawViGEmForwarder] Started — physical={_physicalXInputIndex}, viGEmSlot={viGEmSlot}");
            return true;
        }

        /// <summary>Stops the forwarder and releases all resources.</summary>
        public void Stop()
        {
            if (!_running && _viGEm == null) return;

            _running = false;

            // Wait for poll thread to exit
            if (_pollThread != null)
            {
                _pollThread.Join(1000);
                _pollThread = null;
            }

            // Release HidHide — physical controller becomes visible again
            if (_ownsSuppression && _suppressionManager != null)
            {
                try { _suppressionManager.Disable(); }
                catch (Exception ex) { Logger.Warn($"[MSIClawViGEmForwarder] HidHide Disable threw: {ex.Message}"); }
                _ownsSuppression = false;
            }

            // Disconnect ViGEm virtual controller
            if (_viGEm != null)
            {
                try
                {
                    _viGEm.RumbleReceived -= OnRumbleReceived;
                    _viGEm.Dispose();
                }
                catch { }
                _viGEm = null;
            }

            Logger.Info("[MSIClawViGEmForwarder] Stopped");
        }

        // ── Poll loop ─────────────────────────────────────────────────────

        private void PollLoop()
        {
            int reprobeTick = 0;

            while (_running)
            {
                try
                {
                    if (_viGEm == null || !_viGEm.IsPluggedIn)
                    {
                        Thread.Sleep(PollIntervalMs);
                        continue;
                    }

                    int idx = _physicalXInputIndex;

                    // If physical slot not yet known, try to find it periodically
                    if (idx < 0)
                    {
                        if (++reprobeTick >= ReprobeTicks)
                        {
                            reprobeTick = 0;
                            int viGEmSlot = _viGEm.VirtualXboxUserIndex ?? -1;
                            int found = FindPhysicalXInputSlot(viGEmSlot);
                            if (found >= 0)
                            {
                                _physicalXInputIndex = found;
                                Logger.Info($"[MSIClawViGEmForwarder] Physical controller found on slot {found}");
                                idx = found;
                            }
                        }
                        if (idx < 0)
                        {
                            Thread.Sleep(PollIntervalMs);
                            continue;
                        }
                    }

                    // Read full XInput state including Guide bit (XInputGetStateEx ordinal #100)
                    ViiperXInputState state = default;
                    uint result = ViiperXInput.GetState((uint)idx, ref state);

                    if (result == ViiperXInput.ErrorSuccess)
                    {
                        reprobeTick = 0;
                        ushort buttons = state.Gamepad.Buttons;
                        bool guide = (buttons & ViiperXInput.Guide) != 0;

                        // Forward all standard buttons + axes (Guide handled separately below)
                        _viGEm.SubmitXboxState(
                            (ushort)(buttons & ~ViiperXInput.Guide),
                            state.Gamepad.LeftTrigger,
                            state.Gamepad.RightTrigger,
                            state.Gamepad.ThumbLX,
                            state.Gamepad.ThumbLY,
                            state.Gamepad.ThumbRX,
                            state.Gamepad.ThumbRY);

                        // Guide button edge: send separate ViGEm Guide press/release
                        if (guide != _lastGuide)
                        {
                            _viGEm.SetGuide(guide);
                            _lastGuide = guide;
                        }
                    }
                    else if (result == ViiperXInput.ErrorDeviceNotConnected)
                    {
                        // Physical controller disconnected or re-enumerating —
                        // re-probe periodically to re-pin to the correct slot.
                        if (++reprobeTick >= ReprobeTicks)
                        {
                            reprobeTick = 0;
                            int viGEmSlot = _viGEm.VirtualXboxUserIndex ?? -1;
                            int found = FindPhysicalXInputSlot(viGEmSlot);
                            if (found >= 0 && found != idx)
                            {
                                Logger.Info($"[MSIClawViGEmForwarder] Physical slot re-pinned: {idx} -> {found}");
                                _physicalXInputIndex = found;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"[MSIClawViGEmForwarder] PollLoop: {ex.Message}");
                }

                Thread.Sleep(PollIntervalMs);
            }
        }

        // ── Callbacks ─────────────────────────────────────────────────────

        /// <summary>
        /// ViGEm rumble feedback → forward to physical controller via XInputSetState.
        /// Matches HC fork XInputController vibration passthrough.
        /// </summary>
        private void OnRumbleReceived(byte largeMotor, byte smallMotor)
        {
            try
            {
                if (_physicalXInputIndex < 0) return;
                var vib = new ViiperXInputVibration
                {
                    // ViGEm delivers 0-255; XInputSetState expects 0-65535
                    LeftMotorSpeed  = (ushort)(largeMotor * 257),
                    RightMotorSpeed = (ushort)(smallMotor * 257),
                };
                ViiperXInput.SetState((uint)_physicalXInputIndex, ref vib);
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Scans XInput slots 0-3 and returns the first connected slot that is NOT
        /// the ViGEm virtual controller's own slot (excludeSlot).
        /// Returns -1 if no physical controller is found.
        /// </summary>
        private static int FindPhysicalXInputSlot(int excludeSlot)
        {
            for (uint i = 0; i <= 3; i++)
            {
                if ((int)i == excludeSlot) continue;
                ViiperXInputState state = default;
                uint result = ViiperXInput.GetState(i, ref state);
                if (result == ViiperXInput.ErrorSuccess)
                    return (int)i;
            }
            return -1;
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
