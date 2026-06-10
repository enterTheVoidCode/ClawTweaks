using NLog;
using System;
using System.Runtime.InteropServices;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// MSI Claw "OverBoost" support flag, stored in the firmware (UEFI) variable "MsiDCVarData".
    ///
    /// 1:1 port of HandheldCompanion ClawA1M.InitOverBoost(true): HC reads the variable at device
    /// Open() and, if byte[1] (OverBoost-support) is 0, sets it to 1 and writes it back. Without this
    /// the MSI EC clamps the sustained CPU power back to its low default (~15 W) a short time into
    /// load, regardless of the PL1/PL2 we push over the ACPI WMI — which is the "TDP drops to 15 W"
    /// symptom. The flag is persistent firmware NV storage, so it survives reboots once set.
    ///
    /// HC accesses this through a bundled native helper (UEFIVaribleDll.dll). We use the Win32
    /// firmware-variable APIs directly (kernel32) so nothing extra is shipped — AV-safe. Requires
    /// the SE_SYSTEM_ENVIRONMENT privilege, which the elevated helper can enable.
    /// </summary>
    internal static class MsiOverBoost
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string VarName = "MsiDCVarData";
        // The Win32 firmware API wants the GUID in registry/brace form.
        private const string VarGuid = "{DD96BAAF-145E-4F56-B1CF-193256298E99}";
        private const int OverBoostSupportByte = 1; // box[1] — InitOverBoost target (HC)

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFirmwareEnvironmentVariableExW(
            string lpName, string lpGuid, byte[] pBuffer, uint nSize, out uint pdwAttributes);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetFirmwareEnvironmentVariableExW(
            string lpName, string lpGuid, byte[] pBuffer, uint nSize, uint dwAttributes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string host, string name, out long luid);

        // Pack=4 is essential: the Win32 TOKEN_PRIVILEGES is { DWORD Count; LUID Luid; DWORD Attributes }
        // with the LUID (8 bytes) immediately after Count at offset 4. With default packing the
        // 8-byte `long Luid` would be 8-aligned (offset 8), so AdjustTokenPrivileges would read a
        // bogus LUID and silently fail to enable the privilege (read then returns err=1314).
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TOKEN_PRIVILEGES { public uint Count; public long Luid; public uint Attributes; }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
            ref TOKEN_PRIVILEGES newState, uint len, IntPtr prev, IntPtr retLen);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;
        private const string SE_SYSTEM_ENVIRONMENT_NAME = "SeSystemEnvironmentPrivilege";

        private const int ERROR_NOT_ALL_ASSIGNED = 1300;

        private static bool EnablePrivilege()
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                {
                    Logger.Warn($"[MSIClaw] OverBoost: OpenProcessToken failed (err={Marshal.GetLastWin32Error()})");
                    return false;
                }
                if (!LookupPrivilegeValue(null, SE_SYSTEM_ENVIRONMENT_NAME, out long luid))
                {
                    Logger.Warn($"[MSIClaw] OverBoost: LookupPrivilegeValue failed (err={Marshal.GetLastWin32Error()})");
                    return false;
                }
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                bool ok = AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                int err = Marshal.GetLastWin32Error();
                // AdjustTokenPrivileges returns true even when the privilege is absent from the token;
                // ERROR_NOT_ALL_ASSIGNED is the real "didn't take" signal.
                if (!ok || err == ERROR_NOT_ALL_ASSIGNED)
                {
                    Logger.Warn($"[MSIClaw] OverBoost: AdjustTokenPrivileges did not enable SeSystemEnvironment (ok={ok}, err={err})");
                    return false;
                }
                return true;
            }
            catch (Exception ex) { Logger.Warn($"[MSIClaw] OverBoost: EnablePrivilege threw: {ex.Message}"); return false; }
            finally { if (token != IntPtr.Zero) CloseHandle(token); }
        }

        /// <summary>
        /// Ensures the MSI OverBoost-support flag is enabled (box[1] = 1), exactly like HC's
        /// InitOverBoost(true). Best-effort: returns true only when the flag is confirmed set.
        /// </summary>
        public static bool EnsureOverBoostEnabled()
        {
            try
            {
                bool priv = EnablePrivilege();

                var box = new byte[4096];
                uint attrs;
                uint len = GetFirmwareEnvironmentVariableExW(VarName, VarGuid, box, (uint)box.Length, out attrs);
                if (len == 0)
                {
                    Logger.Warn($"[MSIClaw] OverBoost: could not read {VarName} (err={Marshal.GetLastWin32Error()}, privEnabled={priv}) — skipping");
                    return false;
                }

                if (box[OverBoostSupportByte] != 0)
                {
                    Logger.Info("[MSIClaw] OverBoost already enabled (box[1]=1)");
                    return true;
                }

                box[OverBoostSupportByte] = 1;
                bool ok = SetFirmwareEnvironmentVariableExW(VarName, VarGuid, box, len, attrs);
                Logger.Info($"[MSIClaw] OverBoost enable: wrote box[1]=1 (len={len}, attrs=0x{attrs:X}) — ok={ok}, err={Marshal.GetLastWin32Error()}");
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MSIClaw] OverBoost EnsureEnabled failed: {ex.Message}");
                return false;
            }
        }
    }
}
