using System;
using System.Linq;
using HidSharp;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// HID communication with the physical MSI Claw controller.
    ///
    /// Adapted from HC fork ClawA1M.cs SwitchMode() — sends a vendor-defined
    /// HID output report to set the controller's firmware mode.
    ///
    /// Called when MSI Center M is disabled (ApplyActive(false)) so the
    /// physical controller is forced into XInput mode, preventing the
    /// long-Start-press mouse-mode switch that MSI firmware triggers when
    /// in its native "MSI" or "Desktop" mode.
    /// </summary>
    internal static class MSIClawHidController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Physical MSI Claw controller HID identifiers (HC fork ClawA1M.cs)
        private const int VendorId  = 0x0DB0;
        private const int ProductId = 0x1901; // XInput mode PID

        // Vendor command interface — present in EVERY firmware mode, but on a DIFFERENT
        // usage page depending on the mode (1:1 from HC ClawA1M hidFilters):
        //   XInput mode  (PID_1901): UsagePage 0xFFA0, Usage 0x0001
        //   DInput mode  (PID_1902): UsagePage 0xFFF0, Usage 0x0040
        // ClawTweaks normally runs the Claw in DInput mode (ClawButtonMonitor switches to
        // PID_1902), so searching ONLY 0xFFA0 made FindClawHidDevice return null in normal
        // operation → LED / vendor writes were a silent no-op. Search BOTH pages, exactly like
        // ClawButtonMonitor.FindCommandDevice does.
        private const int CmdUsagePageXInput = 0xFFA0;
        private const int CmdUsageXInput     = 0x0001;
        private const int CmdUsagePageDInput = 0xFFF0;
        private const int CmdUsageDInput     = 0x0040;

        // HC ClawA1M.cs: SwitchMode byte commands (padded to 64 bytes before send)
        // { 0x0F, 0x00, 0x00, 0x3C, CommandType.SwitchMode(0x24), GamepadMode.<X>, MKeysFunction.Macro(0x00) }
        // GamepadMode enum (HC ClawA1M): Offline=0, XInput=1, DirectInput=2, MSI=3, Desktop=4, BIOS=5
        private static readonly byte[] SwitchModeXInputCmd   = { 15, 0, 0, 60, 36, 1, 0 }; // GamepadMode.XInput      = 1
        private static readonly byte[] SwitchModeDInputCmd   = { 15, 0, 0, 60, 36, 2, 0 }; // GamepadMode.DirectInput = 2 (→ PID_1902)
        private static readonly byte[] SwitchModeDesktopCmd  = { 15, 0, 0, 60, 36, 4, 0 }; // GamepadMode.Desktop     = 4

        /// <summary>
        /// Sends the XInput mode-switch command to the physical MSI Claw controller.
        ///
        /// Used proactively on startup (controller emulation active) so the user gets
        /// gamepad behaviour immediately while ClawButtonMonitor's DInput settle (~2.5 s)
        /// completes in the background.  Also used by MSIClawDesktopModeForwarder to
        /// enable XInput reading for software mouse emulation.
        ///
        /// Adapted from HC ClawA1M.SwitchMode(GamepadMode.XInput).
        /// </summary>
        public static bool TrySwitchToXInput()
        {
            return TrySendModeCmd(SwitchModeXInputCmd, "XInput");
        }

        /// <summary>
        /// Sends the DirectInput mode-switch command to the physical MSI Claw controller.
        /// After this, the device re-enumerates as PID_1902 (a HID gamepad) which HidHide CAN
        /// cloak — unlike the XInput gamepad (PID_1901), which XInput accesses bypass HidHide.
        ///
        /// Used by External Gamepad Mode to park the handheld controller as a hidden DInput
        /// device (no XInput gamepad, no virtual ViGEm), leaving only an external gamepad.
        /// The keyboard HID stays on its own PID, so Win+G keeps working.
        ///
        /// The device takes ~2.5 s to re-enumerate as PID_1902; wait before querying it.
        /// Adapted from HC ClawA1M.SwitchMode(GamepadMode.DirectInput).
        /// </summary>
        public static bool TrySwitchToDInput()
        {
            return TrySendModeCmd(SwitchModeDInputCmd, "DInput");
        }

        /// <summary>
        /// Sends the Desktop mode-switch command to the physical MSI Claw controller.
        ///
        /// Mirrors HC ClawA1M.Close() → SwitchMode(GamepadMode.Desktop):
        /// puts the controller back into the hardware-native Desktop state when
        /// ClawTweaks shuts down or emulation is fully disabled.
        ///
        /// In Desktop mode the MSI firmware handles basic cursor movement natively.
        /// The controller is NOT readable via XInput in this mode — call TrySwitchToXInput()
        /// first if you need to read inputs.
        ///
        /// Adapted from HC ClawA1M.SwitchMode(GamepadMode.Desktop).
        /// </summary>
        public static bool TrySwitchToDesktop()
        {
            return TrySendModeCmd(SwitchModeDesktopCmd, "Desktop");
        }

        private static bool TrySendModeCmd(byte[] cmd, string modeName)
        {
            try
            {
                HidDevice device = FindClawHidDevice();
                if (device == null)
                {
                    Logger.Debug($"[MSIClawHidController] Physical controller not found for SwitchMode({modeName})");
                    return false;
                }

                byte[] msg = new byte[64];
                Array.Copy(cmd, msg, cmd.Length);

                using (HidStream stream = device.Open())
                {
                    stream.Write(msg);
                }

                Logger.Info($"[MSIClawHidController] SwitchMode({modeName}) sent successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MSIClawHidController] TrySwitchTo{modeName} failed: {ex.Message}");
                return false;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────

        /// <summary>Exposed as internal so MsiClawLedController can reuse device discovery.</summary>
        internal static HidDevice FindClawHidDeviceInternal() => FindClawHidDevice();

        private static HidDevice FindClawHidDevice()
        {
            try
            {
                // Search ALL VID=0x0DB0 devices — the PID changes with the controller mode
                // (0x1901 = XInput, 0x1902 = DirectInput, other = Desktop/Mouse mode).
                // The vendor HID interface (UsagePage=0xFFA0, Usage=0x0001) is present in
                // every mode and is the only interface that accepts SwitchMode commands.
                var candidates = DeviceList.Local.GetHidDevices()
                    .Where(d => d.VendorID == VendorId)
                    .ToList();

                foreach (var device in candidates)
                {
                    try
                    {
                        var descriptor = device.GetReportDescriptor();
                        if (descriptor == null) continue;

                        foreach (var item in descriptor.DeviceItems)
                        {
                            foreach (uint encodedUsage in item.Usages.GetAllValues())
                            {
                                int page  = (int)((encodedUsage >> 16) & 0xFFFF);
                                int usage = (int)(encodedUsage & 0xFFFF);
                                // Accept the vendor command interface in EITHER firmware mode:
                                //   XInput (PID_1901): 0xFFA0 / 0x0001
                                //   DInput (PID_1902): 0xFFF0 / 0x0040
                                if ((page == CmdUsagePageXInput && usage == CmdUsageXInput) ||
                                    (page == CmdUsagePageDInput && usage == CmdUsageDInput))
                                    return device;
                            }
                        }
                    }
                    catch
                    {
                        // Descriptor read may fail on restricted interfaces; skip and try next
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MSIClawHidController] Device enumeration failed: {ex.Message}");
            }

            return null;
        }
    }
}
