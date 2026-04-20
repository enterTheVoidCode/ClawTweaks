// =============================================================================
// LegionGoLibrary.cs
//
// A comprehensive library for controlling Lenovo Legion Go hardware features
// including WMI system controls and USB HID controller customization.
//
// Supports: Legion Go, Legion Go 2
// Author: LegionGoWMI Project
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using XboxGamingBarHelper.Labs;

namespace XboxGamingBarHelper.Devices.Libraries.Legion
{

    /// <summary>
    /// Service class for controlling Legion Go detachable controllers via USB HID.
    /// Provides RGB lighting, touchpad, gyro, and configuration controls.
    /// Supports Legion Go and Legion Go 2 (same command format).
    /// </summary>
    public class LegionControllerService : IDisposable
    {
        // Lenovo Legion Go USB identifiers
        private const int VENDOR_ID = 0x17EF;

        // Original Legion Go PIDs
        private static readonly int[] ORIGINAL_PIDS = { 0x6182, 0x6183, 0x6184, 0x6185 };

        // Legion Go 2 / 2025 Firmware PIDs (uses same commands as original)
        private static readonly int[] GO2_PIDS = { 0x61EB, 0x61EC, 0x61ED, 0x61EE };

        // All supported PIDs
        private static readonly int[] ALL_PRODUCT_IDS = ORIGINAL_PIDS.Concat(GO2_PIDS).ToArray();

        // HID Usage for Legion Go controller
        private const ushort USAGE_PAGE = 0xFFA0;
        private const ushort USAGE = 0x0001;

        private IntPtr _deviceHandle = IntPtr.Zero;
        private bool _isConnected = false;
        private DeviceType _deviceType = DeviceType.Unknown;
        private int _connectedPid = 0;
        private string _devicePath = null;  // Stored for opening separate read handle

        // Battery monitoring
        private IntPtr _batteryReadHandle = IntPtr.Zero;  // Separate handle for reading input reports
        private Thread _batteryMonitorThread;
        private volatile bool _monitoringBattery;
        private int _leftControllerBattery = -1;
        private int _rightControllerBattery = -1;
        private bool _leftControllerCharging = false;
        private bool _rightControllerCharging = false;

        /// <summary>
        /// Event raised when controller battery status is updated.
        /// </summary>
        public event EventHandler<ControllerServiceBatteryEventArgs> BatteryUpdated;

        /// <summary>
        /// Gets the left controller battery percentage (1-100), or -1 if unavailable.
        /// </summary>
        public int LeftControllerBattery => _leftControllerBattery;

        /// <summary>
        /// Gets the right controller battery percentage (1-100), or -1 if unavailable.
        /// </summary>
        public int RightControllerBattery => _rightControllerBattery;

        /// <summary>
        /// Gets whether the left controller is charging.
        /// </summary>
        public bool LeftControllerCharging => _leftControllerCharging;

        /// <summary>
        /// Gets whether the right controller is charging.
        /// </summary>
        public bool RightControllerCharging => _rightControllerCharging;

        #region Native HID API

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(IntPtr hidDeviceObject, byte[] lpReportBuffer, uint reportBufferLength);

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

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x01;
        private const uint FILE_SHARE_WRITE = 0x02;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        #endregion

        /// <summary>
        /// Gets whether the controller is currently connected.
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Gets the detected device type (LegionGo, LegionGoSlim, or Unknown).
        /// </summary>
        public DeviceType DetectedDeviceType => _deviceType;

        /// <summary>
        /// Gets the connected device's Product ID.
        /// </summary>
        public int ConnectedProductId => _connectedPid;

        #region Connection Management

