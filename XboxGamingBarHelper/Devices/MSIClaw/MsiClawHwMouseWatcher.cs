using System;
using System.Threading;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Detects an UNINTENDED MSI Claw firmware mouse mode and switches back to the controller.
    ///
    /// The Claw's native Desktop mouse mode (long-press Start, or booted-in-mouse-mode) is INVISIBLE to
    /// device enumeration (PID + collections identical in both modes) and its Mouse HID collection is a
    /// system mouse Windows owns exclusively (a user-mode HID read can't open it). But the firmware
    /// exposes the current mode directly: ReadGamepadMode (0x26) → GamepadModeAck (0x27) returns the
    /// GamepadMode byte (2 = DirectInput/controller, 4 = Desktop/HW-mouse) — verified on device.
    ///
    /// So this watcher simply polls the firmware mode (~1 s) while the virtual controller is running. On
    /// Desktop mode it asks the (gated) recover callback to switch back. This is deterministic — it catches
    /// the switch even when the stick isn't moving, unlike the old behavioural approaches — and it costs a
    /// single tiny HID transaction per second, only while emulation is active.
    /// </summary>
    internal sealed class MsiClawHwMouseWatcher
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const int PollIntervalMs = 1000;

        private readonly Action _recover;      // Program.RecoverFromUnintendedHwMouse — gated on its side.
        private readonly Func<bool> _shouldPoll; // only poll while the virtual controller is running.
        private Thread _thread;
        private volatile bool _running;

        public MsiClawHwMouseWatcher(Action recover, Func<bool> shouldPoll)
        {
            _recover = recover;
            _shouldPoll = shouldPoll;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "ClawHwMouseWatch" };
            _thread.Start();
            Logger.Info("MsiClawHwMouseWatcher: started (polls firmware GamepadMode — auto-recovers an unintended Claw HW mouse)");
        }

        public void Stop()
        {
            _running = false;
            try { _thread?.Join(500); } catch { }
            _thread = null;
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    if (_shouldPoll == null || _shouldPoll())
                    {
                        int mode = MSIClawHidController.TryReadGamepadMode();
                        if (mode == MSIClawHidController.GamepadModeDesktop)
                        {
                            // Firmware is in Desktop/HW-mouse mode. The recover callback decides whether to
                            // act (ignores our intentional killswitch, a recovery already in flight, and the
                            // no-controller case) and logs when it does.
                            try { _recover?.Invoke(); } catch (Exception ex) { Logger.Debug($"MsiClawHwMouseWatcher: recover threw: {ex.Message}"); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"MsiClawHwMouseWatcher: poll error: {ex.Message}");
                }

                Sleep(PollIntervalMs);
            }
        }

        private void Sleep(int ms)
        {
            int waited = 0;
            while (_running && waited < ms) { Thread.Sleep(50); waited += 50; }
        }
    }
}
