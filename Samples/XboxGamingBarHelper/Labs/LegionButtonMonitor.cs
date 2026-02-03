using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using NLog;
using Windows.Storage;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Action type for Legion button remap.
    /// </summary>
    internal enum LegionButtonAction
    {
        XboxGuide = 0,
        KeyboardShortcut = 1,
        RunCommand = 2,
        FocusGoTweaks = 3
    }

    /// <summary>
    /// Unified monitor for Legion Go controller HID input.
    /// Monitors both Legion L and Legion R button presses and controller battery status.
    /// Single monitor handles all button/battery parsing from one HID device.
    /// </summary>
    internal class LegionButtonMonitor : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        // Legion Go Controller HID identifiers
        private const ushort LEGION_VID = 0x17EF;
        private const ushort LEGION_GO1_PID = 0x6182;  // Legion Go 1
        private const ushort LEGION_GO2_PID = 0x61EB;  // Legion Go S / Gen 2

        // Legion button position in HID report
        // Attached mode (04:00:A1 header): buttons at byte 16
        // Detached mode (04:XX:XX header): buttons at byte 18
        private const int BUTTON_BYTE_ATTACHED = 16;
        private const int BUTTON_BYTE_DETACHED = 18;
        private const byte LEGION_L_BIT = 0x80;
        private const byte LEGION_R_BIT = 0x40;

        // Detected controller mode
        private bool isDetachedMode = false;
        private int currentButtonByte = BUTTON_BYTE_ATTACHED;

        // Configuration for Legion L button
        private bool legionLEnabled = false;
        private LegionButtonAction legionLActionType = LegionButtonAction.XboxGuide;
        private string legionLShortcutKeys = "";
        private string legionLCommandPath = "";

        // Configuration for Legion R button
        private bool legionREnabled = false;
        private LegionButtonAction legionRActionType = LegionButtonAction.XboxGuide;
        private string legionRShortcutKeys = "";
        private string legionRCommandPath = "";

        // Configuration for Scroll Wheel (unified scroll + click)
        // Note: Raw Input API can't distinguish scroll up/down, so we have unified "scroll" action
        private bool scrollEnabled = false;
        private LegionButtonAction scrollActionType = LegionButtonAction.XboxGuide;
        private string scrollShortcutKeys = "";
        private string scrollCommandPath = "";

        private bool scrollClickEnabled = false;
        private LegionButtonAction scrollClickActionType = LegionButtonAction.XboxGuide;
        private string scrollClickShortcutKeys = "";
        private string scrollClickCommandPath = "";

        // Legacy fields for backward compatibility (deprecated - use scrollEnabled instead)
        private bool scrollUpEnabled = false;
        private LegionButtonAction scrollUpActionType = LegionButtonAction.XboxGuide;
        private string scrollUpShortcutKeys = "";
        private string scrollUpCommandPath = "";
        private bool scrollDownEnabled = false;
        private LegionButtonAction scrollDownActionType = LegionButtonAction.XboxGuide;
        private string scrollDownShortcutKeys = "";
        private string scrollDownCommandPath = "";

        // Callbacks for actions
        private Action<string> onShortcutTriggered;
        private Action<string> onCommandTriggered;
        private Action onFocusGoTweaksTriggered;

        private SafeFileHandle hidHandle;
        private bool _hasWriteAccess = false;  // Track if we have write access for heartbeat
        private readonly object _hidLock = new object();  // Lock for HID operations to prevent race conditions
        private Thread monitorThread;
        private volatile bool isRunning = false;
        private volatile bool isDisposed = false;

        // Scroll wheel click state tracking
        private bool lastScrollClickState = false;
        private DateTime lastScrollClickActionTime = DateTime.MinValue;
        private DateTime scrollClickPressTime = DateTime.MinValue; // Track when scroll click was pressed for minimum hold time
        private const int SCROLL_CLICK_COOLDOWN_MS = 400; // Minimum time between scroll click actions for Game Bar toggle
        private const int SCROLL_CLICK_MIN_HOLD_MS = 150; // Minimum time to hold Xbox Guide button for Game Bar to register

        // Scroll wheel Raw Input monitor thread
        // Uses Raw Input API to capture mouse events from Legion Go mi_01/col02 interface
        private Thread scrollWheelThread;
        private volatile bool scrollWheelRunning;

        // Track button states for both L and R
        private bool lastLegionLState = false;
        private bool lastLegionRState = false;

        private ViGEmController vigemController;
        private bool ownsViGEmController = false;  // True if we created the controller
        private Action<bool> onButtonStateChanged;

        // Battery monitoring - parsed from the same HID reports
        private int _lastLeftBattery = -1;
        private int _lastRightBattery = -1;
        private bool _lastLeftCharging = false;
        private bool _lastRightCharging = false;
        private bool _lastLeftConnected = false;
        private bool _lastRightConnected = false;
        private byte _lastLeftChargingByte = 0;
        private byte _lastRightChargingByte = 0;
        private DateTime _lastBatteryUpdateTime = DateTime.MinValue;
        private const int BATTERY_UPDATE_THROTTLE_MS = 2000;  // Only update battery every 2 seconds

        // Track when output reports are sent to skip button detection (prevents false triggers)
        private DateTime _lastOutputReportTime = DateTime.MinValue;
        private const int OUTPUT_REPORT_IGNORE_MS = 100;  // Ignore button reads for 100ms after output

        // Detected device info
        private ushort _detectedVid = 0;
        private ushort _detectedPid = 0;

        // Cached device path for faster reconnection (persisted to settings)
        private static string _cachedDevicePath = null;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Get or set the cached HID device path for faster startup.
        /// Call LoadCachedDevicePath() at startup and SaveCachedDevicePath() when a device is found.
        /// </summary>
        public static string CachedDevicePath
        {
            get { lock (_cacheLock) return _cachedDevicePath; }
            set
            {
                lock (_cacheLock)
                {
                    _cachedDevicePath = value;
                    SaveCachedDevicePathToSettings(value);
                }
            }
        }

        private const string CachedDevicePathSettingsKey = "LegionHIDDevicePath";

        /// <summary>
        /// Load the cached device path from LocalSettings at startup.
        /// Call this once when the helper starts.
        /// </summary>
        public static void LoadCachedDevicePathFromSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(CachedDevicePathSettingsKey))
                {
                    string path = settings.Values[CachedDevicePathSettingsKey] as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        lock (_cacheLock)
                        {
                            _cachedDevicePath = path;
                        }
                        Logger.Info($"LegionButtonMonitor: Loaded cached device path from settings");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"LegionButtonMonitor: Failed to load cached device path: {ex.Message}");
            }
        }

        private static void SaveCachedDevicePathToSettings(string path)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (string.IsNullOrEmpty(path))
                {
                    settings.Values.Remove(CachedDevicePathSettingsKey);
                }
                else
                {
                    settings.Values[CachedDevicePathSettingsKey] = path;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"LegionButtonMonitor: Failed to save cached device path: {ex.Message}");
            }
        }

        // Static timestamp for external output reports (e.g., from LegionGoLibrary brightness commands)
        // This allows other code to notify us when they send HID output reports to the controller
        private static DateTime _lastExternalOutputReportTime = DateTime.MinValue;
        private static readonly object _externalOutputLock = new object();

        /// <summary>
        /// Notify that an external HID output report was sent to the Legion controller.
        /// This prevents false button triggers when other code (e.g., LegionGoLibrary) sends commands.
        /// Call this immediately before sending any HID output report to the Legion controller.
        /// </summary>
        public static void NotifyOutputReportSent()
        {
            lock (_externalOutputLock)
            {
                _lastExternalOutputReportTime = DateTime.Now;
            }
        }

        /// <summary>
        /// Event raised when controller battery status is updated.
        /// Battery data is parsed from the same HID reports used for button monitoring.
        /// </summary>
        public event EventHandler<LegionButtonBatteryEventArgs> BatteryUpdated;

        // P/Invoke declarations
        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            ref NativeOverlapped lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(
            SafeFileHandle hFile,
            ref NativeOverlapped lpOverlapped,
            out uint lpNumberOfBytesTransferred,
            bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(SafeFileHandle hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(
            SafeFileHandle hidDeviceObject,
            byte[] lpReportBuffer,
            uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        private const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x01;
        private const uint FILE_SHARE_WRITE = 0x02;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint WAIT_OBJECT_0 = 0x00000000;
        private const uint WAIT_TIMEOUT = 0x00000102;
        private const int ERROR_IO_PENDING = 997;

        // Raw Input API constants and structures for scroll wheel monitoring
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIM_TYPEMOUSE = 0;
        private const uint WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint PM_REMOVE = 1;
        private const int HWND_MESSAGE = -3;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWMOUSE
        {
            public ushort usFlags;
            public ushort usButtonFlags;
            public ushort usButtonData;
            public uint ulRawButtons;
            public int lLastX;
            public int lLastY;
            public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUT_MOUSE
        {
            public RAWINPUTHEADER header;
            public RAWMOUSE mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        private static WndProcDelegate _scrollWndProcDelegate; // prevent GC

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent,
            IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Scroll wheel Raw Input window handle
        private IntPtr scrollWheelWindowHandle = IntPtr.Zero;

        // Diagnostic tracking for Raw Input thread
        private DateTime lastScrollHeartbeat = DateTime.MinValue;
        private DateTime lastScrollInputReceived = DateTime.MinValue;
        private int scrollInputCount = 0;
        private bool hasReceivedScrollInput = false; // Track if we've ever received input
        private const int HEARTBEAT_INTERVAL_MS = 30000; // Log heartbeat every 30 seconds
        private const int NO_INPUT_REREGISTER_MS = 15000; // Re-register Raw Input if no input for 15 seconds (after previously receiving input)

        /// <summary>
        /// Initialize the unified monitor for both Legion L and R buttons.
        /// </summary>
        public LegionButtonMonitor(Action<bool> onStateChanged = null)
        {
            onButtonStateChanged = onStateChanged;
        }

        /// <summary>
        /// Configure a Legion button's action. Can be called multiple times to configure both L and R.
        /// </summary>
        /// <param name="button">"L" for Legion L, "R" for Legion R</param>
        /// <param name="enabled">Whether to enable the remap for this button</param>
        /// <param name="action">0 = Xbox Guide, 1 = Keyboard Shortcut, 2 = Run Command, 3 = Focus GoTweaks</param>
        /// <param name="shortcutOrCommand">Keyboard shortcut string (e.g., "Win+G") or command path</param>
        /// <param name="shortcutCallback">Callback to execute keyboard shortcut</param>
        /// <param name="commandCallback">Callback to execute command (optional)</param>
        /// <param name="focusGoTweaksCallback">Callback to focus GoTweaks widget (optional)</param>
        public void ConfigureButton(string button, bool enabled, int action, string shortcutOrCommand,
            Action<string> shortcutCallback, Action<string> commandCallback = null, Action focusGoTweaksCallback = null)
        {
            // Store callbacks (shared between both buttons)
            onShortcutTriggered = shortcutCallback;
            onCommandTriggered = commandCallback;
            onFocusGoTweaksTriggered = focusGoTweaksCallback;

            bool isLegionL = button == "L";
            var actionType = (LegionButtonAction)action;
            string shortcutKeys = actionType == LegionButtonAction.RunCommand ? "" : (shortcutOrCommand ?? "");
            string commandPath = actionType == LegionButtonAction.RunCommand ? (shortcutOrCommand ?? "") : "";

            if (isLegionL)
            {
                legionLEnabled = enabled;
                legionLActionType = actionType;
                legionLShortcutKeys = shortcutKeys;
                legionLCommandPath = commandPath;
            }
            else
            {
                legionREnabled = enabled;
                legionRActionType = actionType;
                legionRShortcutKeys = shortcutKeys;
                legionRCommandPath = commandPath;
            }

            string buttonName = isLegionL ? "Legion L" : "Legion R";
            string actionName = actionType == LegionButtonAction.XboxGuide ? "Xbox Guide" :
                               actionType == LegionButtonAction.KeyboardShortcut ? $"Shortcut: {shortcutKeys}" :
                               actionType == LegionButtonAction.RunCommand ? $"Command: {commandPath}" :
                               "Focus GoTweaks";
            Logger.Info($"LegionButtonMonitor: Configured {buttonName} - Enabled: {enabled}, Action: {actionName}");

            // If monitor is already running and we now need ViGEm, create it
            if (isRunning && NeedsViGEm && vigemController == null)
            {
                EnsureViGEmController();
            }
        }

        /// <summary>
        /// Configure a scroll wheel action (Up, Down, or Click).
        /// </summary>
        /// <param name="direction">"Up", "Down", or "Click"</param>
        /// <param name="enabled">Whether to enable the remap for this action</param>
        /// <param name="action">0 = Xbox Guide, 1 = Keyboard Shortcut, 2 = Run Command, 3 = Focus GoTweaks</param>
        /// <param name="shortcutOrCommand">Keyboard shortcut string or command path</param>
        /// <param name="shortcutCallback">Callback to execute keyboard shortcut</param>
        /// <param name="commandCallback">Callback to execute command (optional)</param>
        /// <param name="focusGoTweaksCallback">Callback to focus GoTweaks widget (optional)</param>
        public void ConfigureScrollWheel(string direction, bool enabled, int action, string shortcutOrCommand,
            Action<string> shortcutCallback, Action<string> commandCallback = null, Action focusGoTweaksCallback = null)
        {
            // Store callbacks (shared between all actions)
            onShortcutTriggered = shortcutCallback;
            onCommandTriggered = commandCallback;
            onFocusGoTweaksTriggered = focusGoTweaksCallback;

            var actionType = (LegionButtonAction)action;
            string shortcutKeys = actionType == LegionButtonAction.RunCommand ? "" : (shortcutOrCommand ?? "");
            string commandPath = actionType == LegionButtonAction.RunCommand ? (shortcutOrCommand ?? "") : "";

            switch (direction.ToLower())
            {
                case "scroll":
                    // Unified scroll action (direction not available via Raw Input API)
                    scrollEnabled = enabled;
                    scrollActionType = actionType;
                    scrollShortcutKeys = shortcutKeys;
                    scrollCommandPath = commandPath;
                    break;
                case "up":
                    // Legacy - now handled by unified "scroll"
                    scrollUpEnabled = enabled;
                    scrollUpActionType = actionType;
                    scrollUpShortcutKeys = shortcutKeys;
                    scrollUpCommandPath = commandPath;
                    break;
                case "down":
                    // Legacy - now handled by unified "scroll"
                    scrollDownEnabled = enabled;
                    scrollDownActionType = actionType;
                    scrollDownShortcutKeys = shortcutKeys;
                    scrollDownCommandPath = commandPath;
                    break;
                case "click":
                    scrollClickEnabled = enabled;
                    scrollClickActionType = actionType;
                    scrollClickShortcutKeys = shortcutKeys;
                    scrollClickCommandPath = commandPath;
                    break;
                default:
                    Logger.Warn($"LegionButtonMonitor: Unknown scroll direction '{direction}'");
                    return;
            }

            string actionName = actionType == LegionButtonAction.XboxGuide ? "Xbox Guide" :
                               actionType == LegionButtonAction.KeyboardShortcut ? $"Shortcut: {shortcutKeys}" :
                               actionType == LegionButtonAction.RunCommand ? $"Command: {commandPath}" :
                               "Focus GoTweaks";
            Logger.Info($"LegionButtonMonitor: Configured Scroll {direction} - Enabled: {enabled}, Action: {actionName}");

            // If monitor is already running and we now need ViGEm, create it
            if (isRunning && NeedsViGEm && vigemController == null)
            {
                EnsureViGEmController();
            }

            // If monitor is already running and scroll is now configured, start the scroll wheel thread
            // This handles the case where scroll is configured after the monitor is already running for buttons/battery
            if (isRunning && HasAnyScrollConfigured && (scrollWheelThread == null || !scrollWheelThread.IsAlive))
            {
                scrollWheelRunning = true;
                scrollWheelThread = new Thread(ScrollWheelThreadProc)
                {
                    IsBackground = true,
                    Name = "LegionScrollWheel"
                };
                scrollWheelThread.Start();
                Logger.Info("LegionButtonMonitor: Scroll wheel Raw Input monitor thread started (hot-configured)");
            }
        }

        /// <summary>
        /// Ensures ViGEmController is created and connected if Xbox Guide action is configured.
        /// Call this after ConfigureButton or when button config changes.
        /// </summary>
        public bool EnsureViGEmController()
        {
            if (!NeedsViGEm)
            {
                return true; // Not needed
            }

            if (vigemController != null)
            {
                return true; // Already exists
            }

            Logger.Info("LegionButtonMonitor: Creating ViGEmController (Xbox Guide action configured)");
            vigemController = new ViGEmController();
            ownsViGEmController = true;

            if (!vigemController.Connect())
            {
                Logger.Error("LegionButtonMonitor: Failed to connect to ViGEmBus");
                vigemController = null;
                ownsViGEmController = false;
                return false;
            }

            if (!vigemController.PlugIn())
            {
                Logger.Error("LegionButtonMonitor: Failed to plug in virtual controller");
                vigemController.Dispose();
                vigemController = null;
                ownsViGEmController = false;
                return false;
            }

            Logger.Info("LegionButtonMonitor: ViGEmController created and plugged in successfully");
            return true;
        }

        /// <summary>
        /// Get whether ViGEmBus is needed for the current configuration.
        /// </summary>
        public bool NeedsViGEm => (legionLEnabled && legionLActionType == LegionButtonAction.XboxGuide) ||
                                  (legionREnabled && legionRActionType == LegionButtonAction.XboxGuide) ||
                                  (scrollUpEnabled && scrollUpActionType == LegionButtonAction.XboxGuide) ||
                                  (scrollDownEnabled && scrollDownActionType == LegionButtonAction.XboxGuide) ||
                                  (scrollClickEnabled && scrollClickActionType == LegionButtonAction.XboxGuide);

        /// <summary>
        /// Get whether any button is configured.
        /// </summary>
        public bool HasAnyButtonConfigured => legionLEnabled || legionREnabled;

        /// <summary>
        /// Get whether any scroll wheel action is configured.
        /// </summary>
        public bool HasAnyScrollConfigured => scrollEnabled || scrollClickEnabled || scrollUpEnabled || scrollDownEnabled;

        /// <summary>
        /// Get whether the monitor is currently running.
        /// </summary>
        public bool IsRunning => isRunning;

        /// <summary>
        /// Get the detected VID:PID as a formatted string (e.g., "17EF:6182").
        /// Returns empty string if no device detected.
        /// </summary>
        public string DetectedVidPid => _detectedVid != 0 ? $"{_detectedVid:X4}:{_detectedPid:X4}" : "";

        /// <summary>
        /// Get whether the controller is in detached/uninitialized mode.
        /// </summary>
        public bool IsDetachedMode => isDetachedMode;

        /// <summary>
        /// Check if ViGEm controller needs to be (re)initialized based on current configuration.
        /// Returns true if we need ViGEm but don't have a controller, or have one but don't need it.
        /// </summary>
        public bool NeedsViGEmRestart => (NeedsViGEm && vigemController == null) || (!NeedsViGEm && vigemController != null);

        /// <summary>
        /// Start monitoring the configured Legion buttons (L and/or R).
        /// </summary>
        public bool Start()
        {
            if (isRunning)
                return true;

            // Check if we have any button or scroll configured
            if (!HasAnyButtonConfigured && !HasAnyScrollConfigured)
            {
                Logger.Warn("LegionButtonMonitor: No buttons or scroll configured, not starting");
                return false;
            }

            // Initialize ViGEmBus controller only if any action is Xbox Guide
            if (NeedsViGEm)
            {
                vigemController = new ViGEmController();
                ownsViGEmController = true;
                if (!vigemController.Connect())
                {
                    Logger.Error("LegionButtonMonitor: Failed to connect to ViGEmBus");
                    return false;
                }

                if (!vigemController.PlugIn())
                {
                    Logger.Error("LegionButtonMonitor: Failed to plug in virtual controller");
                    vigemController.Dispose();
                    vigemController = null;
                    return false;
                }
            }

            // Try to find and open Legion controller HID device
            // Even if not found initially, start the monitor thread which will retry
            bool controllerFound = OpenLegionController();
            if (!controllerFound)
            {
                Logger.Warn("LegionButtonMonitor: Controller not found initially, will retry in background");
            }

            // Start monitoring thread - it will handle reconnection if controller not found
            isRunning = true;
            monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "LegionButtonMonitor"
            };
            monitorThread.Start();

            // Start scroll wheel thread if any scroll action is configured
            // Uses Raw Input API to capture mouse events from Legion Go mi_01/col02 interface
            if (HasAnyScrollConfigured)
            {
                scrollWheelRunning = true;
                scrollWheelThread = new Thread(ScrollWheelThreadProc)
                {
                    IsBackground = true,
                    Name = "LegionScrollWheel"
                };
                scrollWheelThread.Start();
                Logger.Info("LegionButtonMonitor: Scroll wheel Raw Input monitor thread started");
            }

            string buttons = "";
            if (legionLEnabled) buttons += "L";
            if (legionREnabled) buttons += (buttons.Length > 0 ? " + R" : "R");
            if (controllerFound)
            {
                Logger.Info($"LegionButtonMonitor: Started monitoring Legion {buttons} button(s)");
            }
            else
            {
                Logger.Info($"LegionButtonMonitor: Started in background for Legion {buttons}, waiting for controller connection");
            }

            return true;
        }

        /// <summary>
        /// Stop monitoring.
        /// </summary>
        public void Stop()
        {
            if (!isRunning)
                return;

            isRunning = false;

            // Stop scroll wheel Raw Input thread
            scrollWheelRunning = false;
            if (scrollWheelThread != null && scrollWheelThread.IsAlive)
            {
                // Thread will exit its message loop and clean up its window
                scrollWheelThread.Join(2000);
                scrollWheelThread = null;
            }

            // Wait for main monitor thread to finish
            if (monitorThread != null && monitorThread.IsAlive)
            {
                monitorThread.Join(1000);
                monitorThread = null;
            }

            // Release Guide button if either was pressed
            if ((lastLegionLState || lastLegionRState) && vigemController != null)
            {
                vigemController.SetGuide(false);
                lastLegionLState = false;
                lastLegionRState = false;
            }

            // Close HID handle
            if (hidHandle != null && !hidHandle.IsInvalid)
            {
                hidHandle.Close();
                hidHandle = null;
            }

            // Dispose ViGEmBus controller only if we created it
            if (vigemController != null && ownsViGEmController)
            {
                vigemController.Dispose();
            }
            vigemController = null;
            ownsViGEmController = false;

            Logger.Info("LegionButtonMonitor: Stopped");
        }

        /// <summary>
        /// Start the monitor for battery monitoring only (no button remapping).
        /// This allows battery data to be collected even when no buttons are configured.
        /// </summary>
        /// <returns>True if successfully started</returns>
        public bool StartForBatteryMonitoring()
        {
            if (isRunning)
                return true;

            // Try to find and open Legion controller HID device
            // Even if not found initially, start the monitor thread which will retry
            bool controllerFound = OpenLegionController();
            if (!controllerFound)
            {
                Logger.Warn("LegionButtonMonitor: Controller not found initially, will retry in background");
            }

            // Start monitoring thread - it will handle reconnection if controller not found
            isRunning = true;
            monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "LegionButtonMonitor"
            };
            monitorThread.Start();

            if (controllerFound)
            {
                Logger.Info("LegionButtonMonitor: Started for battery monitoring only");
            }
            else
            {
                Logger.Info("LegionButtonMonitor: Started in background, waiting for controller connection");
            }
            return true;
        }

        private bool OpenLegionController()
        {
            try
            {
                // Try cached device path first for faster startup
                string cachedPath = CachedDevicePath;
                if (!string.IsNullOrEmpty(cachedPath))
                {
                    Logger.Info($"LegionButtonMonitor: Trying cached device path first: {cachedPath}");
                    if (TryOpenDeviceAtPath(cachedPath, out SafeFileHandle cachedHandle, out bool cachedWriteAccess))
                    {
                        if (ProbeDeviceFormat(cachedHandle, cachedWriteAccess))
                        {
                            hidHandle = cachedHandle;
                            _hasWriteAccess = cachedWriteAccess;
                            Logger.Info($"LegionButtonMonitor: Cached device path worked! VID:{_detectedVid:X4} PID:{_detectedPid:X4}");
                            return true;
                        }
                        else
                        {
                            Logger.Info("LegionButtonMonitor: Cached device path no longer valid, scanning all devices");
                            cachedHandle.Close();
                            CachedDevicePath = null; // Clear invalid cache
                        }
                    }
                    else
                    {
                        Logger.Info("LegionButtonMonitor: Cached device path not accessible, scanning all devices");
                        CachedDevicePath = null; // Clear invalid cache
                    }
                }

                HidD_GetHidGuid(out Guid hidGuid);

                IntPtr deviceInfoSet = SetupDiGetClassDevs(
                    ref hidGuid,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

                if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                {
                    Logger.Error("LegionButtonMonitor: Failed to get device info set");
                    return false;
                }

                try
                {
                    var interfaceData = new SP_DEVICE_INTERFACE_DATA
                    {
                        cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                    };

                    int candidateCount = 0;
                    uint memberIndex = 0;
                    while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, memberIndex, ref interfaceData))
                    {
                        // Get required buffer size
                        SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

                        // Allocate buffer for device path
                        IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            // Set cbSize for SP_DEVICE_INTERFACE_DETAIL_DATA
                            Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);

                            if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                            {
                                // Get device path (starts at offset 4)
                                string devicePath = Marshal.PtrToStringAuto(detailDataBuffer + 4);

                                // Try to open device with overlapped I/O for timeout support
                                // First try with read + write access (needed for initialization command)
                                // Fall back to read-only if write access is denied
                                var handle = CreateFile(
                                    devicePath,
                                    GENERIC_READ | GENERIC_WRITE,
                                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                                    IntPtr.Zero,
                                    OPEN_EXISTING,
                                    FILE_FLAG_OVERLAPPED,
                                    IntPtr.Zero);

                                bool hasWriteAccess = !handle.IsInvalid;
                                if (handle.IsInvalid)
                                {
                                    // Fallback to read-only access (initialization won't work but we can still monitor)
                                    handle = CreateFile(
                                        devicePath,
                                        GENERIC_READ,
                                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                                        IntPtr.Zero,
                                        OPEN_EXISTING,
                                        FILE_FLAG_OVERLAPPED,
                                        IntPtr.Zero);
                                }

                                if (!handle.IsInvalid)
                                {
                                    // Check VID/PID
                                    var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                                    if (HidD_GetAttributes(handle, ref attrs))
                                    {
                                        if (attrs.VendorID == LEGION_VID &&
                                            (attrs.ProductID == LEGION_GO1_PID || attrs.ProductID == LEGION_GO2_PID))
                                        {
                                            candidateCount++;
                                            Logger.Info($"LegionButtonMonitor: Found candidate #{candidateCount} at {devicePath} (write access: {hasWriteAccess})");

                                            // Probe the device by reading a report to verify it's the correct format
                                            // The correct device sends 64-byte reports with 04:00:A1 header
                                            if (ProbeDeviceFormat(handle, hasWriteAccess))
                                            {
                                                hidHandle = handle;
                                                _hasWriteAccess = hasWriteAccess;
                                                _detectedVid = attrs.VendorID;
                                                _detectedPid = attrs.ProductID;
                                                Logger.Info($"LegionButtonMonitor: Selected device #{candidateCount} - VID:{_detectedVid:X4} PID:{_detectedPid:X4} (write: {hasWriteAccess})");

                                                // Cache the successful device path for faster startup next time
                                                CachedDevicePath = devicePath;
                                                Logger.Info($"LegionButtonMonitor: Cached device path for future use");
                                                return true;
                                            }
                                            else
                                            {
                                                Logger.Info($"LegionButtonMonitor: Device #{candidateCount} rejected - wrong report format");
                                            }
                                        }
                                    }
                                    handle.Close();
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailDataBuffer);
                        }

                        memberIndex++;
                    }

                    if (candidateCount > 0)
                    {
                        Logger.Warn($"LegionButtonMonitor: Found {candidateCount} Legion HID devices but none had correct 64-byte format");
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }

                Logger.Warn("LegionButtonMonitor: Legion controller not found");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"LegionButtonMonitor: Exception opening controller: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Try to open a specific HID device at the given path.
        /// </summary>
        private bool TryOpenDeviceAtPath(string devicePath, out SafeFileHandle handle, out bool hasWriteAccess)
        {
            handle = null;
            hasWriteAccess = false;

            try
            {
                // Try with read + write access first
                handle = CreateFile(
                    devicePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);

                hasWriteAccess = !handle.IsInvalid;
                if (handle.IsInvalid)
                {
                    // Fallback to read-only
                    handle = CreateFile(
                        devicePath,
                        GENERIC_READ,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_FLAG_OVERLAPPED,
                        IntPtr.Zero);
                }

                if (handle.IsInvalid)
                {
                    handle = null;
                    return false;
                }

                // Verify VID/PID
                var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (HidD_GetAttributes(handle, ref attrs))
                {
                    if (attrs.VendorID == LEGION_VID &&
                        (attrs.ProductID == LEGION_GO1_PID || attrs.ProductID == LEGION_GO2_PID))
                    {
                        _detectedVid = attrs.VendorID;
                        _detectedPid = attrs.ProductID;
                        return true;
                    }
                }

                handle.Close();
                handle = null;
                return false;
            }
            catch (Exception ex)
            {
                Logger.Debug($"LegionButtonMonitor: TryOpenDeviceAtPath exception: {ex.Message}");
                if (handle != null && !handle.IsInvalid)
                {
                    handle.Close();
                }
                handle = null;
                return false;
            }
        }

        /// <summary>
        /// Process a scroll action (up/down) - these are instant actions, not press/release.
        /// </summary>
        private void ProcessScrollAction(string actionName, LegionButtonAction actionType, string shortcutKeys, string commandPath)
        {
            Logger.Info($"LegionButtonMonitor: {actionName} triggered - action={actionType}");

            switch (actionType)
            {
                case LegionButtonAction.XboxGuide:
                    if (vigemController != null)
                    {
                        // Press and release Guide button quickly for scroll actions
                        vigemController.SetGuide(true);
                        Thread.Sleep(50);
                        vigemController.SetGuide(false);
                    }
                    break;

                case LegionButtonAction.KeyboardShortcut:
                    if (!string.IsNullOrEmpty(shortcutKeys))
                    {
                        Logger.Info($"LegionButtonMonitor: Executing shortcut '{shortcutKeys}', callback={(onShortcutTriggered != null ? "set" : "NULL")}");
                        try
                        {
                            if (onShortcutTriggered != null)
                            {
                                onShortcutTriggered.Invoke(shortcutKeys);
                            }
                            else
                            {
                                Logger.Warn("LegionButtonMonitor: Shortcut callback is null!");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"LegionButtonMonitor: Scroll shortcut exception: {ex.Message}");
                        }
                    }
                    else
                    {
                        Logger.Warn($"LegionButtonMonitor: Shortcut keys is empty!");
                    }
                    break;

                case LegionButtonAction.RunCommand:
                    if (!string.IsNullOrEmpty(commandPath))
                    {
                        try
                        {
                            onCommandTriggered?.Invoke(commandPath);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"LegionButtonMonitor: Scroll command exception: {ex.Message}");
                        }
                    }
                    break;

                case LegionButtonAction.FocusGoTweaks:
                    try
                    {
                        onFocusGoTweaksTriggered?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"LegionButtonMonitor: Scroll FocusGoTweaks exception: {ex.Message}");
                    }
                    break;
            }
        }

        /// <summary>
        /// WndProc callback for the Raw Input message window.
        /// Simply passes messages to DefWindowProc.
        /// </summary>
        private static IntPtr ScrollWheelWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProcW(hWnd, uMsg, wParam, lParam);
        }

        /// <summary>
        /// Creates a message-only window to receive Raw Input events.
        /// </summary>
        private IntPtr CreateScrollWheelRawInputWindow()
        {
            try
            {
                // Only assign delegate once to prevent GC issues when window class is reused
                // (window class retains the original function pointer even after re-registration)
                if (_scrollWndProcDelegate == null)
                {
                    _scrollWndProcDelegate = ScrollWheelWndProc;
                }
                IntPtr hInstance = GetModuleHandle(null);

                var wc = new WNDCLASS
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_scrollWndProcDelegate),
                    hInstance = hInstance,
                    lpszClassName = "LegionScrollWheelRawInput"
                };

                ushort atom = RegisterClassW(ref wc);
                // 1410 = ERROR_CLASS_ALREADY_EXISTS which is OK
                if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
                {
                    Logger.Error($"LegionButtonMonitor: Failed to register window class (error={Marshal.GetLastWin32Error()})");
                    return IntPtr.Zero;
                }

                // Create message-only window (HWND_MESSAGE = -3)
                IntPtr hwnd = CreateWindowExW(0, "LegionScrollWheelRawInput", "LegionScrollWheel",
                    0, 0, 0, 0, 0, new IntPtr(HWND_MESSAGE), IntPtr.Zero, hInstance, IntPtr.Zero);

                if (hwnd == IntPtr.Zero)
                {
                    Logger.Error($"LegionButtonMonitor: Failed to create window (error={Marshal.GetLastWin32Error()})");
                }

                return hwnd;
            }
            catch (Exception ex)
            {
                Logger.Error($"LegionButtonMonitor: CreateScrollWheelRawInputWindow exception: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Initializes Raw Input registration for mouse events (to capture scroll wheel).
        /// </summary>
        private bool InitializeScrollWheelRawInput(IntPtr hwnd)
        {
            try
            {
                // Register for mouse input (scroll wheel reports as mouse)
                var rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = 0x01;  // Generic Desktop
                rid[0].usUsage = 0x02;       // Mouse
                rid[0].dwFlags = RIDEV_INPUTSINK;  // Receive input even when not focused
                rid[0].hwndTarget = hwnd;

                if (!RegisterRawInputDevices(rid, 1, Marshal.SizeOf<RAWINPUTDEVICE>()))
                {
                    Logger.Error($"LegionButtonMonitor: Failed to register for Raw Input (error={Marshal.GetLastWin32Error()})");
                    return false;
                }

                Logger.Info("LegionButtonMonitor: Registered for Raw Input mouse events");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"LegionButtonMonitor: InitializeScrollWheelRawInput exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the device name from a Raw Input device handle.
        /// </summary>
        private string GetRawInputDeviceName(IntPtr hDevice)
        {
            try
            {
                uint size = 0;
                GetRawInputDeviceInfoW(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size);
                if (size == 0) return null;

                IntPtr buffer = Marshal.AllocHGlobal((int)(size * 2));
                try
                {
                    if (GetRawInputDeviceInfoW(hDevice, RIDI_DEVICENAME, buffer, ref size) > 0)
                        return Marshal.PtrToStringUni(buffer);
                    return null;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Dedicated thread procedure for monitoring scroll wheel via Raw Input API.
        /// Uses Windows Raw Input to capture mouse events from the Legion Go scroll wheel
        /// interface (mi_01/col02). This works even though the device is "locked" by Windows.
        ///
        /// Raw Input button data:
        /// - 0x0010 = scroll click pressed
        /// - 0x0020 = scroll click released
        /// - 0x0400 = scroll event (check ulRawButtons for direction)
        /// </summary>
        private void ScrollWheelThreadProc()
        {
            Logger.Info("LegionButtonMonitor: Scroll wheel Raw Input thread started");

            try
            {
                // Create message-only window for Raw Input
                scrollWheelWindowHandle = CreateScrollWheelRawInputWindow();
                if (scrollWheelWindowHandle == IntPtr.Zero)
                {
                    Logger.Error("LegionButtonMonitor: Failed to create Raw Input window");
                    return;
                }

                // Register for Raw Input mouse events
                if (!InitializeScrollWheelRawInput(scrollWheelWindowHandle))
                {
                    Logger.Error("LegionButtonMonitor: Failed to initialize Raw Input");
                    DestroyWindow(scrollWheelWindowHandle);
                    scrollWheelWindowHandle = IntPtr.Zero;
                    return;
                }

                Logger.Info("LegionButtonMonitor: Scroll wheel Raw Input initialized, listening for Legion Go mi_01/col02 events");

                // Initialize diagnostic tracking
                lastScrollHeartbeat = DateTime.Now;
                lastScrollInputReceived = DateTime.Now;
                scrollInputCount = 0;
                hasReceivedScrollInput = false;

                // Message loop
                while (scrollWheelRunning)
                {
                    while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                    {
                        if (msg.message == WM_INPUT)
                        {
                            hasReceivedScrollInput = true;
                            lastScrollInputReceived = DateTime.Now;
                            scrollInputCount++;
                            ProcessScrollWheelRawInput(msg.lParam);
                        }
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }

                    // Periodic heartbeat logging
                    var now = DateTime.Now;
                    if ((now - lastScrollHeartbeat).TotalMilliseconds >= HEARTBEAT_INTERVAL_MS)
                    {
                        var timeSinceLastInput = (now - lastScrollInputReceived).TotalSeconds;
                        bool windowValid = IsWindow(scrollWheelWindowHandle);
                        Logger.Info($"LegionButtonMonitor: Scroll thread heartbeat - inputCount={scrollInputCount}, lastInputAge={timeSinceLastInput:F1}s, windowValid={windowValid}, hasReceivedInput={hasReceivedScrollInput}");
                        lastScrollHeartbeat = now;

                        // Self-healing: Re-register Raw Input if we previously received input but stopped getting any
                        // This can help recover if Game Bar or another app disrupts Raw Input registration
                        if (hasReceivedScrollInput && timeSinceLastInput > (NO_INPUT_REREGISTER_MS / 1000.0) && windowValid)
                        {
                            Logger.Warn($"LegionButtonMonitor: No scroll input for {timeSinceLastInput:F1}s after previously receiving input, re-registering Raw Input");
                            if (InitializeScrollWheelRawInput(scrollWheelWindowHandle))
                            {
                                Logger.Info("LegionButtonMonitor: Raw Input re-registration successful");
                                lastScrollInputReceived = now; // Reset timer after re-registration
                            }
                            else
                            {
                                Logger.Error("LegionButtonMonitor: Raw Input re-registration failed");
                            }
                        }
                    }

                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LegionButtonMonitor: Scroll wheel thread exception: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                if (scrollWheelWindowHandle != IntPtr.Zero)
                {
                    DestroyWindow(scrollWheelWindowHandle);
                    scrollWheelWindowHandle = IntPtr.Zero;
                }
                Logger.Info("LegionButtonMonitor: Scroll wheel Raw Input thread exiting");
            }
        }

        /// <summary>
        /// Process a Raw Input message for scroll wheel events.
        /// Filters for Legion Go device (VID 17EF, PID 61EB) and mi_01/col02 interface.
        /// </summary>
        private void ProcessScrollWheelRawInput(IntPtr hRawInput)
        {
            try
            {
                // Get size of raw input data
                uint size = 0;
                uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
                GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
                if (size == 0) return;

                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize) == size)
                    {
                        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);

                        // Filter: only process mouse input from Legion Go scroll wheel interface
                        string deviceName = GetRawInputDeviceName(header.hDevice);
                        if (deviceName == null) return;

                        string deviceLower = deviceName.ToLowerInvariant();

                        // Must be Legion Go device (VID 17EF, PID 61EB)
                        if (!deviceLower.Contains("vid_17ef") || !deviceLower.Contains("pid_61eb")) return;

                        // Must be scroll wheel interface: mi_01 and col02
                        if (!deviceLower.Contains("mi_01") || !deviceLower.Contains("col02")) return;

                        if (header.dwType == RIM_TYPEMOUSE)
                        {
                            var mouse = Marshal.PtrToStructure<RAWINPUT_MOUSE>(buffer);
                            ushort buttonFlags = mouse.mouse.usButtonFlags;
                            ushort buttonData = mouse.mouse.usButtonData;
                            uint rawButtons = mouse.mouse.ulRawButtons;

                            // Log ALL raw values to diagnose Legion Go 2 scroll wheel
                            // This helps identify what values LG2 sends (may differ from LG1)
                            if (buttonFlags != 0 || buttonData != 0 || rawButtons != 0)
                            {
                                Logger.Info($"LegionButtonMonitor: Scroll Raw Input - buttonFlags=0x{buttonFlags:X4}, buttonData=0x{buttonData:X4}, rawButtons=0x{rawButtons:X8}");
                            }

                            // Legion Go 1 sends scroll click in usButtonData (non-standard)
                            // 0x0010 = click pressed, 0x0020 = click released, 0x0400 = wheel scroll
                            // Legion Go 2 may use different values - check buttonFlags and rawButtons too

                            // First try buttonData (LG1 style)
                            switch (buttonData)
                            {
                                case 0x0010: // Scroll click pressed
                                    if (scrollClickEnabled && !lastScrollClickState)
                                    {
                                        // Apply cooldown for XboxGuide action to ensure Game Bar has time to process
                                        var timeSinceLastAction = (DateTime.Now - lastScrollClickActionTime).TotalMilliseconds;
                                        if (scrollClickActionType == LegionButtonAction.XboxGuide &&
                                            timeSinceLastAction < SCROLL_CLICK_COOLDOWN_MS)
                                        {
                                            Logger.Debug($"LegionButtonMonitor: Scroll Click PRESSED skipped (cooldown: {timeSinceLastAction:F0}ms < {SCROLL_CLICK_COOLDOWN_MS}ms)");
                                            // Don't set lastScrollClickState - ignore this press entirely
                                            break;
                                        }

                                        lastScrollClickState = true;
                                        lastScrollClickActionTime = DateTime.Now;
                                        scrollClickPressTime = DateTime.Now; // Track press time for minimum hold
                                        Logger.Info("LegionButtonMonitor: Scroll Click PRESSED (Raw Input)");
                                        ProcessButtonAction("Scroll Click", true, scrollClickActionType,
                                            scrollClickShortcutKeys, scrollClickCommandPath);
                                    }
                                    break;

                                case 0x0020: // Scroll click released
                                    if (scrollClickEnabled && lastScrollClickState)
                                    {
                                        lastScrollClickState = false;

                                        // Enforce minimum hold time for Xbox Guide action
                                        // Game Bar needs the button held for a minimum duration to register properly
                                        if (scrollClickActionType == LegionButtonAction.XboxGuide)
                                        {
                                            var holdDuration = (DateTime.Now - scrollClickPressTime).TotalMilliseconds;
                                            if (holdDuration < SCROLL_CLICK_MIN_HOLD_MS)
                                            {
                                                int waitTime = SCROLL_CLICK_MIN_HOLD_MS - (int)holdDuration;
                                                Logger.Debug($"LegionButtonMonitor: Scroll Click hold too short ({holdDuration:F0}ms), waiting {waitTime}ms before release");
                                                Thread.Sleep(waitTime);
                                            }
                                        }

                                        Logger.Info("LegionButtonMonitor: Scroll Click RELEASED (Raw Input)");
                                        ProcessButtonAction("Scroll Click", false, scrollClickActionType,
                                            scrollClickShortcutKeys, scrollClickCommandPath);
                                    }
                                    break;

                                case 0x0400: // Scroll wheel movement
                                    // Note: Raw Input API for Legion Go mi_01/col02 doesn't provide scroll direction
                                    // We can only detect that a scroll event occurred, not up vs down
                                    // Use unified scroll action for any scroll event
                                    if (scrollEnabled)
                                    {
                                        Logger.Debug("LegionButtonMonitor: Scroll Wheel (Raw Input)");
                                        ProcessScrollAction("Scroll", scrollActionType, scrollShortcutKeys, scrollCommandPath);
                                    }
                                    break;
                            }

                            // Also check buttonFlags for standard mouse wheel events (Legion Go 2 may use this)
                            // RI_MOUSE_WHEEL = 0x0400
                            const ushort RI_MOUSE_WHEEL = 0x0400;
                            const ushort RI_MOUSE_HWHEEL = 0x0800;
                            const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
                            const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;

                            if ((buttonFlags & RI_MOUSE_WHEEL) != 0 || (buttonFlags & RI_MOUSE_HWHEEL) != 0)
                            {
                                // Standard mouse wheel event via buttonFlags
                                short wheelDelta = (short)buttonData; // Signed wheel delta
                                Logger.Info($"LegionButtonMonitor: Scroll Wheel via buttonFlags - delta={wheelDelta}");

                                if (wheelDelta > 0 && scrollUpEnabled)
                                {
                                    ProcessScrollAction("Scroll Up", scrollUpActionType, scrollUpShortcutKeys, scrollUpCommandPath);
                                }
                                else if (wheelDelta < 0 && scrollDownEnabled)
                                {
                                    ProcessScrollAction("Scroll Down", scrollDownActionType, scrollDownShortcutKeys, scrollDownCommandPath);
                                }
                                else if (scrollEnabled)
                                {
                                    ProcessScrollAction("Scroll", scrollActionType, scrollShortcutKeys, scrollCommandPath);
                                }
                            }

                            if ((buttonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
                            {
                                if (scrollClickEnabled && !lastScrollClickState)
                                {
                                    lastScrollClickState = true;
                                    lastScrollClickActionTime = DateTime.Now;
                                    scrollClickPressTime = DateTime.Now;
                                    Logger.Info("LegionButtonMonitor: Scroll Click PRESSED via buttonFlags");
                                    ProcessButtonAction("Scroll Click", true, scrollClickActionType,
                                        scrollClickShortcutKeys, scrollClickCommandPath);
                                }
                            }

                            if ((buttonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
                            {
                                if (scrollClickEnabled && lastScrollClickState)
                                {
                                    lastScrollClickState = false;
                                    Logger.Info("LegionButtonMonitor: Scroll Click RELEASED via buttonFlags");
                                    ProcessButtonAction("Scroll Click", false, scrollClickActionType,
                                        scrollClickShortcutKeys, scrollClickCommandPath);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LegionButtonMonitor: ProcessScrollWheelRawInput exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends the initialization command to switch the controller to initialized mode.
        /// Legion Space sends this command: 05:00:01:04:00:00... (64 bytes)
        /// After initialization, the controller switches from 04:3C:74 to 04:00:A1 format.
        /// </summary>
        private bool InitializeController(SafeFileHandle handle)
        {
            try
            {
                // Initialization command from Legion Space: 05:00:01:04:00:00...
                // Byte 0 is the report ID (0x05)
                byte[] initCommand = new byte[64];
                initCommand[0] = 0x05;  // Report ID
                initCommand[1] = 0x00;
                initCommand[2] = 0x01;
                initCommand[3] = 0x04;
                // Rest are already zeros

                // Log the actual bytes we're sending (Debug level to reduce log spam)
                Logger.Debug($"LegionButtonMonitor: Sending init command: {initCommand[0]:X2}:{initCommand[1]:X2}:{initCommand[2]:X2}:{initCommand[3]:X2}:{initCommand[4]:X2}:{initCommand[5]:X2}");

                // Use HidD_SetOutputReport for HID output reports (more reliable than WriteFile)
                // Mark the time so we can skip button detection for a short period after
                _lastOutputReportTime = DateTime.Now;

                bool result = HidD_SetOutputReport(handle, initCommand, (uint)initCommand.Length);
                int error = Marshal.GetLastWin32Error();

                if (result)
                {
                    Logger.Debug("LegionButtonMonitor: Init command sent successfully via HidD_SetOutputReport");
                    return true;
                }
                else
                {
                    Logger.Warn($"LegionButtonMonitor: Failed to send init command (error={error})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LegionButtonMonitor: Exception sending initialization command: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Probes a HID device to verify it sends the correct Legion format reports.
        /// If the controller is uninitialized (04:3C:74), sends initialization command.
        /// After initialization, controller should send 04:00:A1 format.
        /// </summary>
        /// <param name="handle">The HID device handle</param>
        /// <param name="hasWriteAccess">Whether the handle has write access for initialization</param>
        private bool ProbeDeviceFormat(SafeFileHandle handle, bool hasWriteAccess)
        {
            const uint READ_TIMEOUT_MS = 200; // Reduced timeout per read attempt (was 500ms)
            IntPtr eventHandle = IntPtr.Zero;
            bool initializationAttempted = false;

            try
            {
                // Create an event for overlapped I/O
                eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
                if (eventHandle == IntPtr.Zero)
                {
                    Logger.Debug("LegionButtonMonitor: Failed to create event for probe");
                    return false;
                }

                byte[] buffer = new byte[64];
                int wrongFormatCount = 0;

                // Reduced from 10 to 5 attempts (still enough for initialization)
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    // Reset event before each overlapped operation
                    ResetEvent(eventHandle);
                    var overlapped = new NativeOverlapped { EventHandle = eventHandle };

                    bool readResult = ReadFile(handle, buffer, (uint)buffer.Length, out uint bytesRead, ref overlapped);
                    int lastError = Marshal.GetLastWin32Error();

                    if (!readResult && lastError == ERROR_IO_PENDING)
                    {
                        // Wait for read to complete with timeout
                        uint waitResult = WaitForSingleObject(eventHandle, READ_TIMEOUT_MS);

                        if (waitResult == WAIT_TIMEOUT)
                        {
                            // Timeout - cancel the pending I/O and try next attempt
                            CancelIo(handle);
                            Logger.Debug($"LegionButtonMonitor: Probe attempt {attempt + 1} timed out");
                            continue;
                        }
                        else if (waitResult == WAIT_OBJECT_0)
                        {
                            // Read completed - get the result
                            if (!GetOverlappedResult(handle, ref overlapped, out bytesRead, false))
                            {
                                Logger.Debug($"LegionButtonMonitor: Probe attempt {attempt + 1} GetOverlappedResult failed");
                                continue;
                            }
                            readResult = true;
                        }
                        else
                        {
                            Logger.Debug($"LegionButtonMonitor: Probe attempt {attempt + 1} WaitForSingleObject failed: {waitResult}");
                            continue;
                        }
                    }

                    if (readResult || bytesRead > 0)
                    {
                        // Must be exactly 64 bytes with Legion format starting with 04
                        if (bytesRead == 64 && buffer[0] == 0x04)
                        {
                            // Check if initialized (04:00:A1) or uninitialized (04:3C:74)
                            bool isInitialized = buffer[1] == 0x00 && buffer[2] == 0xA1;
                            bool isUninitialized = buffer[1] == 0x3C && buffer[2] == 0x74;

                            if (isInitialized)
                            {
                                // Controller is in initialized mode - use standard format
                                isDetachedMode = false;
                                currentButtonByte = BUTTON_BYTE_ATTACHED;
                                Logger.Info($"LegionButtonMonitor: Probe success (initialized mode) - {bytesRead} bytes, header: 04:00:A1, btn byte: {currentButtonByte}");
                                return true;
                            }
                            else if (isUninitialized)
                            {
                                if (!initializationAttempted && hasWriteAccess)
                                {
                                    // Controller is uninitialized - send initialization command
                                    Logger.Info("LegionButtonMonitor: Controller is uninitialized (04:3C:74), sending init command...");
                                    initializationAttempted = true;
                                    bool initResult = InitializeController(handle);
                                    Logger.Info($"LegionButtonMonitor: Init command result: {initResult}");
                                    if (initResult)
                                    {
                                        Thread.Sleep(100); // Give controller time to switch modes
                                    }
                                    // Continue reading to check if initialization worked
                                    continue;
                                }
                                else if (!hasWriteAccess)
                                {
                                    // No write access - use fallback mode for uninitialized controller
                                    Logger.Warn("LegionButtonMonitor: Controller is uninitialized but no write access - using fallback mode");
                                    isDetachedMode = true;
                                    currentButtonByte = BUTTON_BYTE_DETACHED;
                                    Logger.Info($"LegionButtonMonitor: Probe success (fallback/uninitialized mode) - {bytesRead} bytes, header: 04:3C:74, btn byte: {currentButtonByte}");
                                    return true;
                                }
                                else
                                {
                                    // Initialization was attempted but controller is still in uninitialized mode
                                    // Don't use fallback - the format is different and would cause issues
                                    Logger.Error("LegionButtonMonitor: Controller failed to initialize, cannot use uninitialized format");
                                    return false;
                                }
                            }
                        }

                        wrongFormatCount++;
                        Logger.Debug($"LegionButtonMonitor: Probe attempt {attempt + 1} - got {bytesRead} bytes, header: {buffer[0]:X2}:{buffer[1]:X2}:{buffer[2]:X2}");

                        // Reduced from 3 to 2 - fail faster on wrong devices
                        if (wrongFormatCount >= 2)
                        {
                            Logger.Debug("LegionButtonMonitor: Probe failed - consistently wrong format");
                            return false;
                        }
                    }
                }

                Logger.Debug("LegionButtonMonitor: Probe failed - could not read correct format reports");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Debug($"LegionButtonMonitor: Probe exception: {ex.Message}");
                return false;
            }
            finally
            {
                if (eventHandle != IntPtr.Zero)
                {
                    CloseHandle(eventHandle);
                }
            }
        }

        /// <summary>
        /// Attempts to reconnect to the Legion controller after disconnection.
        /// Also ensures ViGEm controller is created if Xbox Guide action is configured.
        /// </summary>
        private bool TryReconnect()
        {
            try
            {
                // Ensure old handle is closed
                if (hidHandle != null && !hidHandle.IsInvalid)
                {
                    hidHandle.Close();
                    hidHandle = null;
                }

                // Try to find and open the controller again
                if (!OpenLegionController())
                {
                    return false;
                }

                // Reset button states on reconnect
                lastLegionLState = false;
                lastLegionRState = false;

                // Create ViGEm controller if needed but not yet created
                // This handles the case where monitor was started for battery only,
                // then button config was added later with Xbox Guide action
                if (NeedsViGEm && vigemController == null)
                {
                    Logger.Info("LegionButtonMonitor: Creating ViGEmController on reconnect (Xbox Guide action configured)");
                    vigemController = new ViGEmController();
                    ownsViGEmController = true;
                    if (vigemController.Connect())
                    {
                        if (vigemController.PlugIn())
                        {
                            Logger.Info("LegionButtonMonitor: ViGEmController created and plugged in successfully");
                        }
                        else
                        {
                            Logger.Warn("LegionButtonMonitor: Failed to plug in ViGEmController on reconnect");
                            vigemController.Dispose();
                            vigemController = null;
                            ownsViGEmController = false;
                        }
                    }
                    else
                    {
                        Logger.Warn("LegionButtonMonitor: Failed to connect ViGEmController on reconnect");
                        vigemController = null;
                        ownsViGEmController = false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug($"LegionButtonMonitor: Reconnect exception: {ex.Message}");
                return false;
            }
        }

        private void MonitorLoop()
        {
            Logger.Info("LegionButtonMonitor: Monitor thread started (unified L+R)");
            try
            {
                MonitorLoopInternal();
                Logger.Info("LegionButtonMonitor: Monitor thread exited normally");
            }
            catch (AccessViolationException ex)
            {
                Logger.Error($"LegionButtonMonitor: FATAL ACCESS VIOLATION: {ex.Message}\n{ex.StackTrace}");
            }
            catch (SEHException ex)
            {
                Logger.Error($"LegionButtonMonitor: FATAL SEH EXCEPTION: {ex.Message}\n{ex.StackTrace}");
            }
            catch (Exception ex)
            {
                Logger.Error($"LegionButtonMonitor: FATAL - Monitor loop crashed: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Logger.Info($"LegionButtonMonitor: Monitor thread ending, isRunning={isRunning}");
            }
        }

        private void MonitorLoopInternal()
        {
            byte[] buffer = new byte[64];
            // Pin the buffer for the lifetime of the monitor loop to prevent GC from moving it
            // This is critical for overlapped I/O operations
            GCHandle bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            int consecutiveFailures = 0;
            const int MAX_FAILURES_BEFORE_RECONNECT = 3;
            int reconnectDelayMs = 1000;
            const int MAX_RECONNECT_DELAY_MS = 10000;
            const uint READ_TIMEOUT_MS = 100; // Short timeout so we can check isRunning frequently

            // Controller heartbeat - send init command every 3 seconds to keep controller in initialized mode
            // Legion Space uses 5 second timeout, so 3 seconds gives us margin
            const int HEARTBEAT_INTERVAL_MS = 3000;
            DateTime lastHeartbeat = DateTime.Now;

            IntPtr eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
            if (eventHandle == IntPtr.Zero)
            {
                Logger.Error("LegionButtonMonitor: Failed to create event for monitor loop");
                return;
            }

            try
            {
                int loopIteration = 0;
                while (isRunning)
                {
                    loopIteration++;
                    try
                    {
                        // If no valid handle, try to reconnect
                        if (hidHandle == null || hidHandle.IsInvalid)
                        {
                            if (TryReconnect())
                            {
                                consecutiveFailures = 0;
                                reconnectDelayMs = 1000; // Reset delay on success
                                lastHeartbeat = DateTime.Now; // Reset heartbeat after reconnect
                                Logger.Info("LegionButtonMonitor: Reconnected successfully");
                            }
                            else
                            {
                                // Exponential backoff for reconnect attempts
                                Thread.Sleep(reconnectDelayMs);
                                reconnectDelayMs = Math.Min(reconnectDelayMs * 2, MAX_RECONNECT_DELAY_MS);
                            }
                            continue;
                        }

                        // Send heartbeat to keep controller in initialized mode
                        // Legion Space times out after 5 seconds, so we send every 3 seconds
                        // Only send heartbeats if:
                        // 1. We have write access
                        // 2. Controller was successfully initialized (not in fallback/detached mode)
                        // If we're in fallback mode (isDetachedMode=true), heartbeat could cause mode switch
                        // which would break our parsing since we're using wrong byte offsets
                        if (_hasWriteAccess && !isDetachedMode && (DateTime.Now - lastHeartbeat).TotalMilliseconds >= HEARTBEAT_INTERVAL_MS)
                        {
                            if (InitializeController(hidHandle))
                            {
                                lastHeartbeat = DateTime.Now;
                            }
                        }

                        // Read HID report with overlapped I/O and timeout
                        // Reset the event before starting a new overlapped operation
                        ResetEvent(eventHandle);
                        var overlapped = new NativeOverlapped { EventHandle = eventHandle };
                        bool readResult = ReadFile(hidHandle, buffer, (uint)buffer.Length, out uint bytesRead, ref overlapped);
                        int lastError = Marshal.GetLastWin32Error();

                        if (!readResult && lastError == ERROR_IO_PENDING)
                        {
                            // Wait for read to complete with timeout
                            uint waitResult = WaitForSingleObject(eventHandle, READ_TIMEOUT_MS);

                            if (waitResult == WAIT_TIMEOUT)
                            {
                                // Timeout - cancel and check if we should continue
                                CancelIo(hidHandle);
                                continue;
                            }
                            else if (waitResult == WAIT_OBJECT_0)
                            {
                                // Read completed - get the result
                                if (GetOverlappedResult(hidHandle, ref overlapped, out bytesRead, false))
                                {
                                    readResult = true;
                                }
                                else
                                {
                                    consecutiveFailures++;
                                    continue;
                                }
                            }
                            else
                            {
                                consecutiveFailures++;
                                continue;
                            }
                        }
                        else if (!readResult)
                        {
                            // Read failed immediately
                            consecutiveFailures++;

                            if (consecutiveFailures >= MAX_FAILURES_BEFORE_RECONNECT)
                            {
                                Logger.Warn($"LegionButtonMonitor: {consecutiveFailures} consecutive read failures (error {lastError}), attempting reconnect...");

                                // Close invalid handle and trigger reconnect
                                if (hidHandle != null && !hidHandle.IsInvalid)
                                {
                                    hidHandle.Close();
                                }
                                hidHandle = null;
                                consecutiveFailures = 0;
                            }
                            continue;
                        }

                        if (readResult && bytesRead >= 1)
                        {
                            consecutiveFailures = 0; // Reset on successful read

                            // Note: Scroll wheel reports (0x07) are now handled by the dedicated ScrollWheelThreadProc
                            // which reads from the separate mi_01 HID interface. This ensures only Legion Go
                            // scroll wheel events are captured, not events from other mice.

                            // Validate report header before parsing battery or buttons
                            // Only process reports with valid Legion controller headers:
                            // - Attached/initialized mode: 04:00:A1 (battery at bytes 3-6, buttons at byte 16)
                            // - Detached/uninitialized mode: 04:3C:74 (battery at bytes 5-8, buttons at byte 18)
                            // Other reports like 04:06:xx (brightness responses) should be ignored
                            bool hasValidReportHeader = false;
                            if (bytesRead >= currentButtonByte + 1 && bytesRead >= 14 && buffer[0] == 0x04)
                            {
                                if (!isDetachedMode && buffer[1] == 0x00 && buffer[2] == 0xA1)
                                {
                                    hasValidReportHeader = true;  // Attached mode: 04:00:A1
                                }
                                else if (isDetachedMode && buffer[1] == 0x3C && buffer[2] == 0x74)
                                {
                                    hasValidReportHeader = true;  // Detached mode: 04:3C:74
                                }
                            }

                            // Parse battery data from valid reports
                            try
                            {
                                if (hasValidReportHeader)
                                {
                                    // With heartbeat always active, we use 04:00:A1 header format
                                    // Battery at bytes 3-6, connection status at bytes 10-11
                                    int batteryOffset = 3;
                                    int connOffset = 10;

                                    // Connection status: 0x01=Off, 0x02=Attached, 0x03=Detached
                                    // Only 0x02 means the controller is actually connected
                                    bool leftConnected = buffer[connOffset] == 0x02;
                                    bool rightConnected = buffer[connOffset + 1] == 0x02;

                                    // Battery value (1-100), or -1 if not connected
                                    int leftBattery = leftConnected ? buffer[batteryOffset] : -1;
                                    int rightBattery = rightConnected ? buffer[batteryOffset + 2] : -1;

                                    // Charging status byte values (need to verify which means charging)
                                    byte leftChargingByte = buffer[batteryOffset + 1];
                                    byte rightChargingByte = buffer[batteryOffset + 3];

                                    // Log raw values when they change to help debug
                                    if (leftChargingByte != _lastLeftChargingByte || rightChargingByte != _lastRightChargingByte)
                                    {
                                        Logger.Info($"LegionButtonMonitor: Charging bytes L=0x{leftChargingByte:X2} R=0x{rightChargingByte:X2}");
                                        _lastLeftChargingByte = leftChargingByte;
                                        _lastRightChargingByte = rightChargingByte;
                                    }

                                    // Charging status: 0x02 = charging (USB power), 0x01 = discharging (battery)
                                    bool leftCharging = leftConnected && leftChargingByte == 0x02;
                                    bool rightCharging = rightConnected && rightChargingByte == 0x02;

                                    // Fire event if values changed AND throttle time has passed
                                    bool valuesChanged = leftBattery != _lastLeftBattery || rightBattery != _lastRightBattery ||
                                        leftCharging != _lastLeftCharging || rightCharging != _lastRightCharging ||
                                        leftConnected != _lastLeftConnected || rightConnected != _lastRightConnected;
                                    bool throttleExpired = (DateTime.Now - _lastBatteryUpdateTime).TotalMilliseconds >= BATTERY_UPDATE_THROTTLE_MS;

                                    if (valuesChanged && throttleExpired)
                                    {
                                        _lastLeftBattery = leftBattery;
                                        _lastRightBattery = rightBattery;
                                        _lastLeftCharging = leftCharging;
                                        _lastRightCharging = rightCharging;
                                        _lastLeftConnected = leftConnected;
                                        _lastRightConnected = rightConnected;
                                        _lastBatteryUpdateTime = DateTime.Now;

                                        Logger.Debug($"LegionButtonMonitor: Battery update L={leftBattery}% (conn={leftConnected}) R={rightBattery}% (conn={rightConnected})");
                                        try
                                        {
                                            BatteryUpdated?.Invoke(this, new LegionButtonBatteryEventArgs(
                                                leftBattery, leftCharging, leftConnected,
                                                rightBattery, rightCharging, rightConnected));
                                        }
                                        catch (Exception eventEx)
                                        {
                                            Logger.Error($"LegionButtonMonitor: BatteryUpdated event handler exception: {eventEx.Message}");
                                        }
                                    }
                                }
                            }
                            catch (Exception batteryEx)
                            {
                                Logger.Error($"LegionButtonMonitor: Battery parsing exception: {batteryEx.Message}");
                            }

                            // Check button states only from valid reports (04:00:A1 or 04:3C:74)
                            // This prevents false triggers from response reports like 04:06:xx (brightness)
                            if (hasValidReportHeader)
                            {
                                byte currentBtnValue = buffer[currentButtonByte];

                                // Process Legion L button if configured
                                if (legionLEnabled)
                                {
                                    bool legionLPressed = (currentBtnValue & LEGION_L_BIT) != 0;
                                    if (legionLPressed != lastLegionLState)
                                    {
                                        lastLegionLState = legionLPressed;
                                        try
                                        {
                                            ProcessButtonAction("Legion L", legionLPressed, legionLActionType,
                                                legionLShortcutKeys, legionLCommandPath);
                                        }
                                        catch (Exception btnEx)
                                        {
                                            Logger.Error($"LegionButtonMonitor: Legion L button action exception: {btnEx.Message}\n{btnEx.StackTrace}");
                                        }
                                    }
                                }

                                // Process Legion R button if configured
                                if (legionREnabled)
                                {
                                    bool legionRPressed = (currentBtnValue & LEGION_R_BIT) != 0;
                                    if (legionRPressed != lastLegionRState)
                                    {
                                        lastLegionRState = legionRPressed;
                                        try
                                        {
                                            ProcessButtonAction("Legion R", legionRPressed, legionRActionType,
                                                legionRShortcutKeys, legionRCommandPath);
                                        }
                                        catch (Exception btnEx)
                                        {
                                            Logger.Error($"LegionButtonMonitor: Legion R button action exception: {btnEx.Message}\n{btnEx.StackTrace}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"LegionButtonMonitor: Exception in monitor loop (iteration {loopIteration}): {ex.Message}\n{ex.StackTrace}");
                        consecutiveFailures++;
                        Thread.Sleep(500);
                    }

                    // Log every 500 iterations to verify loop is running (Debug level to reduce log spam)
                    if (loopIteration % 500 == 0)
                    {
                        Logger.Debug($"LegionButtonMonitor: Monitor loop alive, iteration {loopIteration}");
                    }
                }
            }
            catch (Exception fatalEx)
            {
                Logger.Error($"LegionButtonMonitor: FATAL exception in monitor loop: {fatalEx.Message}\n{fatalEx.StackTrace}");
            }
            finally
            {
                Logger.Info("LegionButtonMonitor: Monitor loop exiting, closing event handle and freeing buffer");
                CloseHandle(eventHandle);
                if (bufferHandle.IsAllocated)
                {
                    bufferHandle.Free();
                }
            }
        }

        /// <summary>
        /// Process button press/release and execute the configured action.
        /// </summary>
        private void ProcessButtonAction(string buttonName, bool pressed, LegionButtonAction actionType,
            string shortcutKeys, string commandPath)
        {
            Logger.Info($"LegionButtonMonitor: {buttonName} {(pressed ? "PRESSED" : "RELEASED")} - action={actionType}");

            try
            {
                onButtonStateChanged?.Invoke(pressed);
            }
            catch (Exception ex)
            {
                Logger.Error($"LegionButtonMonitor: onButtonStateChanged exception: {ex.Message}");
            }

            if (pressed)
            {
                // Button pressed - perform the configured action
                switch (actionType)
                {
                    case LegionButtonAction.XboxGuide:
                        if (vigemController != null)
                        {
                            Logger.Info($"LegionButtonMonitor: Calling SetGuide(true) for {buttonName}");
                            try
                            {
                                if (!vigemController.SetGuide(true))
                                {
                                    Logger.Warn($"LegionButtonMonitor: SetGuide(true) failed for {buttonName}");
                                }
                                else
                                {
                                    Logger.Info($"LegionButtonMonitor: SetGuide(true) succeeded for {buttonName}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"LegionButtonMonitor: SetGuide(true) exception for {buttonName}: {ex.Message}\n{ex.StackTrace}");
                            }
                        }
                        else
                        {
                            Logger.Warn($"LegionButtonMonitor: vigemController is null, cannot send Xbox Guide for {buttonName}");
                        }
                        break;

                    case LegionButtonAction.KeyboardShortcut:
                        if (!string.IsNullOrEmpty(shortcutKeys))
                        {
                            try
                            {
                                onShortcutTriggered?.Invoke(shortcutKeys);
                                Logger.Debug($"LegionButtonMonitor: {buttonName} pressed -> Shortcut '{shortcutKeys}' triggered");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"LegionButtonMonitor: Shortcut trigger exception: {ex.Message}");
                            }
                        }
                        break;

                    case LegionButtonAction.RunCommand:
                        if (!string.IsNullOrEmpty(commandPath))
                        {
                            try
                            {
                                onCommandTriggered?.Invoke(commandPath);
                                Logger.Debug($"LegionButtonMonitor: {buttonName} pressed -> Command '{commandPath}' triggered");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"LegionButtonMonitor: Command trigger exception: {ex.Message}");
                            }
                        }
                        break;

                    case LegionButtonAction.FocusGoTweaks:
                        try
                        {
                            onFocusGoTweaksTriggered?.Invoke();
                            Logger.Debug($"LegionButtonMonitor: {buttonName} pressed -> Focus GoTweaks triggered");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"LegionButtonMonitor: FocusGoTweaks trigger exception: {ex.Message}");
                        }
                        break;
                }
            }
            else
            {
                // Button released - only release Xbox Guide if that's the action
                if (actionType == LegionButtonAction.XboxGuide && vigemController != null)
                {
                    Logger.Info($"LegionButtonMonitor: Calling SetGuide(false) for {buttonName}");
                    try
                    {
                        vigemController.SetGuide(false);
                        Logger.Info($"LegionButtonMonitor: SetGuide(false) completed for {buttonName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"LegionButtonMonitor: SetGuide(false) exception for {buttonName}: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }

            Logger.Info($"LegionButtonMonitor: ProcessButtonAction completed for {buttonName}");
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            Stop();
            Logger.Info("LegionButtonMonitor: Disposed");
        }
    }

    /// <summary>
    /// Event arguments for battery status updates from LegionButtonMonitor.
    /// </summary>
    internal class LegionButtonBatteryEventArgs : EventArgs
    {
        public int LeftBattery { get; }
        public bool LeftCharging { get; }
        public bool LeftConnected { get; }
        public int RightBattery { get; }
        public bool RightCharging { get; }
        public bool RightConnected { get; }

        public LegionButtonBatteryEventArgs(int leftBattery, bool leftCharging, bool leftConnected,
                                            int rightBattery, bool rightCharging, bool rightConnected)
        {
            LeftBattery = leftBattery;
            LeftCharging = leftCharging;
            LeftConnected = leftConnected;
            RightBattery = rightBattery;
            RightCharging = rightCharging;
            RightConnected = rightConnected;
        }
    }
}
