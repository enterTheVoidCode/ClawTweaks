using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace XboxGamingBarHelper.Core
{
    /// <summary>
    /// Manages global hotkey registration and handling for the helper process.
    /// Uses a message-only window to receive WM_HOTKEY messages.
    /// </summary>
    internal class HotkeyManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Windows API constants
        private const int WM_HOTKEY = 0x0312;
        private const int HWND_MESSAGE = -3;

        // Modifier keys for RegisterHotKey
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // Virtual key codes
        public const uint VK_D = 0x44;

        // Windows API imports
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // UIPI (User Interface Privilege Isolation) - allow WM_HOTKEY through elevation boundary
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeFilterStruct);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

        private const uint MSGFLT_ALLOW = 1;
        private const uint MSGFLT_ADD = 1; // For ChangeWindowMessageFilter (process-wide)

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        // Pending hotkey registration request
        private class HotkeyRegistration
        {
            public int Id;
            public uint Modifiers;
            public uint VirtualKey;
            public Action Callback;
            public ManualResetEventSlim CompletedEvent;
            public bool Success;
        }

        // Hotkey tracking
        private IntPtr _hwnd = IntPtr.Zero;
        private Thread _messageThread;
        private volatile bool _running;
        private int _nextHotkeyId = 1;
        private readonly Dictionary<int, Action> _hotkeyCallbacks = new Dictionary<int, Action>();
        private readonly object _lock = new object();
        private readonly ConcurrentQueue<HotkeyRegistration> _pendingRegistrations = new ConcurrentQueue<HotkeyRegistration>();
        private readonly ManualResetEventSlim _windowCreatedEvent = new ManualResetEventSlim(false);

        public HotkeyManager()
        {
            _running = true;
            _messageThread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "HotkeyMessageLoop"
            };
            _messageThread.SetApartmentState(ApartmentState.STA);
            _messageThread.Start();

            // Wait for window to be created
            if (_windowCreatedEvent.Wait(1000))
            {
                Logger.Info("HotkeyManager: Initialized successfully");
            }
            else
            {
                Logger.Warn("HotkeyManager: Message window creation timed out");
            }
        }

        private void MessageLoop()
        {
            try
            {
                // Create a message-only window
                _hwnd = CreateWindowEx(
                    0, "STATIC", "HotkeyMessageWindow", 0,
                    0, 0, 0, 0,
                    new IntPtr(HWND_MESSAGE), IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

                if (_hwnd == IntPtr.Zero)
                {
                    Logger.Error($"HotkeyManager: Failed to create message window. Error: {Marshal.GetLastWin32Error()}");
                    _windowCreatedEvent.Set();
                    return;
                }

                Logger.Info($"HotkeyManager: Message window created (hwnd: {_hwnd})");

                // Allow WM_HOTKEY messages through UIPI (required for elevated processes)
                // WM_HOTKEY is posted to the thread's message queue, so use process-wide filter
                if (!ChangeWindowMessageFilter(WM_HOTKEY, MSGFLT_ADD))
                {
                    int error = Marshal.GetLastWin32Error();
                    Logger.Warn($"HotkeyManager: ChangeWindowMessageFilter failed. Error: {error}");
                }
                else
                {
                    Logger.Info("HotkeyManager: UIPI filter enabled for WM_HOTKEY (process-wide)");
                }

                _windowCreatedEvent.Set();

                // Message pump
                MSG msg;
                while (_running)
                {
                    // Process any pending hotkey registrations (must be done on this thread)
                    while (_pendingRegistrations.TryDequeue(out var registration))
                    {
                        ProcessRegistration(registration);
                    }

                    if (PeekMessage(out msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE = 1
                    {
                        if (msg.message == WM_HOTKEY)
                        {
                            int hotkeyId = (int)msg.wParam;
                            Logger.Info($"HotkeyManager: WM_HOTKEY received for ID {hotkeyId}");

                            Action callback = null;
                            lock (_lock)
                            {
                                _hotkeyCallbacks.TryGetValue(hotkeyId, out callback);
                            }

                            if (callback != null)
                            {
                                try
                                {
                                    // Execute on a thread pool thread to avoid blocking message loop
                                    ThreadPool.QueueUserWorkItem(_ => callback());
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"HotkeyManager: Error executing hotkey callback: {ex.Message}");
                                }
                            }
                        }

                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HotkeyManager: Message loop error: {ex}");
            }
            finally
            {
                // Unregister all hotkeys before destroying window
                lock (_lock)
                {
                    foreach (var id in _hotkeyCallbacks.Keys)
                    {
                        if (_hwnd != IntPtr.Zero)
                        {
                            UnregisterHotKey(_hwnd, id);
                        }
                    }
                    _hotkeyCallbacks.Clear();
                }

                if (_hwnd != IntPtr.Zero)
                {
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }
            }
        }

        private void ProcessRegistration(HotkeyRegistration registration)
        {
            try
            {
                // Use MOD_NOREPEAT to prevent repeated WM_HOTKEY when holding the key
                if (RegisterHotKey(_hwnd, registration.Id, registration.Modifiers | MOD_NOREPEAT, registration.VirtualKey))
                {
                    lock (_lock)
                    {
                        _hotkeyCallbacks[registration.Id] = registration.Callback;
                    }
                    Logger.Info($"HotkeyManager: Registered hotkey ID {registration.Id} (modifiers: 0x{registration.Modifiers:X}, vk: 0x{registration.VirtualKey:X2})");
                    registration.Success = true;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    Logger.Error($"HotkeyManager: Failed to register hotkey ID {registration.Id}. Error: {error}");
                    registration.Success = false;
                }
            }
            finally
            {
                registration.CompletedEvent?.Set();
            }
        }

        /// <summary>
        /// Registers a global hotkey.
        /// </summary>
        /// <param name="modifiers">Modifier keys (MOD_CONTROL, MOD_SHIFT, MOD_ALT, MOD_WIN)</param>
        /// <param name="virtualKey">Virtual key code</param>
        /// <param name="callback">Action to execute when hotkey is pressed</param>
        /// <returns>Hotkey ID if successful, -1 if failed</returns>
        public int RegisterHotkey(uint modifiers, uint virtualKey, Action callback)
        {
            if (_hwnd == IntPtr.Zero)
            {
                Logger.Error("HotkeyManager: Cannot register hotkey - window not created");
                return -1;
            }

            int hotkeyId;
            lock (_lock)
            {
                hotkeyId = _nextHotkeyId++;
            }

            // Queue the registration to be processed on the message loop thread
            var registration = new HotkeyRegistration
            {
                Id = hotkeyId,
                Modifiers = modifiers,
                VirtualKey = virtualKey,
                Callback = callback,
                CompletedEvent = new ManualResetEventSlim(false)
            };

            _pendingRegistrations.Enqueue(registration);

            // Wait for registration to complete (with timeout)
            if (registration.CompletedEvent.Wait(2000))
            {
                return registration.Success ? hotkeyId : -1;
            }
            else
            {
                Logger.Error("HotkeyManager: Hotkey registration timed out");
                return -1;
            }
        }

        /// <summary>
        /// Unregisters a previously registered hotkey.
        /// </summary>
        /// <param name="hotkeyId">The ID returned by RegisterHotkey</param>
        public void UnregisterHotkey(int hotkeyId)
        {
            if (_hwnd == IntPtr.Zero || hotkeyId < 1)
                return;

            if (UnregisterHotKey(_hwnd, hotkeyId))
            {
                lock (_lock)
                {
                    _hotkeyCallbacks.Remove(hotkeyId);
                }
                Logger.Info($"HotkeyManager: Unregistered hotkey ID {hotkeyId}");
            }
            else
            {
                Logger.Warn($"HotkeyManager: Failed to unregister hotkey ID {hotkeyId}");
            }
        }

        public void Dispose()
        {
            _running = false;

            // Wait for message thread to exit
            if (_messageThread != null && _messageThread.IsAlive)
            {
                _messageThread.Join(1000);
            }

            _windowCreatedEvent?.Dispose();

            Logger.Info("HotkeyManager: Disposed");
        }
    }
}
