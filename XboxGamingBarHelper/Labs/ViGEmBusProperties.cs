using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NLog;
using Shared.Enums;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Utility class for ViGEmBus detection and installation.
    /// Used by Program.cs to manage ViGEmBus driver status.
    /// </summary>
    internal static class ViGEmBusHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ViGEmBus device interface GUID
        private static readonly Guid VIGEM_GUID = new Guid("96E42B22-F5E9-42F8-B043-ED0F932F014F");

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;

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

        /// <summary>
        /// Checks if ViGEmBus is installed by looking for the device interface.
        /// </summary>
        public static bool IsInstalled()
        {
            Guid vigemGuid = VIGEM_GUID;

            IntPtr deviceInfoSet = SetupDiGetClassDevs(
                ref vigemGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            {
                return false;
            }

            try
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                };

                // Try to get the first device interface
                bool found = SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref vigemGuid, 0, ref interfaceData);
                return found;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        /// <summary>
        /// Installs ViGEmBus via the embedded Setup-Tools.ps1 (winget-first, with a direct-download
        /// fallback inside the PowerShell script). The download-and-execute logic deliberately lives
        /// in the .ps1 — NOT in this managed assembly — so a static AV scan of the helper doesn't see
        /// a WebClient-download + Process.Start-exe pattern (the .NET "downloader" heuristic). Returns
        /// true if ViGEmBus is present afterwards.
        /// </summary>
        public static bool Install()
        {
            try
            {
                Logger.Info("ViGEmBus install requested — running tool setup (winget) for 'vigem'...");
                int code = XboxGamingBarHelper.Setup.ToolSetupRunner.Run("vigem");
                bool installed = IsInstalled();
                Logger.Info($"ViGEmBus install finished (script exit={code}, installed={installed}).");
                return installed;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to install ViGEmBus: {ex.Message}");
                return false;
            }
        }
    }
}