        /// <summary>
        /// Lists all Lenovo HID devices found on the system.
        /// Useful for debugging connection issues.
        /// </summary>
        /// <returns>Tuple containing success status, message, and list of device descriptions</returns>
        public (bool Success, string Message, List<string> Devices) ListLenovoHidDevices()
        {
            var devices = new List<string>();
            try
            {
                HidD_GetHidGuid(out Guid hidGuid);

                IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (deviceInfoSet == INVALID_HANDLE_VALUE)
                    return (false, "Failed to get device info set", devices);

                try
                {
                    SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                    deviceInterfaceData.cbSize = Marshal.SizeOf(deviceInterfaceData);

                    uint memberIndex = 0;
                    while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, memberIndex++, ref deviceInterfaceData))
                    {
                        SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

                        IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);

                            if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                            {
                                string devicePath = Marshal.PtrToStringAuto(detailDataBuffer + 4) ?? "";

                                // Check if path contains our VID
                                if (!devicePath.ToLower().Contains("vid_17ef"))
                                    continue;

                                string deviceInfo = "";

                                // Try multiple access modes
                                IntPtr handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                string accessMode = "RW";

                                if (handle == INVALID_HANDLE_VALUE)
                                {
                                    handle = CreateFile(devicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                    accessMode = "R";
                                }

                                if (handle == INVALID_HANDLE_VALUE)
                                {
                                    handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                    accessMode = "Q";
                                }

                                if (handle != INVALID_HANDLE_VALUE)
                                {
                                    HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                                    attributes.Size = Marshal.SizeOf(attributes);

                                    if (HidD_GetAttributes(handle, ref attributes))
                                    {
                                        deviceInfo = $"VID:0x{attributes.VendorID:X4} PID:0x{attributes.ProductID:X4} [{accessMode}]";

                                        if (HidD_GetPreparsedData(handle, out IntPtr preparsedData))
                                        {
                                            try
                                            {
                                                int status = HidP_GetCaps(preparsedData, out HIDP_CAPS caps);
                                                if (status == 0) // HIDP_STATUS_SUCCESS
                                                {
                                                    deviceInfo += $" UP:0x{caps.UsagePage:X4} U:0x{caps.Usage:X4}";
                                                    deviceInfo += $" Out:{caps.OutputReportByteLength}";

                                                    if (caps.UsagePage == USAGE_PAGE && caps.Usage == USAGE)
                                                    {
                                                        deviceInfo += " [MATCH]";
                                                    }
                                                }
                                                else
                                                {
                                                    deviceInfo += $" (caps:0x{status:X8})";
                                                }
                                            }
                                            finally
                                            {
                                                HidD_FreePreparsedData(preparsedData);
                                            }
                                        }
                                        else
                                        {
                                            int err = Marshal.GetLastWin32Error();
                                            deviceInfo += $" (preparsed err:{err})";
                                        }
                                    }
                                    else
                                    {
                                        deviceInfo = "Path contains VID_17EF but GetAttributes failed";
                                    }

                                    CloseHandle(handle);
                                }
                                else
                                {
                                    int err = Marshal.GetLastWin32Error();
                                    deviceInfo = $"VID_17EF device (open err:{err})";
                                }

                                if (!string.IsNullOrEmpty(deviceInfo))
                                    devices.Add(deviceInfo);
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailDataBuffer);
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }

                return (true, $"Found {devices.Count} Lenovo HID device(s)", devices);
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}", devices);
            }
        }

        /// <summary>
        /// Connects to the Legion Go controller.
        /// Automatically finds the correct HID interface with write access.
        /// </summary>
        /// <returns>Tuple containing success status and connection message</returns>
        public (bool Success, string Message) Connect()
        {
            try
            {
                HidD_GetHidGuid(out Guid hidGuid);

                IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (deviceInfoSet == INVALID_HANDLE_VALUE)
                    return (false, "Failed to get device info set");

                IntPtr bestHandle = IntPtr.Zero;
                string bestInfo = "";
                int bestScore = 0;
                string bestPath = null;

                try
                {
                    SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                    deviceInterfaceData.cbSize = Marshal.SizeOf(deviceInterfaceData);

                    uint memberIndex = 0;
                    while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, memberIndex++, ref deviceInterfaceData))
                    {
                        SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

                        IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);

                            if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                            {
                                string devicePath = Marshal.PtrToStringAuto(detailDataBuffer + 4) ?? "";

                                // Quick filter by path
                                if (!devicePath.ToLower().Contains("vid_17ef"))
                                    continue;

                                // Try to open with write access
                                IntPtr handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                                if (handle != INVALID_HANDLE_VALUE)
                                {
                                    HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                                    attributes.Size = Marshal.SizeOf(attributes);

                                    if (HidD_GetAttributes(handle, ref attributes))
                                    {
                                        if (attributes.VendorID == VENDOR_ID && ALL_PRODUCT_IDS.Contains(attributes.ProductID))
                                        {
                                            int score = 1; // Base score for matching VID/PID with RW access
                                            string info = $"PID: 0x{attributes.ProductID:X4}";

                                            if (HidD_GetPreparsedData(handle, out IntPtr preparsedData))
                                            {
                                                try
                                                {
                                                    if (HidP_GetCaps(preparsedData, out HIDP_CAPS caps) == 0)
                                                    {
                                                        info += $", UP: 0x{caps.UsagePage:X4}, U: 0x{caps.Usage:X4}, Out: {caps.OutputReportByteLength}";

                                                        // Exact match - highest priority
                                                        if (caps.UsagePage == USAGE_PAGE && caps.Usage == USAGE)
                                                        {
                                                            score = 100;
                                                        }
                                                        // Good output report size
                                                        else if (caps.OutputReportByteLength >= 64)
                                                        {
                                                            score = 10;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        // HidP_GetCaps failed - test if WriteFile works on this interface
                                                        // This is needed for Legion Go 2 which has multiple interfaces with same VID/PID
                                                        byte[] testBuffer = new byte[64];
                                                        testBuffer[0] = 0x05; // Report ID for Legion Go
                                                        if (WriteFile(handle, testBuffer, (uint)testBuffer.Length, out uint written, IntPtr.Zero) && written > 0)
                                                        {
                                                            score = 50; // WriteFile works - good candidate
                                                            info += " (WriteFile OK)";
                                                        }
                                                        else
                                                        {
                                                            score = 2; // WriteFile failed - skip this one
                                                            info += " (caps failed, WriteFile failed)";
                                                        }
                                                    }
                                                }
                                                finally
                                                {
                                                    HidD_FreePreparsedData(preparsedData);
                                                }
                                            }
                                            else
                                            {
                                                // Can't get preparsed data - test if WriteFile works
                                                byte[] testBuffer = new byte[64];
                                                testBuffer[0] = 0x05; // Report ID for Legion Go
                                                if (WriteFile(handle, testBuffer, (uint)testBuffer.Length, out uint written, IntPtr.Zero) && written > 0)
                                                {
                                                    score = 50; // WriteFile works - good candidate
                                                    info += " (no caps, WriteFile OK)";
                                                }
                                                else
                                                {
                                                    score = 2; // WriteFile failed - skip this one
                                                    info += " (no caps, WriteFile failed)";
                                                }
                                            }

                                            if (score > bestScore)
                                            {
                                                if (bestHandle != IntPtr.Zero)
                                                    CloseHandle(bestHandle);
                                                bestHandle = handle;
                                                bestPath = devicePath;
                                                bestInfo = info;
                                                bestScore = score;
                                                _connectedPid = attributes.ProductID;
                                                handle = IntPtr.Zero;
                                            }
                                        }
                                    }

                                    if (handle != IntPtr.Zero)
                                        CloseHandle(handle);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailDataBuffer);
                        }
                    }

                    if (bestHandle != IntPtr.Zero)
                    {
                        _deviceHandle = bestHandle;
                        _devicePath = bestPath;
                        _isConnected = true;

                        // Determine device type based on PID
                        // Legion Go 2 uses same commands as original Legion Go
                        if (ORIGINAL_PIDS.Contains(_connectedPid) || GO2_PIDS.Contains(_connectedPid))
                            _deviceType = DeviceType.LegionGo;
                        else
                            _deviceType = DeviceType.Unknown;

                        string mode = bestScore >= 100 ? "exact match" : bestScore >= 50 ? "WriteFile test" : bestScore >= 10 ? "by output size" : "basic";
                        string deviceName = GO2_PIDS.Contains(_connectedPid) ? "Legion Go 2" : "Legion Go";
                        return (true, $"Connected to {deviceName} ({mode}: {bestInfo})");
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }

                return (false, "Legion Go controller not found. Make sure controllers are attached and no other app is using them.");
            }
            catch (Exception ex)
            {
                return (false, $"Error connecting to controller: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnects from the controller and releases the HID handles.
        /// </summary>
        public void Disconnect()
        {
            // Stop battery monitoring first (this also closes the battery read handle)
            StopBatteryMonitoring();

            if (_deviceHandle != IntPtr.Zero && _deviceHandle != INVALID_HANDLE_VALUE)
            {
                CloseHandle(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
            _devicePath = null;
            _isConnected = false;
        }

        private (bool Success, string Message) SendCommand(byte[] command)
        {
            // If we lost the handle (e.g. HidHide cycle-port from VIIPER enabling, USB
            // re-enumeration, sleep/wake), try to reconnect once before giving up. Without
            // this the helper kept reporting "Controller not connected" indefinitely after
            // VIIPER suppression landed, even though the device was back on the bus.
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                if (!TryReopenAfterStaleHandle("pre-send"))
                {
                    return (false, "Controller not connected");
                }
            }

            var (sendOk, sendMsg) = SendCommandOnce(command);
            if (sendOk) return (sendOk, sendMsg);

            // Write failed: handle may have been invalidated by a cycle-port between the
            // last successful write and now. Reopen and retry once.
            if (TryReopenAfterStaleHandle("post-send"))
            {
                return SendCommandOnce(command);
            }
            return (sendOk, sendMsg);
        }

        private (bool Success, string Message) SendCommandOnce(byte[] command)
        {
            try
            {
                // Pad command to 64 bytes (HID report size)
                byte[] buffer = new byte[64];
                Array.Copy(command, buffer, Math.Min(command.Length, 64));

                // Notify LegionButtonMonitor that we're about to send an output report
                // This prevents false button triggers caused by HID report interference
                LegionButtonMonitor.NotifyOutputReportSent();

                // Try HidD_SetOutputReport first (standard method)
                if (HidD_SetOutputReport(_deviceHandle, buffer, (uint)buffer.Length))
                {
                    return (true, "Command sent successfully");
                }

                // Fallback to WriteFile if HidD_SetOutputReport fails
                // This is needed for Legion Go 2 where some interfaces don't support SetOutputReport
                if (WriteFile(_deviceHandle, buffer, (uint)buffer.Length, out uint written, IntPtr.Zero) && written > 0)
                {
                    return (true, "Command sent successfully (WriteFile)");
                }

                int error = Marshal.GetLastWin32Error();
                _isConnected = false; // Mark stale so next call triggers reconnect path
                return (false, $"Failed to send command (Error: {error})");
            }
            catch (Exception ex)
            {
                _isConnected = false;
                return (false, $"Error sending command: {ex.Message}");
            }
        }

        private bool TryReopenAfterStaleHandle(string reason)
        {
            // Close any stale handle silently before reconnecting
            if (_deviceHandle != IntPtr.Zero && _deviceHandle != INVALID_HANDLE_VALUE)
            {
                CloseHandle(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
            _isConnected = false;

            // No logging here — this class is logging-free by convention; callers (LegionManager)
            // log success/failure based on the returned tuple from SendCommand.
            var (ok, _) = Connect();
            return ok;
        }

        #endregion

        #region RGB/Stick Light Control

        /// <summary>
        /// Enables or disables RGB lighting on a controller.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="enabled">True to enable RGB, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetRgbEnabled(Controller controller, bool enabled)
        {
            // Legion Go / Legion Go 2: 05 06 70 02 [ctrl] [0/1] 01
            byte[] command = {
                0x05, 0x06, 0x70, 0x02,
                (byte)controller,
                (byte)(enabled ? 0x01 : 0x00),
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"RGB {(enabled ? "enabled" : "disabled")} for {controller} controller"
                : result.Message);
        }

        /// <summary>
        /// Sets the RGB profile with color, mode, brightness, and speed settings.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="mode">RGB animation mode</param>
        /// <param name="red">Red color component (0-255)</param>
        /// <param name="green">Green color component (0-255)</param>
        /// <param name="blue">Blue color component (0-255)</param>
        /// <param name="brightness">Brightness level (0.0 to 1.0)</param>
        /// <param name="speed">Animation speed (0.0 to 1.0, where 1.0 is fastest)</param>
        /// <param name="profile">Profile slot to save to (default 0x03)</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetRgbProfile(
            Controller controller,
            RgbMode mode,
            byte red, byte green, byte blue,
            float brightness = 1.0f,
            float speed = 0.5f,
            byte profile = 0x03)
        {
            // Legion Go / Legion Go 2
            byte r_brightness = (byte)Math.Max(0, Math.Min(63, (int)(64 * brightness)));
            byte r_speed = (byte)Math.Max(0, Math.Min(63, (int)(64 * (1 - speed))));

            byte[] command = {
                0x05, 0x0C, 0x72, 0x01,
                (byte)controller,
                (byte)mode,
                red, green, blue,
                r_brightness,
                r_speed,
                profile,
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"RGB profile set: {mode}, Color: RGB({red},{green},{blue})"
                : result.Message);
        }

        /// <summary>
        /// Loads (applies) a previously saved RGB profile.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="profile">Profile slot to load (default 0x03)</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) LoadRgbProfile(Controller controller, byte profile = 0x03)
        {
            // Legion Go / Legion Go 2
            byte[] command = {
                0x05, 0x06, 0x73, 0x02,
                (byte)controller,
                profile,
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"RGB profile {profile} loaded for {controller} controller"
                : result.Message);
        }

        /// <summary>
        /// Sets the stick light mode for both controllers at once.
        /// Convenience method that applies settings to both left and right controllers.
        /// </summary>
        /// <param name="mode">RGB animation mode</param>
        /// <param name="red">Red color component (0-255)</param>
        /// <param name="green">Green color component (0-255)</param>
        /// <param name="blue">Blue color component (0-255)</param>
        /// <param name="brightness">Brightness level (0.0 to 1.0)</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetStickLightMode(RgbMode mode, byte red, byte green, byte blue, float brightness = 1.0f, float speed = 0.5f)
        {
            // Legion Go / Legion Go 2 - set for both controllers
            var leftResult = SetRgbProfile(Controller.Left, mode, red, green, blue, brightness, speed);
            var rightResult = SetRgbProfile(Controller.Right, mode, red, green, blue, brightness, speed);

            LoadRgbProfile(Controller.Left);
            LoadRgbProfile(Controller.Right);

            if (leftResult.Success && rightResult.Success)
                return (true, $"Stick light mode set to {mode} with color RGB({red},{green},{blue})");

            return (false, $"Left: {leftResult.Message}, Right: {rightResult.Message}");
        }

        #endregion

        #region Touchpad Control

        /// <summary>
        /// Enables or disables the right controller touchpad.
        /// </summary>
        /// <param name="enabled">True to enable touchpad, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetTouchpadEnabled(bool enabled)
        {
            // Legion Go / Legion Go 2: 05 06 6B 02 04 [0/1] 01
            byte[] command = {
                0x05, 0x06, 0x6B, 0x02,
                0x04,  // Right controller
                (byte)(enabled ? 0x01 : 0x00),
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"Touchpad {(enabled ? "enabled" : "disabled")}"
                : result.Message);
        }

        /// <summary>
        /// Sets the touchpad haptic vibration intensity.
        /// </summary>
        /// <param name="level">Vibration intensity level</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetTouchpadVibration(TouchpadVibrationLevel level)
        {
            // Touchpad vibration mode: 05 00 06 06 00 [mode]
            // Off=0x01, Low=0x02, Medium=0x03, High=0x04
            byte[] command = {
                0x05, 0x00, 0x06, 0x06,
                0x00,
                (byte)level
            };

            var result = SendCommand(command);
            string levelName = level switch
            {
                TouchpadVibrationLevel.Off => "Off",
                TouchpadVibrationLevel.Low => "Low",
                TouchpadVibrationLevel.Medium => "Medium",
                TouchpadVibrationLevel.High => "High",
                _ => "Unknown"
            };

            return (result.Success, result.Success
                ? $"Touchpad vibration set to {levelName}"
                : result.Message);
        }

        #endregion

        #region Controller Vibration

        /// <summary>
        /// Sets the vibration/haptic feedback intensity for a controller.
        /// This controls the overall motor vibration strength.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="level">Vibration intensity level</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetControllerVibration(Controller controller, ControllerVibrationLevel level)
        {
            // Command: 05 06 67 02 [controller] [level] 01
            byte[] command = {
                0x05, 0x06, 0x67, 0x02,
                (byte)controller,
                (byte)level,
                0x01
            };

            var result = SendCommand(command);
            string levelName = level switch
            {
                ControllerVibrationLevel.Off => "Off",
                ControllerVibrationLevel.Weak => "Weak",
                ControllerVibrationLevel.Medium => "Medium",
                ControllerVibrationLevel.Strong => "Strong",
                _ => "Unknown"
            };

            return (result.Success, result.Success
                ? $"Vibration set to {levelName} for {controller} controller"
                : result.Message);
        }

        /// <summary>
        /// Sets vibration intensity for both controllers at once.
        /// </summary>
        /// <param name="level">Vibration intensity level</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetBothControllersVibration(ControllerVibrationLevel level)
        {
            var leftResult = SetControllerVibration(Controller.Left, level);
            var rightResult = SetControllerVibration(Controller.Right, level);

            if (leftResult.Success && rightResult.Success)
            {
                string levelName = level switch
                {
                    ControllerVibrationLevel.Off => "Off",
                    ControllerVibrationLevel.Weak => "Weak",
                    ControllerVibrationLevel.Medium => "Medium",
                    ControllerVibrationLevel.Strong => "Strong",
                    _ => "Unknown"
                };
                return (true, $"Vibration set to {levelName} for both controllers");
            }

            return (false, $"Left: {leftResult.Message}, Right: {rightResult.Message}");
        }

        #endregion

        #region Gyro Control

        /// <summary>
        /// Enables or disables the gyroscope on a controller.
        /// When enabled, also sets high-quality gyro mode.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="enabled">True to enable gyro, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetGyroEnabled(Controller controller, bool enabled)
        {
            // Legion Go / Legion Go 2
            if (enabled)
            {
                byte[] enableCmd = { 0x05, 0x06, 0x6A, 0x02, (byte)controller, 0x01, 0x01 };
                var result1 = SendCommand(enableCmd);

                byte[] hqCmd = { 0x05, 0x06, 0x6A, 0x07, (byte)controller, 0x02, 0x01 };
                var result2 = SendCommand(hqCmd);

                return (result1.Success && result2.Success,
                    $"Gyro enabled for {controller} controller");
            }
            else
            {
                byte[] disableCmd = { 0x05, 0x06, 0x6A, 0x07, (byte)controller, 0x01, 0x01 };
                var result = SendCommand(disableCmd);

                return (result.Success, $"Gyro disabled for {controller} controller");
            }
        }

        #endregion

        #region Controller Configuration

        /// <summary>
        /// Swaps the left and right controller button mappings.
        /// Useful for left-handed users.
        /// </summary>
        /// <param name="swapped">True to swap controllers, false for normal layout</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetControllerSwap(bool swapped)
        {
            // Same format for both device types
            byte[] command = {
                0x05, 0x06, 0x69, 0x04,
                0x01,
                (byte)(swapped ? 0x02 : 0x01),
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"Controller swap {(swapped ? "enabled" : "disabled")}"
                : result.Message);
        }

        /// <summary>
        /// Enables or disables the FPS button remapper mode.
        /// When enabled, provides optimized button layout for FPS games.
        /// </summary>
        /// <param name="enabled">True to enable FPS remapper, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetFpsRemapper(bool enabled)
        {
            // FPS remapper: 05 06 69 05 01 [01=on, 02=off] 01
            byte[] command = {
                0x05, 0x06, 0x69, 0x05,
                0x01,
                (byte)(enabled ? 0x01 : 0x02),
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"FPS remapper {(enabled ? "enabled" : "disabled")}"
                : result.Message);
        }

        #endregion

        #region Battery Monitoring

        /// <summary>
        /// Starts monitoring battery status from controller input reports.
        /// Battery reports are pushed by the controllers continuously.
        /// Report format: 04 00 a1 [leftBat] [leftStatus] [rightBat] [rightStatus]
        /// Uses a separate device handle to avoid blocking the main command handle.
        /// </summary>
        public void StartBatteryMonitoring()
        {
            if (_monitoringBattery)
                return;

            if (string.IsNullOrEmpty(_devicePath))
                return;

            // Open a separate handle for reading input reports
            // This avoids blocking the main handle used for sending commands
            _batteryReadHandle = CreateFile(_devicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (_batteryReadHandle == INVALID_HANDLE_VALUE)
            {
                _batteryReadHandle = IntPtr.Zero;
                return;
            }

            _monitoringBattery = true;
            _batteryMonitorThread = new Thread(ReadBatteryReports)
            {
                IsBackground = true,
                Name = "LegionControllerService-BatteryMonitor"
            };
            _batteryMonitorThread.Start();
        }

        /// <summary>
        /// Stops the battery monitoring thread and closes the read handle.
        /// </summary>
        public void StopBatteryMonitoring()
        {
            _monitoringBattery = false;

            // Close the battery read handle to unblock any pending ReadFile call
            if (_batteryReadHandle != IntPtr.Zero && _batteryReadHandle != INVALID_HANDLE_VALUE)
            {
                CloseHandle(_batteryReadHandle);
                _batteryReadHandle = IntPtr.Zero;
            }

            if (_batteryMonitorThread != null && _batteryMonitorThread.IsAlive)
            {
                _batteryMonitorThread.Join(500);
                _batteryMonitorThread = null;
            }
        }

        /// <summary>
        /// Background thread that reads battery status from input reports.
        /// Uses a separate handle (_batteryReadHandle) to avoid blocking main command handle.
        /// </summary>
        private void ReadBatteryReports()
        {
            byte[] buffer = new byte[64];
            int consecutiveFailures = 0;
            const int MAX_CONSECUTIVE_FAILURES = 5;

            while (_monitoringBattery && _isConnected && _batteryReadHandle != IntPtr.Zero)
            {
                try
                {
                    uint bytesRead;
                    if (ReadFile(_batteryReadHandle, buffer, (uint)buffer.Length, out bytesRead, IntPtr.Zero))
                    {
                        consecutiveFailures = 0; // Reset on successful read


                        // Check for battery report format
                        // Attached mode: 04:00:A1 - battery at bytes 3-6, connection at bytes 10-11
                        // Detached mode: 04:3C:74 - battery at bytes 5-8, connection at bytes 12-13
                        bool isAttachedMode = bytesRead >= 12 && buffer[0] == 0x04 && buffer[1] == 0x00 && buffer[2] == 0xA1;
                        bool isDetachedMode = bytesRead >= 14 && buffer[0] == 0x04 && buffer[1] == 0x3C && buffer[2] == 0x74;

                        if (isAttachedMode || isDetachedMode)
                        {
                            // Byte offsets differ between modes (detached has 2 extra bytes before battery data)
                            int batteryOffset = isDetachedMode ? 5 : 3;
                            int connOffset = isDetachedMode ? 12 : 10;

                            // Check connection status: 0x01 = not connected, 0x02 = BT/attached, 0x03 = USB/detached
                            bool leftConnected = buffer[connOffset] != 0x01;
                            bool rightConnected = buffer[connOffset + 1] != 0x01;

                            // Battery value (1-100), or -1 if not connected
                            int leftBattery = leftConnected ? buffer[batteryOffset] : -1;
                            int rightBattery = rightConnected ? buffer[batteryOffset + 2] : -1;

                            // Charging status: 0x04 = charging, 0x01 = discharging
                            bool leftCharging = leftConnected && buffer[batteryOffset + 1] == 0x04;
                            bool rightCharging = rightConnected && buffer[batteryOffset + 3] == 0x04;

                            bool changed = _leftControllerBattery != leftBattery ||
                                           _rightControllerBattery != rightBattery ||
                                           _leftControllerCharging != leftCharging ||
                                           _rightControllerCharging != rightCharging;

                            _leftControllerBattery = leftBattery;
                            _leftControllerCharging = leftCharging;
                            _rightControllerBattery = rightBattery;
                            _rightControllerCharging = rightCharging;

                            if (changed)
                            {
                                BatteryUpdated?.Invoke(this, new ControllerServiceBatteryEventArgs(
                                    leftBattery, leftCharging, rightBattery, rightCharging));
                            }
                        }
                    }
                    else
                    {
                        // Read failed - check if device disconnected
                        int error = Marshal.GetLastWin32Error();
                        consecutiveFailures++;

                        // ERROR_DEVICE_NOT_CONNECTED (1167) or ERROR_INVALID_HANDLE (6) = device disconnected or handle closed
                        if (error == 1167 || error == 6 || consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                        {
                            // Device disconnected - mark as disconnected (main handle will be closed by Disconnect())
                            _isConnected = false;
                            break;
                        }

                        Thread.Sleep(100);
                    }
                }
                catch
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                    {
                        _isConnected = false;
                        break;
                    }
                    Thread.Sleep(100);
                }
            }

            // Cleanup when monitoring stops (device disconnected or StopBatteryMonitoring called)
            _monitoringBattery = false;

            // Close handle if still valid (may already be closed by StopBatteryMonitoring)
            var handleToClose = _batteryReadHandle;
            _batteryReadHandle = IntPtr.Zero;
            if (handleToClose != IntPtr.Zero && handleToClose != INVALID_HANDLE_VALUE)
            {
                try { CloseHandle(handleToClose); } catch { }
            }

            // Reset values when monitoring stops
            _leftControllerBattery = -1;
            _rightControllerBattery = -1;
            _leftControllerCharging = false;
            _rightControllerCharging = false;
        }

        #endregion

        /// <summary>
        /// Disposes the service and disconnects from the controller.
        /// </summary>
        public void Dispose()
        {
            StopBatteryMonitoring();
            Disconnect();
        }
    }

    /// <summary>
    /// Event arguments for controller battery status updates from LegionControllerService.
    /// </summary>
    public class ControllerServiceBatteryEventArgs : EventArgs
    {
        public int LeftBattery { get; private set; }
        public bool LeftCharging { get; private set; }
        public int RightBattery { get; private set; }
        public bool RightCharging { get; private set; }

        public ControllerServiceBatteryEventArgs(int leftBattery, bool leftCharging, int rightBattery, bool rightCharging)
        {
            LeftBattery = leftBattery;
            LeftCharging = leftCharging;
            RightBattery = rightBattery;
            RightCharging = rightCharging;
        }
    }
}
