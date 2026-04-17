using Microsoft.Win32;
using Shared.Enums;
using System;
using System.IO;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Detects whether usbip-win2 is installed on the system.
    /// Required prerequisite for the VIIPER emulation backend.
    /// </summary>
    internal class UsbipInstalledProperty : HelperProperty<bool, SettingsManager>
    {
        // Real service names installed by usbip-win2 (verified on a live install).
        private static readonly string[] ServiceKeyNames = new[]
        {
            "usbip2_ude",      // primary — USB Device Emulation (driver)
            "usbip2_filter",   // upper filter driver
            "mausbip",         // MsUsbIp service
            // Historical / alternate names, kept for safety:
            "usbip2vhci",
            "VHCI",
        };

        private static readonly string[] BinaryPaths = new[]
        {
            @"C:\Program Files\USBip\usbip.exe",
            @"C:\Program Files (x86)\USBip\usbip.exe",
        };

        public UsbipInstalledProperty(SettingsManager inManager)
            : base(Detect(), null, Function.Viiper_UsbipInstalled, inManager)
        {
            Logger.Info($"usbip-win2 installed: {Value}");
        }

        /// <summary>
        /// Re-runs detection and pushes the new state. Call after a user-initiated install.
        /// </summary>
        public void Refresh()
        {
            var detected = Detect();
            if (detected != Value)
            {
                SetValue(detected);
                Logger.Info($"usbip-win2 detection refreshed: {detected}");
            }
        }

        private static bool Detect()
        {
            // Any one of these signals is enough. We're intentionally permissive so partial
            // installs or newer release builds (which might rename one service) still pass.
            try
            {
                foreach (var name in ServiceKeyNames)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                    {
                        if (key != null)
                        {
                            Logger.Debug($"usbip-win2 detected via service key: {name}");
                            return true;
                        }
                    }
                }
                foreach (var path in BinaryPaths)
                {
                    if (File.Exists(path))
                    {
                        Logger.Debug($"usbip-win2 detected via binary: {path}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"UsbipInstalled detection failed: {ex.Message}");
            }
            return false;
        }
    }
}
