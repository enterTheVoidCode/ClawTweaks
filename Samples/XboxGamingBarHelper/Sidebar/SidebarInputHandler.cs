using System;
using System.Runtime.InteropServices;
using System.Threading;
using NLog;

namespace XboxGamingBarHelper.Sidebar
{
    internal class SidebarInputHandler : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        #region XInput P/Invoke

        private const uint ERROR_SUCCESS = 0;

        private const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        private const ushort XINPUT_GAMEPAD_A = 0x1000;
        private const ushort XINPUT_GAMEPAD_B = 0x2000;

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

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState910(uint dwUserIndex, ref XINPUT_STATE pState);

        private delegate uint XInputGetStateDelegate(uint dwUserIndex, ref XINPUT_STATE pState);
        private static XInputGetStateDelegate _xinputGetState;
        private static bool _xinputInitialized;

        #endregion

        // Button actions
        internal Action OnDPadUp;
        internal Action OnDPadDown;
        internal Action OnDPadLeft;
        internal Action OnDPadRight;
        internal Action OnButtonA;
        internal Action OnButtonB;
        internal Action OnLeftTrigger;
        internal Action OnRightTrigger;

        // Trigger threshold
        private const byte TriggerThreshold = 128;

        // Thread control
        private Thread _pollThread;
        private volatile bool _running;
        private bool _disposed;

        // Debounce tracking per button
        private const int InitialDelayMs = 200;
        private const int RepeatDelayMs = 120;
        private const int ButtonDebounceMs = 200;

        internal SidebarInputHandler()
        {
            InitializeXInput();
        }

        private static void InitializeXInput()
        {
            if (_xinputInitialized) return;
            _xinputInitialized = true;

            try
            {
                var state = new XINPUT_STATE();
                XInputGetState14(0, ref state);
                _xinputGetState = XInputGetState14;
                Logger.Info("SidebarInput: Using xinput1_4.dll");
            }
            catch
            {
                try
                {
                    var state = new XINPUT_STATE();
                    XInputGetState910(0, ref state);
                    _xinputGetState = XInputGetState910;
                    Logger.Info("SidebarInput: Using xinput9_1_0.dll");
                }
                catch (Exception ex)
                {
                    Logger.Error($"SidebarInput: Failed to load XInput: {ex.Message}");
                    _xinputGetState = null;
                }
            }
        }

        internal void Start()
        {
            if (_xinputGetState == null)
            {
                Logger.Error("SidebarInput: Cannot start - XInput not available");
                return;
            }

            if (_running) return;
            _running = true;

            _pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "SidebarInputPoller",
            };
            _pollThread.Start();
            Logger.Info("SidebarInput: Started polling");
        }

        internal void Stop()
        {
            _running = false;
            _pollThread?.Join(500);
            _pollThread = null;
            Logger.Info("SidebarInput: Stopped polling");
        }

        private void PollLoop()
        {
            ushort prevButtons = 0;
            bool prevLT = false;
            bool prevRT = false;
            var buttonHeldSince = new long[6]; // UP, DOWN, LEFT, RIGHT, A, B
            var buttonLastFired = new long[6];
            var buttonMasks = new ushort[]
            {
                XINPUT_GAMEPAD_DPAD_UP, XINPUT_GAMEPAD_DPAD_DOWN,
                XINPUT_GAMEPAD_DPAD_LEFT, XINPUT_GAMEPAD_DPAD_RIGHT,
                XINPUT_GAMEPAD_A, XINPUT_GAMEPAD_B,
            };
            var buttonActions = new Action[6];

            while (_running)
            {
                // Refresh action references each iteration (they can change)
                buttonActions[0] = OnDPadUp;
                buttonActions[1] = OnDPadDown;
                buttonActions[2] = OnDPadLeft;
                buttonActions[3] = OnDPadRight;
                buttonActions[4] = OnButtonA;
                buttonActions[5] = OnButtonB;

                var state = new XINPUT_STATE();
                bool found = false;

                for (uint i = 0; i < 4; i++)
                {
                    if (_xinputGetState(i, ref state) == ERROR_SUCCESS)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    Thread.Sleep(16);
                    continue;
                }

                ushort buttons = state.Gamepad.wButtons;
                long nowTicks = Environment.TickCount;

                for (int b = 0; b < 6; b++)
                {
                    bool isPressed = (buttons & buttonMasks[b]) != 0;
                    bool wasPressed = (prevButtons & buttonMasks[b]) != 0;

                    if (isPressed && !wasPressed)
                    {
                        // Fresh press
                        buttonHeldSince[b] = nowTicks;
                        buttonLastFired[b] = nowTicks;
                        buttonActions[b]?.Invoke();
                    }
                    else if (isPressed && wasPressed)
                    {
                        // Held — apply repeat for D-pad only (indices 0-3)
                        if (b < 4)
                        {
                            long heldMs = nowTicks - buttonHeldSince[b];
                            long sinceFired = nowTicks - buttonLastFired[b];
                            int delay = heldMs < InitialDelayMs ? InitialDelayMs : RepeatDelayMs;

                            if (sinceFired >= delay)
                            {
                                buttonLastFired[b] = nowTicks;
                                buttonActions[b]?.Invoke();
                            }
                        }
                    }
                }

                // Trigger detection (no repeat)
                bool ltPressed = state.Gamepad.bLeftTrigger > TriggerThreshold;
                bool rtPressed = state.Gamepad.bRightTrigger > TriggerThreshold;
                if (ltPressed && !prevLT) OnLeftTrigger?.Invoke();
                if (rtPressed && !prevRT) OnRightTrigger?.Invoke();
                prevLT = ltPressed;
                prevRT = rtPressed;

                prevButtons = buttons;
                Thread.Sleep(16); // ~60Hz
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
