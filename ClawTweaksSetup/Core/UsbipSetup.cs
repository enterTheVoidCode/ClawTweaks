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
    /// Downloads + verifies + installs usbip-win2 (the VIIPER backend prerequisite). Ported 1:1 from
    /// the helper's UsbipInstaller: pinned signed release URL, Authenticode + publisher verification,
    /// then the interactive Inno installer (/NORESTART — the visible wizard is required so the user
    /// confirms the driver-install prompt). Exit 3010 = success but a reboot is required.
    /// </summary>
    public static class UsbipSetup
    {
        private const string DownloadUrl =
            "https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.7.7/USBip-0.9.7.7-x64.exe";

        private static readonly string[] ExpectedSignerSubstrings = { "Scheibling", "Cloudyne" };

        public enum Result { Failed, Success, RebootRequired }

        /// <summary>Blocks until done. Returns Success, RebootRequired (exit 3010) or Failed.</summary>
        public static Result Run(Action<string> log = null)
        {
            string exePath = null;
            try
            {
                exePath = Path.Combine(Path.GetTempPath(), "ClawTweaks_USBip-setup.exe");
                log?.Invoke("Downloading signed usbip-win2 installer…");
                if (!Download(DownloadUrl, exePath)) { log?.Invoke("Download failed."); return Result.Failed; }

                if (!VerifyAuthenticode(exePath))
                {
                    log?.Invoke("Signature verification failed — refusing to run the installer.");
                    return Result.Failed;
                }

                log?.Invoke("Signature OK — launching installer (confirm the driver prompt)…");
                var psi = new ProcessStartInfo { FileName = exePath, Arguments = "/NORESTART", UseShellExecute = false };
                using var proc = Process.Start(psi);
                if (proc == null) return Result.Failed;
                if (!proc.WaitForExit(10 * 60 * 1000))
                {
                    try { proc.Kill(); } catch { }
                    return Result.Failed;
                }
                int code = proc.ExitCode;
                log?.Invoke($"Installer finished (exit {code}).");
                return code == 3010 ? Result.RebootRequired : (code == 0 ? Result.Success : Result.Failed);
            }
            catch (Exception ex)
            {
                log?.Invoke("usbip install error: " + ex.Message);
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
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
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
