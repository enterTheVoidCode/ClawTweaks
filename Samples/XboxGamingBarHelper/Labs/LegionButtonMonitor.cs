using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using NLog;

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
        }

        /// <summary>
        /// Get whether ViGEmBus is needed for the current configuration.
        /// </summary>
        public bool NeedsViGEm => (legionLEnabled && legionLActionType == LegionButtonAction.XboxGuide) ||
                                  (legionREnabled && legionRActionType == LegionButtonAction.XboxGuide);

        /// <summary>
        /// Get whether any button is configured.
        /// </summary>
        public bool HasAnyButtonConfigured => legionLEnabled || legionREnabled;

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

            // Check if we have any button configured
            if (!HasAnyButtonConfigured)
            {
                Logger.Warn("LegionButtonMonitor: No buttons configured, not starting");
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

            // Find and open Legion controller HID device
            if (!OpenLegionController())
            {
                Logger.Error("LegionButtonMonitor: Failed to open Legion controller");
                if (vigemController != null)
                {
                    vigemController.Dispose();
                    vigemController = null;
                }
                return false;
            }

            // Start monitoring thread
            isRunning = true;
            monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "LegionButtonMonitor"
            };
            monitorThread.Start();

            string buttons = "";
            if (legionLEnabled) buttons += "L";
            if (legionREnabled) buttons += (buttons.Length > 0 ? " + R" : "R");
            Logger.Info($"LegionButtonMonitor: Started monitoring Legion {buttons} button(s)");
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

            // Wait for thread to finish
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

            // Find and open Legion controller HID device
            if (!OpenLegionController())
            {
                Logger.Warn("LegionButtonMonitor: Failed to open Legion controller for battery monitoring");
                return false;
            }

            // Start monitoring thread (will only collect battery data since no buttons configured)
            isRunning = true;
            monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "LegionButtonMonitor"
            };
            monitorThread.Start();

            Logger.Info("LegionButtonMonitor: Started for battery monitoring only");
            return true;
        }

        private bool OpenLegionController()
        {
            try
            {
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
            const uint READ_TIMEOUT_MS = 500; // Timeout per read attempt
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

                // Try to read up to 10 reports to verify format (more attempts to allow for initialization)
                for (int attempt = 0; attempt < 10; attempt++)
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

                        // If we've seen 3+ wrong format reports, this is probably not the right device
                        if (wrongFormatCount >= 3)
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
        /// Also ensures ViGEm controller is still valid if Xbox Guide action is configured.
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

                        if (readResult && bytesRead >= currentButtonByte + 1)
                        {
                            consecutiveFailures = 0; // Reset on successful read

                            // Validate report header before parsing battery or buttons
                            // Only process reports with valid Legion controller headers:
                            // - Attached/initialized mode: 04:00:A1 (battery at bytes 3-6, buttons at byte 16)
                            // - Detached/uninitialized mode: 04:3C:74 (battery at bytes 5-8, buttons at byte 18)
                            // Other reports like 04:06:xx (brightness responses) should be ignored
                            bool hasValidReportHeader = false;
                            if (bytesRead >= 14 && buffer[0] == 0x04)
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
                                    int batteryOffset = isDetachedMode ? 5 : 3;
                                    int connOffset = isDetachedMode ? 12 : 10;

                                    // Check connection status: 0x01 = not connected
                                    bool leftConnected = buffer[connOffset] != 0x01;
                                    bool rightConnected = buffer[connOffset + 1] != 0x01;

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
