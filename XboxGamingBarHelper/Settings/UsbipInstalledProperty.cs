using Microsoft.Win32;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Detects whether usbip-win2 is installed on the system.
    /// Required prerequisite for the VIIPER emulation backend.
    /// </summary>
    internal class UsbipInstalledProperty : HelperProperty<bool, SettingsManager>
    {
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
            // usbip-win2 installs the usbip2vhci kernel service. Presence of its service key
            // in the registry is a reliable indicator (works without requiring elevation to query).
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\usbip2vhci"))
                {
                    if (key != null) return true;
                }
                // Fallback: older builds used different service names.
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\VHCI"))
                {
                    if (key != null) return true;
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
