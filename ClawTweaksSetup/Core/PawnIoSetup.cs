using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Downloads + verifies + silently installs PawnIO (kernel driver used for TDP control — required
    /// exactly like HidHide/RTSS/usbip). Ported 1:1 from the helper's Setup-Tools.ps1 Install-PawnIO:
    /// same pinned "latest release" download URL, same /S silent install, same winget fallback
    /// (namazso.PawnIO) if the direct install doesn't leave a working driver behind. Authenticode +
    /// publisher verification added on top (not present in the PowerShell version) to match the same
    /// safety bar already used for usbip's downloaded installer — never run an unverified exe.
    /// </summary>
    public static class PawnIoSetup
    {
        private const string DownloadUrl =
            "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";

        private static readonly string[] ExpectedSignerSubstrings = { "namazso" };

        public enum Result { Failed, Success }

        /// <summary>Blocks until done. Tries the direct silent installer first, then winget.</summary>
        public static Result Run(Action<string> log = null)
        {
            string exePath = null;
            try
            {
                exePath = Path.Combine(Path.GetTempPath(), "ClawTweaks_PawnIO-setup.exe");
                log?.Invoke("Downloading signed PawnIO installer…");
                if (Download(DownloadUrl, exePath) && VerifyAuthenticode(exePath))
                {
                    log?.Invoke("Signature OK — installing silently…");
                    var psi = new ProcessStartInfo { FileName = exePath, Arguments = "/S", UseShellExecute = false };
                    using var proc = Process.Start(psi);
                    if (proc != null && proc.WaitForExit(5 * 60 * 1000) && ToolDetect.PawnIO().Installed)
                    {
                        log?.Invoke("PawnIO installed.");
                        return Result.Success;
                    }
                    log?.Invoke("Direct silent install didn't leave a working driver — falling back to winget.");
                }
                else
                {
                    log?.Invoke("Download or signature check failed — falling back to winget.");
                }

                return ToolInstaller.InstallPawnIOViaWinget(log) ? Result.Success : Result.Failed;
            }
            catch (Exception ex)
            {
                log?.Invoke("PawnIO install error: " + ex.Message);
                return Result.Failed;
            }
            finally
            {
                if (exePath != null) { try { File.Delete(exePath); } catch { } }
            }
        }

        private static bool Download(string url, string destPath)
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "ClawTweaks");
                using var resp = http.GetAsync(url).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                using (var fs = File.Create(destPath))
                    resp.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                var fi = new FileInfo(destPath);
                return fi.Exists && fi.Length > 0;
            }
            catch { return false; }
        }

        private static bool VerifyAuthenticode(string path)
        {
            if (!WinVerifyTrustValid(path)) return false;
            try
            {
                var cert = X509CertificateLoader.LoadCertificateFromFile(path);
                string subject = cert.Subject ?? string.Empty;
                return ExpectedSignerSubstrings.Any(s => subject.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        #region WinVerifyTrust
        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private static bool WinVerifyTrustValid(string path)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                    dwUnionChoice = 1,       // WTD_CHOICE_FILE
                    pFile = pFile,
                    dwStateAction = 0,
                    dwProvFlags = 0x00000010, // WTD_SAFER_FLAG
                };
                IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
                try
                {
                    Marshal.StructureToPtr(data, pData, false);
                    uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pData);
                    return result == 0;
                }
                finally { Marshal.FreeHGlobal(pData); }
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(pFile); }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hWnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }
        #endregion
    }
}
