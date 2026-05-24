using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Utility for changing a Windows service's startup mode.
    /// Ported 1:1 from HandheldCompanion/Utils/ServiceUtils.cs.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    internal static class ServiceUtils
    {
        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_QUERY_CONFIG = 0x00000001;
        private const uint SERVICE_CHANGE_CONFIG = 0x00000002;

        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig(
            IntPtr hService,
            uint nServiceType,
            uint nStartType,
            uint nErrorControl,
            string lpBinaryPathName,
            string lpLoadOrderGroup,
            IntPtr lpdwTagId,
            [In] char[] lpDependencies,
            string lpServiceStartName,
            string lpPassword,
            string lpDisplayName);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint dwAccess);

        [DllImport("advapi32.dll", EntryPoint = "CloseServiceHandle")]
        private static extern int CloseServiceHandle(IntPtr hSCObject);

        /// <summary>
        /// Change the startup mode for the provided service.
        /// Ported 1:1 from HandheldCompanion/Utils/ServiceUtils.cs.
        /// </summary>
        public static bool ChangeStartMode(ServiceController svc, ServiceStartMode mode, out string error)
        {
            error = string.Empty;

            var hManager = IntPtr.Zero;
            var hService = IntPtr.Zero;

            try
            {
                hManager = OpenSCManager(null, null, SC_MANAGER_CONNECT + SC_MANAGER_ENUMERATE_SERVICE);
                if (hManager == IntPtr.Zero)
                    return false;

                hService = OpenService(hManager, svc.ServiceName, SERVICE_QUERY_CONFIG | SERVICE_CHANGE_CONFIG);
                if (hService == IntPtr.Zero)
                    return false;

                bool result = ChangeServiceConfig(hService, SERVICE_NO_CHANGE, (uint)mode, SERVICE_NO_CHANGE,
                    null, null, IntPtr.Zero, null, null, null, null);

                return result;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (hService != IntPtr.Zero) CloseServiceHandle(hService);
                if (hManager != IntPtr.Zero) CloseServiceHandle(hManager);
            }
        }
    }
}
