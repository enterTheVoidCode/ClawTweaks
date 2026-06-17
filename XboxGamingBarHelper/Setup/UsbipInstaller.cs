using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace XboxGamingBarHelper.Setup
{
    /// <summary>
    /// Installs the usbip-win2 (vadimgrn) kernel driver — the VIIPER emulation backend prerequisite.
    /// usbip-win2 is NOT on winget, and its signed installer is ~33 MB, so it is NOT bundled (that
    /// would nearly double our package / in-app-update size). Instead the (signed) helper downloads
    /// the official, signed installer from a PINNED GitHub release URL on demand, verifies its
    /// Authenticode signature (valid + expected publisher) and only then runs it silently. No web
    /// fetch happens unless the user explicitly taps Install.
    ///
    /// The installer is an Inno Setup bundle (verified: "Inno Setup Setup Data (6.7.0)"), so it takes
    /// the Inno silent switches (<c>/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-</c>) — NOT the WiX/
    /// msiexec ones. Inno self-registers a QuietUninstallString (unins000.exe /VERYSILENT) in ARP,
    /// which <see cref="Labs.ToolUninstaller.UninstallUsbip"/> uses for silent removal (no re-download).
    /// </summary>
    internal static class UsbipInstaller
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Pinned, known-good signed release (the version verified to work with our libviiper).
        private const string DownloadUrl =
            "https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.7.7/USBip-0.9.7.7-x64.exe";

        // The downloaded installer must be Authenticode-valid AND signed by this publisher
        // (usbip-win2 is signed by "Cloudyne Systems (Scheibling Consulting AB)").
        private static readonly string[] ExpectedSignerSubstrings = { "Scheibling", "Cloudyne" };

        /// <summary>
        /// Downloads + verifies + silently installs usbip-win2. Blocks until done. Returns the
        /// installer exit code (0 = success, 3010 = success-reboot-required), or -1 on download/
        /// signature/launch failure.
        /// </summary>
        public static int Run()
        {
            string exePath = null;
            try
            {
                exePath = Path.Combine(Path.GetTempPath(), "ClawTweaks_USBip-setup.exe");

                Logger.Info($"UsbipInstaller: downloading signed installer from {DownloadUrl}");
                if (!Download(DownloadUrl, exePath))
                {
                    Logger.Error("UsbipInstaller: download failed.");
                    return -1;
                }

                if (!VerifyAuthenticode(exePath))
                {
                    Logger.Error("UsbipInstaller: downloaded installer failed signature verification — refusing to run it.");
                    return -1;
                }

                // Run the Inno Setup wizard INTERACTIVELY (no /VERYSILENT). Fully-silent install left
                // the driver NOT installed: /SUPPRESSMSGBOXES answers the driver-install confirmation
                // with its default (= don't install), so usbip-win2's UDE driver never landed. The
                // visible wizard lets the user confirm the driver-install prompt. /NORESTART only
                // prevents an automatic reboot. (Revisit true-silent later via pre-trusting the
                // publisher cert in the Trusted Publishers store before launching.)
                Logger.Info("UsbipInstaller: signature OK — launching interactive Inno installer (/NORESTART)");
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "/NORESTART",
                    UseShellExecute = false,
                };

                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();
                    if (!proc.WaitForExit(10 * 60 * 1000))
                    {
                        Logger.Warn("UsbipInstaller: installer timed out after 10 minutes.");
                        try { proc.Kill(); } catch { }
                        return -1;
                    }
                    int code = proc.ExitCode;
                    Logger.Info($"UsbipInstaller: installer finished with exit code {code}" +
                                (code == 3010 ? " (reboot required to activate the driver)." : "."));
                    return code;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"UsbipInstaller.Run failed: {ex.Message}");
                return -1;
            }
            finally
            {
                if (exePath != null)
                {
                    try { File.Delete(exePath); } catch { }
                }
            }
        }

        private static bool Download(string url, string destPath)
        {
            try
            {
                // GitHub requires TLS 1.2+; .NET 4.8 usually negotiates it, but set it explicitly.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ClawTweaks");
                    wc.DownloadFile(url, destPath);
                }
                var fi = new FileInfo(destPath);
                Logger.Info($"UsbipInstaller: downloaded {fi.Length} bytes to {destPath}");
                return fi.Exists && fi.Length > 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"UsbipInstaller: download error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// True only if the file has a valid, trusted Authenticode signature AND the signer subject
        /// matches the expected usbip-win2 publisher. Both checks matter: WinVerifyTrust catches
        /// tampering / untrusted chains; the subject check pins the publisher.
        /// </summary>
        private static bool VerifyAuthenticode(string path)
        {
            if (!WinVerifyTrustValid(path))
            {
                Logger.Warn("UsbipInstaller: WinVerifyTrust reported the file is not validly signed/trusted.");
                return false;
            }
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                string subject = cert.Subject ?? string.Empty;
                bool ok = ExpectedSignerSubstrings.Any(s => subject.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
                Logger.Info($"UsbipInstaller: installer signer subject='{subject}' expectedPublisher={ok}");
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Warn($"UsbipInstaller: could not read signer certificate: {ex.Message}");
                return false;
            }
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
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = 2,            // WTD_UI_NONE
                    fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                    dwUnionChoice = 1,         // WTD_CHOICE_FILE
                    pFile = pFile,
                    dwStateAction = 0,         // WTD_STATEACTION_IGNORE
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = 0x00000010,  // WTD_SAFER_FLAG
                    dwUIContext = 0,
                };

                IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
                try
                {
                    Marshal.StructureToPtr(data, pData, false);
                    uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pData);
                    if (result != 0)
                        Logger.Info($"UsbipInstaller: WinVerifyTrust result=0x{result:X8}");
                    return result == 0; // 0 == ERROR_SUCCESS (trusted)
                }
                finally { Marshal.FreeHGlobal(pData); }
            }
            catch (Exception ex)
            {
                Logger.Warn($"UsbipInstaller: WinVerifyTrust threw: {ex.Message}");
                return false;
            }
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
