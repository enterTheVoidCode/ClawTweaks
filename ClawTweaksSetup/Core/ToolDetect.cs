using System;
using System.IO;
using Microsoft.Win32;

namespace ClawTweaksSetup.Core
{
    /// <summary>Result of a single tool's presence check.</summary>
    public sealed class ToolStatus
    {
        public string Name { get; set; }
        public bool Installed { get; set; }
        public string Detail { get; set; }   // where it was found, or why it's considered missing
    }

    /// <summary>
    /// Detects the required prerequisite tools (HidHide, usbip-win2, RTSS) using pure registry /
    /// service-key / file checks — no PowerShell, so it is instant and safe to run repeatedly.
    ///
    /// The detection logic mirrors the helper's own checks (Setup-Tools.ps1 Test-*Installed,
    /// HidHideHelper.IsInstalled, RTSSHelper) so the setup and the in-app Setup tab agree.
    /// ViGEm is intentionally NOT checked — VIIPER (usbip) is the sole virtual-controller backend.
    /// </summary>
    public static class ToolDetect
    {
        public static ToolStatus HidHide()
        {
            // 1) HidHideCLI on disk (definitive)
            foreach (var cli in new[]
            {
                @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
                @"C:\Program Files\Nefarius Software Solutions e.U\HidHide\x64\HidHideCLI.exe",
                @"C:\Program Files\HidHide\x64\HidHideCLI.exe",
            })
            {
                if (File.Exists(cli))
                    return Ok("HidHide", cli);
            }

            // 2) Kernel-driver service registered
            if (ServiceKeyExists("HidHide"))
                return Ok("HidHide", "driver service present");

            // 3) Vendor registry marker
            if (RegKeyExists(RegistryHive.LocalMachine, @"SOFTWARE\Nefarius Software Solutions e.U.\HidHide"))
                return Ok("HidHide", "vendor registry key present");

            return Missing("HidHide");
        }

        public static ToolStatus Usbip()
        {
            // 1) usbip.exe on disk (Inno-installed) — a few known layouts
            foreach (var exe in new[]
            {
                @"C:\Program Files\USBip\usbip.exe",
                @"C:\Program Files\usbip-win2\usbip.exe",
                @"C:\Program Files\usbipd-win\usbip.exe",
            })
            {
                if (File.Exists(exe))
                    return Ok("usbip", exe);
            }

            // 2) UDE driver service registered (usbip-win2)
            foreach (var svc in new[] { "usbip2_ude", "usbip_vhci", "usbipd" })
            {
                if (ServiceKeyExists(svc))
                    return Ok("usbip", $"driver service '{svc}' present");
            }

            // 3) Add/Remove-Programs entry
            if (ArpDisplayNameContains("usbip"))
                return Ok("usbip", "listed in installed programs");

            return Missing("usbip");
        }

        /// <summary>
        /// RTSS presence — deliberately mirrors Shared RTSSHelper.IsInstalled(): only an RTSS.exe that
        /// actually exists on disk counts. A bare registry key must NEVER count: an NSIS uninstall leaves
        /// Unwinder\RTSS (incl. its InstallDir value) behind, so trusting the key reported RTSS as
        /// installed on machines where it was long gone. That made this screen disagree with the helper —
        /// which correctly saw it missing — so the wizard skipped installing RTSS while onboarding could
        /// never finalize, with no way out for the user. The registry is used only as a POINTER to the
        /// install dir; the file check is the proof.
        /// </summary>
        public static ToolStatus Rtss()
        {
            // 1) Registry InstallDir → verify the exe is really there (orphan-key safe).
            foreach (var key in new[]
            {
                @"SOFTWARE\WOW6432Node\Unwinder\RTSS",
                @"SOFTWARE\Unwinder\RTSS",
                @"SOFTWARE\WOW6432Node\Guru3D\RTSS",
                @"SOFTWARE\Guru3D\RTSS",
            })
            {
                var dir = RegValue(RegistryHive.LocalMachine, key, "InstallDir");
                if (string.IsNullOrEmpty(dir)) continue;
                var exe = Path.Combine(dir, "RTSS.exe");
                if (File.Exists(exe))
                    return Ok("RTSS", exe);
            }

            // 2) Default install locations.
            foreach (var exe in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "RivaTuner Statistics Server", "RTSS.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "RivaTuner Statistics Server", "RTSS.exe"),
            })
            {
                if (File.Exists(exe))
                    return Ok("RTSS", exe);
            }

            return Missing("RTSS");
        }

        #region helpers
        private static ToolStatus Ok(string name, string detail) =>
            new ToolStatus { Name = name, Installed = true, Detail = detail };

        private static ToolStatus Missing(string name) =>
            new ToolStatus { Name = name, Installed = false, Detail = "not found" };

        private static bool ServiceKeyExists(string serviceName) =>
            RegKeyExists(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{serviceName}");

        private static bool RegKeyExists(RegistryHive hive, string subKey)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var k = baseKey.OpenSubKey(subKey);
                return k != null;
            }
            catch { return false; }
        }

        private static string RegValue(RegistryHive hive, string subKey, string valueName)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var k = baseKey.OpenSubKey(subKey);
                return k?.GetValue(valueName) as string;
            }
            catch { return null; }
        }

        private static bool ArpDisplayNameContains(string needle)
        {
            foreach (var root in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var key = baseKey.OpenSubKey(root);
                    if (key == null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var k = key.OpenSubKey(sub);
                        var name = k?.GetValue("DisplayName") as string;
                        if (!string.IsNullOrEmpty(name) &&
                            name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
                catch { /* ignore and try next root */ }
            }
            return false;
        }
        #endregion
    }
}
