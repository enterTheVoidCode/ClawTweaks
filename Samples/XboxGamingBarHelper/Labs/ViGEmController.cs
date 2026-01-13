using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using NLog;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Virtual Xbox 360 controller using ViGEmBus driver.
    /// Used to send Xbox Guide button presses when Legion L is pressed.
    /// </summary>
    internal class ViGEmController : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ViGEmBus device interface GUID
        private static readonly Guid VIGEM_GUID = new Guid("96E42B22-F5E9-42F8-B043-ED0F932F014F");

        // ViGEmBus IOCTL codes (matching working MenuToGuide implementation)
        private const uint IOCTL_PLUGIN = 0x2AA004;
        private const uint IOCTL_UNPLUG = 0x2AA008;
        private const uint IOCTL_SUBMIT_REPORT = 0x2AA808;

        // Xbox 360 controller buttons
        private const ushort XINPUT_GAMEPAD_GUIDE = 0x0400;

        private SafeFileHandle vigemHandle;
        private uint serialNo = 0;  // Unique serial for this controller instance
        private bool isPluggedIn = false;
        private bool isDisposed = false;

        // SetupAPI constants
        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;

        // P/Invoke declarations
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
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

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

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        // ViGEmBus structures
        [StructLayout(LayoutKind.Sequential)]
        private struct VIGEM_PLUGIN_TARGET
        {
            public uint Size;
            public uint SerialNo;
            public uint TargetType; // 0 = Xbox 360
            public ushort VendorId;
            public ushort ProductId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VIGEM_UNPLUG_TARGET
        {
            public uint Size;
            public uint SerialNo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XUSB_SUBMIT_REPORT
        {
            public uint Size;
            public uint SerialNo;
            public ushort Buttons;       // Button flags (Guide = 0x0400)
            public byte LeftTrigger;
            public byte RightTrigger;
            public short LeftThumbX;
            public short LeftThumbY;
            public short RightThumbX;
            public short RightThumbY;
        }

        /// <summary>
        /// Connect to ViGEmBus device.
        /// </summary>
        public bool Connect()
        {
            try
            {
                // Find the ViGEmBus device interface path using SetupDi
                string devicePath = FindViGEmBusDevicePath();
                if (string.IsNullOrEmpty(devicePath))
                {
                    Logger.Error("ViGEmController: ViGEmBus device not found. Is ViGEmBus installed?");
                    return false;
                }

                Logger.Info($"ViGEmController: Found ViGEmBus at {devicePath}");

                // Open with GENERIC_READ | GENERIC_WRITE and FILE_ATTRIBUTE_NORMAL (matching working MenuToGuide)
                vigemHandle = CreateFile(
                    devicePath,
                    0xC0000000, // GENERIC_READ | GENERIC_WRITE
                    0x3,        // FILE_SHARE_READ | FILE_SHARE_WRITE
                    IntPtr.Zero,
                    3,          // OPEN_EXISTING
                    0x80,       // FILE_ATTRIBUTE_NORMAL
                    IntPtr.Zero);

                if (vigemHandle.IsInvalid)
                {
                    Logger.Error($"ViGEmController: Failed to open ViGEmBus device. Error: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                Logger.Info("ViGEmController: Connected to ViGEmBus");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Exception connecting to ViGEmBus: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Find the ViGEmBus device interface path using SetupDi.
        /// </summary>
        private string FindViGEmBusDevicePath()
        {
            Guid vigemGuid = VIGEM_GUID;

            IntPtr deviceInfoSet = SetupDiGetClassDevs(
                ref vigemGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            {
                Logger.Error($"ViGEmController: SetupDiGetClassDevs failed. Error: {Marshal.GetLastWin32Error()}");
                return null;
            }

            try
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                };

                // Get the first device interface
                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref vigemGuid, 0, ref interfaceData))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) // ERROR_NO_MORE_ITEMS
                    {
                        Logger.Error("ViGEmController: No ViGEmBus device interfaces found");
                    }
                    else
                    {
                        Logger.Error($"ViGEmController: SetupDiEnumDeviceInterfaces failed. Error: {error}");
                    }
                    return null;
                }

                // Get required buffer size
                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

                // Allocate buffer for device path
                IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    // Set cbSize for SP_DEVICE_INTERFACE_DETAIL_DATA
                    // On 64-bit: 8 bytes (4 for cbSize + 4 padding before DevicePath)
                    // On 32-bit: 6 bytes (4 for cbSize + 2 for first char alignment)
                    Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);

                    if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                    {
                        // Get device path (starts at offset 4)
                        string devicePath = Marshal.PtrToStringAuto(detailDataBuffer + 4);
                        return devicePath;
                    }
                    else
                    {
                        Logger.Error($"ViGEmController: SetupDiGetDeviceInterfaceDetail failed. Error: {Marshal.GetLastWin32Error()}");
                        return null;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailDataBuffer);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        /// <summary>
        /// Plug in a virtual Xbox 360 controller.
        /// </summary>
        public bool PlugIn()
        {
            if (vigemHandle == null || vigemHandle.IsInvalid)
            {
                Logger.Error("ViGEmController: Cannot plug in - not connected to ViGEmBus");
                return false;
            }

            if (isPluggedIn)
            {
                Logger.Warn("ViGEmController: Controller already plugged in");
                return true;
            }

            try
            {
                // Use unique serial based on tick count (matching working MenuToGuide implementation)
                // Only generate new serial if not already set (for reconnection scenarios)
                if (serialNo == 0)
                {
                    serialNo = (uint)Environment.TickCount;
                }

                int structSize = Marshal.SizeOf<VIGEM_PLUGIN_TARGET>();

                var pluginTarget = new VIGEM_PLUGIN_TARGET
                {
                    Size = (uint)structSize,
                    SerialNo = serialNo,
                    TargetType = 0, // Xbox 360
                    VendorId = 0x045E, // Microsoft
                    ProductId = 0x028E // Xbox 360 Controller
                };

                IntPtr buffer = Marshal.AllocHGlobal(structSize);

                try
                {
                    Marshal.StructureToPtr(pluginTarget, buffer, false);

                    Logger.Info($"ViGEmController: Calling IOCTL_PLUGIN with SerialNo={serialNo}");

                    bool success = DeviceIoControl(
                        vigemHandle,
                        IOCTL_PLUGIN,
                        buffer,
                        (uint)structSize,
                        IntPtr.Zero,
                        0,
                        out uint bytesReturned,
                        IntPtr.Zero);

                    if (success)
                    {
                        isPluggedIn = true;
                        Logger.Info($"ViGEmController: Virtual Xbox 360 controller plugged in (serial={serialNo})");
                        return true;
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        Logger.Error($"ViGEmController: Failed to plug in controller. Error: {error} (0x{error:X})");
                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Exception plugging in controller: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Unplug the virtual controller.
        /// </summary>
        public bool Unplug()
        {
            if (!isPluggedIn)
                return true;

            try
            {
                var unplugTarget = new VIGEM_UNPLUG_TARGET
                {
                    Size = (uint)Marshal.SizeOf<VIGEM_UNPLUG_TARGET>(),
                    SerialNo = serialNo
                };

                int size = Marshal.SizeOf(unplugTarget);
                IntPtr buffer = Marshal.AllocHGlobal(size);

                try
                {
                    Marshal.StructureToPtr(unplugTarget, buffer, false);

                    bool success = DeviceIoControl(
                        vigemHandle,
                        IOCTL_UNPLUG,
                        buffer,
                        (uint)size,
                        IntPtr.Zero,
                        0,
                        out uint bytesReturned,
                        IntPtr.Zero);

                    isPluggedIn = false;
                    Logger.Info($"ViGEmController: Virtual controller unplugged (serial={serialNo})");
                    return success;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Exception unplugging controller: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets whether the virtual controller is currently plugged in.
        /// </summary>
        public bool IsPluggedIn => isPluggedIn && vigemHandle != null && !vigemHandle.IsInvalid;

        /// <summary>
        /// Ensures the ViGEm controller is connected and plugged in.
        /// Attempts to reconnect if connection was lost.
        /// </summary>
        public bool EnsureConnected()
        {
            // Check if already connected and plugged in
            if (vigemHandle != null && !vigemHandle.IsInvalid && isPluggedIn)
            {
                return true;
            }

            Logger.Info("ViGEmController: Connection lost, attempting to reconnect...");

            // Unplug first if we think we're still plugged in (cleanup orphaned controller)
            if (isPluggedIn && vigemHandle != null && !vigemHandle.IsInvalid)
            {
                Unplug();
            }

            // Close old handle if invalid
            if (vigemHandle != null)
            {
                if (!vigemHandle.IsInvalid)
                    vigemHandle.Close();
                vigemHandle = null;
            }
            isPluggedIn = false;

            // Reconnect
            if (!Connect())
            {
                Logger.Error("ViGEmController: Failed to reconnect to ViGEmBus");
                return false;
            }

            // Re-plug the controller (will reuse existing serial)
            if (!PlugIn())
            {
                Logger.Error("ViGEmController: Failed to re-plug virtual controller");
                return false;
            }

            Logger.Info("ViGEmController: Successfully reconnected and re-plugged virtual controller");
            return true;
        }

        /// <summary>
        /// Set the Xbox Guide button state.
        /// </summary>
        public bool SetGuide(bool pressed)
        {
            if (!isPluggedIn || vigemHandle == null || vigemHandle.IsInvalid)
                return false;

            try
            {
                var report = new XUSB_SUBMIT_REPORT
                {
                    Size = (uint)Marshal.SizeOf<XUSB_SUBMIT_REPORT>(),
                    SerialNo = serialNo,
                    Buttons = pressed ? XINPUT_GAMEPAD_GUIDE : (ushort)0,
                    LeftTrigger = 0,
                    RightTrigger = 0,
                    LeftThumbX = 0,
                    LeftThumbY = 0,
                    RightThumbX = 0,
                    RightThumbY = 0
                };

                int size = Marshal.SizeOf(report);
                IntPtr buffer = Marshal.AllocHGlobal(size);

                try
                {
                    Marshal.StructureToPtr(report, buffer, false);

                    bool success = DeviceIoControl(
                        vigemHandle,
                        IOCTL_SUBMIT_REPORT,
                        buffer,
                        (uint)size,
                        IntPtr.Zero,
                        0,
                        out uint bytesReturned,
                        IntPtr.Zero);

                    return success;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Exception setting guide button: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;

            if (isPluggedIn)
                Unplug();

            if (vigemHandle != null && !vigemHandle.IsInvalid)
            {
                vigemHandle.Close();
                vigemHandle = null;
            }

            Logger.Info("ViGEmController: Disposed");
        }
    }
}
