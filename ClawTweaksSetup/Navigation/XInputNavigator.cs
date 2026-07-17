using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ClawTweaksSetup.Navigation
{
    /// <summary>
    /// Polls the Claw's gamepad and raises a single <see cref="ButtonPressed"/> event on the rising
    /// edge of A / B / X / Y / Menu. The wizard maps those to fixed actions (there is no roaming
    /// focus) so the user always sees exactly which button does what.
    ///
    /// We poll on a DispatcherTimer because XInput has no message-loop hook. All four user slots are
    /// OR-ed together so the controller works regardless of which slot it occupies.
    /// </summary>
    public sealed class XInputNavigator : IDisposable
    {
        public event Action<PadButton> ButtonPressed;

        /// <summary>Raised continuously while the user pushes up/down (D-Pad or left stick). Positive = down.</summary>
        public event Action<double> ScrollRequested;

        /// <summary>Raised continuously while the user pushes the RIGHT stick up/down. Positive = down.
        /// Kept separate from <see cref="ScrollRequested"/> so a screen that binds the D-Pad to a
        /// discrete grid selection (CenterMenuWindow's build picker) can still offer stick scrolling
        /// without the two fighting over the same input.</summary>
        public event Action<double> RightStickScrollRequested;

        private readonly Window _window;
        private readonly DispatcherTimer _timer;
        private ushort _prevButtons;
        private ushort _prevStickDirBits;
        private const short StickDeadzone = 12000;

        // Virtual D-Pad bits for the left stick — deliberately the same values as the real
        // XINPUT_GAMEPAD_DPAD_* constants below so a screen bound to PadButton.Up/Down/Left/Right
        // (CenterMenuWindow's grid selection) reacts identically whether the user used the D-Pad or
        // the left stick. Edge-triggered exactly like the real D-Pad (one Raise per push past the
        // deadzone), not continuous — the left stick had no effect at all on the grid before this,
        // since only the physical D-Pad bits were ever edge-detected.
        private const ushort StickDirUp = 0x0001;
        private const ushort StickDirDown = 0x0002;
        private const ushort StickDirLeft = 0x0004;
        private const ushort StickDirRight = 0x0008;

        public XInputNavigator(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(40),
            };
            _timer.Tick += OnTick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose() { _timer.Stop(); _timer.Tick -= OnTick; }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_window.IsActive) { _prevButtons = 0; _prevStickDirBits = 0; return; }
            if (!TryPollCombined(out ushort buttons, out short lx, out short ly, out short ry))
            { _prevButtons = 0; _prevStickDirBits = 0; return; }

            // Continuous scroll from D-Pad up/down or left-stick Y (fires every tick while held).
            double scroll = 0;
            if ((buttons & XINPUT_GAMEPAD_DPAD_UP) != 0) scroll -= 46;
            if ((buttons & XINPUT_GAMEPAD_DPAD_DOWN) != 0) scroll += 46;
            if (ly > StickDeadzone) scroll -= 46 * (ly / 32767.0);
            if (ly < -StickDeadzone) scroll += 46 * (-ly / 32767.0);
            if (Math.Abs(scroll) > 0.5) ScrollRequested?.Invoke(scroll);

            // Right-stick Y drives its own independent scroll signal (see RightStickScrollRequested).
            double rscroll = 0;
            if (ry > StickDeadzone) rscroll -= 46 * (ry / 32767.0);
            if (ry < -StickDeadzone) rscroll += 46 * (-ry / 32767.0);
            if (Math.Abs(rscroll) > 0.5) RightStickScrollRequested?.Invoke(rscroll);

            // Left stick as a virtual D-Pad: edge-triggered exactly like the real D-Pad (one Raise per
            // push past the deadzone, not one per tick while held), so screens bound to
            // PadButton.Up/Down/Left/Right react identically either way. Runs unconditionally (not
            // gated behind the button "pressed == 0" check below) since a stick push alone never sets
            // any wButtons bit.
            ushort stickDirBits = 0;
            if (ly > StickDeadzone) stickDirBits |= StickDirUp;
            if (ly < -StickDeadzone) stickDirBits |= StickDirDown;
            if (lx > StickDeadzone) stickDirBits |= StickDirRight;
            if (lx < -StickDeadzone) stickDirBits |= StickDirLeft;
            ushort stickPressed = (ushort)(stickDirBits & ~_prevStickDirBits);
            _prevStickDirBits = stickDirBits;
            if ((stickPressed & StickDirUp) != 0) Raise(PadButton.Up);
            if ((stickPressed & StickDirDown) != 0) Raise(PadButton.Down);
            if ((stickPressed & StickDirLeft) != 0) Raise(PadButton.Left);
            if ((stickPressed & StickDirRight) != 0) Raise(PadButton.Right);

            ushort pressed = (ushort)(buttons & ~_prevButtons);
            _prevButtons = buttons;
            if (pressed == 0) return;

            if ((pressed & XINPUT_GAMEPAD_A) != 0) Raise(PadButton.A);
            if ((pressed & XINPUT_GAMEPAD_B) != 0) Raise(PadButton.B);
            if ((pressed & XINPUT_GAMEPAD_X) != 0) Raise(PadButton.X);
            if ((pressed & XINPUT_GAMEPAD_Y) != 0) Raise(PadButton.Y);
            if ((pressed & XINPUT_GAMEPAD_START) != 0) Raise(PadButton.Menu); // Menu/☰ button

            // Discrete D-Pad edges, in addition to the continuous ScrollRequested above — screens
            // with a real grid/list selection (CenterMenuWindow) bind these; phases that don't bind
            // them (MainWindow) simply never see them.
            if ((pressed & XINPUT_GAMEPAD_DPAD_UP) != 0) Raise(PadButton.Up);
            if ((pressed & XINPUT_GAMEPAD_DPAD_DOWN) != 0) Raise(PadButton.Down);
            if ((pressed & XINPUT_GAMEPAD_DPAD_LEFT) != 0) Raise(PadButton.Left);
            if ((pressed & XINPUT_GAMEPAD_DPAD_RIGHT) != 0) Raise(PadButton.Right);
        }

        private void Raise(PadButton b) => ButtonPressed?.Invoke(b);

        #region XInput P/Invoke
        private const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        private const ushort XINPUT_GAMEPAD_START = 0x0010; // Menu (☰)
        private const ushort XINPUT_GAMEPAD_A = 0x1000;
        private const ushort XINPUT_GAMEPAD_B = 0x2000;
        private const ushort XINPUT_GAMEPAD_X = 0x4000;
        private const ushort XINPUT_GAMEPAD_Y = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);

        private const uint ERROR_SUCCESS = 0;

        private static bool TryPollCombined(out ushort buttons, out short leftStickX, out short leftStickY, out short rightStickY)
        {
            buttons = 0; leftStickX = 0; leftStickY = 0; rightStickY = 0;
            bool any = false;
            for (uint i = 0; i < 4; i++)
            {
                var state = new XINPUT_STATE();
                if (XInputGetState(i, ref state) != ERROR_SUCCESS) continue;
                any = true;
                buttons |= state.Gamepad.wButtons;
                // Cast to int before Math.Abs: a stick pushed to its exact extreme reports
                // short.MinValue (-32768), and Math.Abs(short) — the exact overload C# picks here —
                // throws OverflowException for MinValue since +32768 doesn't fit back in a short.
                // Math.Abs(int) has no such problem. This was the real crash-on-scroll bug.
                if (Math.Abs((int)state.Gamepad.sThumbLX) > Math.Abs((int)leftStickX)) leftStickX = state.Gamepad.sThumbLX;
                if (Math.Abs((int)state.Gamepad.sThumbLY) > Math.Abs((int)leftStickY)) leftStickY = state.Gamepad.sThumbLY;
                if (Math.Abs((int)state.Gamepad.sThumbRY) > Math.Abs((int)rightStickY)) rightStickY = state.Gamepad.sThumbRY;
            }
            return any;
        }
        #endregion
    }
}
