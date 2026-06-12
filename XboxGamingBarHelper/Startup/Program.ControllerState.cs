using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// Controller-State diagnostic for the Controller-tab status card.
    ///
    /// Mirrors Diagnostics\Get-ControllerState.ps1 but runs in-process in the elevated
    /// helper (which can read ViGEm / HidHide / PnP directly). All inspection is read-only;
    /// nothing here changes hide/unhide or emulation state.
    /// </summary>
    internal partial class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CS_XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CS_XINPUT_STATE
        {
            public uint dwPacketNumber;
            public CS_XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint CS_XInputGetState(uint dwUserIndex, ref CS_XINPUT_STATE pState);

        /// <summary>
        /// Build the compact controller-state string for the widget's Controller-State card.
        /// Format: "state|vigem|pid1901|pid1902|blocked|xinput".
        ///   state   : 0=Undetermined, 1=VirtualControllerMode, 2=HwControllerMode
        ///   vigem   : virtual Xbox360 controllers (VID_045E&amp;PID_028E)
        ///   pid1901 : MSI Claw XInput/keyboard devices (VID_0DB0&amp;PID_1901)
        ///   pid1902 : MSI Claw DInput gamepad devices (VID_0DB0&amp;PID_1902)
        ///   blocked : devices currently hidden by HidHide (-1 = HidHide state unknown)
        ///   xinput  : connected XInput slots (0-4)
        /// </summary>
        internal static string BuildControllerStateString()
        {
            int vigem = 0, pid1901 = 0, pid1902 = 0, blocked = 0, xinput = 0;
            bool hidHideKnown = false;
            bool cloaking = false;

            // ── PnP devices via WMI ───────────────────────────────────────────
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VID_045E%PID_028E%' OR PNPDeviceID LIKE '%VID_0DB0%'"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string id = (mo["PNPDeviceID"] as string) ?? "";
                        if (id.IndexOf("VID_045E", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            id.IndexOf("PID_028E", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            vigem++;
                        }
                        else if (id.IndexOf("VID_0DB0", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (id.IndexOf("PID_1901", StringComparison.OrdinalIgnoreCase) >= 0) pid1901++;
                            else if (id.IndexOf("PID_1902", StringComparison.OrdinalIgnoreCase) >= 0) pid1902++;
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Debug($"ControllerState: WMI query failed: {ex.Message}"); }

            // ── HidHide live state ────────────────────────────────────────────
            try
            {
                var suppression = controllerEmulationManager?.SuppressionManager;
                if (suppression != null)
                    hidHideKnown = suppression.TryGetState(out cloaking, out blocked);
            }
            catch (Exception ex) { Logger.Debug($"ControllerState: HidHide state failed: {ex.Message}"); }

            // ── XInput connected slots ────────────────────────────────────────
            try
            {
                for (uint i = 0; i < 4; i++)
                {
                    var st = new CS_XINPUT_STATE();
                    if (CS_XInputGetState(i, ref st) == 0) xinput++;
                }
            }
            catch (Exception ex) { Logger.Debug($"ControllerState: XInput probe failed: {ex.Message}"); }

            // ── Classify ──────────────────────────────────────────────────────
            // 1 = Virtual controller mode: a ViGEm virtual pad is present AND the physical
            //     DInput gamepad is hidden by HidHide (cloaking on / blocked > 0).
            // 2 = HW controller mode: no ViGEm pad AND HidHide not cloaking (0 blocked) AND
            //     the physical XInput device (PID_1901) is visible.
            // 0 = Undetermined / transitional.
            int state = 0;
            bool hidHideHiding = hidHideKnown && (cloaking || blocked > 0);
            if (_externalGamepadModeActive)
                state = 3; // External Gamepad Mode: handheld parked/hidden, only external gamepad visible
            else if (vigem > 0 && hidHideHiding)
                state = 1;
            else if (vigem == 0 && pid1901 > 0 && hidHideKnown && !cloaking && blocked == 0)
                state = 2;

            // The classification above infers the mode purely from live PnP/HidHide/XInput
            // snapshots, which are racy: WMI may briefly omit the ViGEm device during a PnP
            // re-enumeration, or HidHide's API may momentarily be unavailable (hidHideKnown=false).
            // When that happens neither strict branch matches and we'd leave state=0 ("unknown"),
            // even though the controller is working fine — the reported "rare invalid state" that
            // forced a manual virtual<->HW toggle to clear. Fall back to the helper's authoritative
            // emulation intent (the master switch the user actually toggles, valid for both the
            // legacy ViGEm and VIIPER backends) so the headline is always HW or Virtual.
            if (state == 0)
            {
                bool emulationOn = controllerEmulationManager?.EmulationEnabled ?? false;
                state = emulationOn ? 1 : 2;
                Logger.Debug($"ControllerState: live snapshot undetermined " +
                             $"(vigem={vigem}, pid1901={pid1901}, hidHideKnown={hidHideKnown}, " +
                             $"cloaking={cloaking}, blocked={blocked}) → resolved to {state} via EmulationEnabled={emulationOn}");
            }

            // -1 signals "HidHide state unknown" to the widget so it doesn't show a misleading 0.
            int blockedOut = hidHideKnown ? blocked : -1;
            return $"{state}|{vigem}|{pid1901}|{pid1902}|{blockedOut}|{xinput}";
        }
    }
}
