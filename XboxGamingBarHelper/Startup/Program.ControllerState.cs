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
        /// <summary>
        /// Cheap pre-mount baseline check: is the physical MSI Claw controller cleanly present in
        /// the expected HW-controller state, i.e. ready for the virtual controller to be mounted on
        /// top of it? Returns true when the physical Claw XInput device (PID_1901) is visible, no
        /// stale ViGEm pad is lingering, and HidHide is not still cloaking from a previous session.
        ///
        /// Used once at the first virtual-controller mount as a self-heal pre-check (see
        /// StartClawButtonMonitorBackground). Mirrors the Controller-tab status card's classification
        /// but does NOT use its EmulationEnabled fallback — at this point emulation intent is "on"
        /// (we are about to mount) yet no ViGEm pad exists yet, so that fallback would mislead.
        /// One WMI query + one HidHide probe; only run once, so no per-frame cost.
        /// </summary>
        internal static bool IsHwControllerBaselineClean(out string diag)
            => IsHwControllerBaselineClean(out diag, out _);

        /// <summary>
        /// As <see cref="IsHwControllerBaselineClean(out string)"/>, but additionally reports whether
        /// the base is dirty because of <em>stale state a Stop()/unhide can actually fix</em>
        /// (<paramref name="staleStatePresent"/> = a lingering ViGEm pad OR HidHide still cloaking),
        /// as opposed to merely "the physical Claw hasn't enumerated yet" (pid1901=0 at cold boot).
        ///
        /// The self-heal (emulation off→on cycle) only helps the former. At a cold boot the base is
        /// "not clean" purely because pid1901=0 — the device is mid-enumeration — and a Stop()+settle
        /// cannot conjure it (measured: baseline stays clean=False afterwards), so it just wastes
        /// 1.5–3.3 s. Callers skip the heal when !staleStatePresent.
        /// </summary>
        internal static bool IsHwControllerBaselineClean(out string diag, out bool staleStatePresent)
        {
            int vigem = 0, pid1901 = 0;
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VID_045E%PID_028E%' OR PNPDeviceID LIKE '%VID_0DB0%PID_1901%'"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string id = (mo["PNPDeviceID"] as string) ?? "";
                        if (id.IndexOf("VID_045E", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            id.IndexOf("PID_028E", StringComparison.OrdinalIgnoreCase) >= 0)
                            vigem++;
                        else if (id.IndexOf("PID_1901", StringComparison.OrdinalIgnoreCase) >= 0)
                            pid1901++;
                    }
                }
            }
            catch (Exception ex) { Logger.Debug($"ControllerState: baseline WMI query failed: {ex.Message}"); }

            bool hidHideKnown = false, cloaking = false;
            int blocked = 0;
            try
            {
                var suppression = controllerEmulationManager?.SuppressionManager;
                if (suppression != null)
                    hidHideKnown = suppression.TryGetState(out cloaking, out blocked);
            }
            catch (Exception ex) { Logger.Debug($"ControllerState: baseline HidHide probe failed: {ex.Message}"); }

            bool hidHideHiding = hidHideKnown && (cloaking || blocked > 0);
            bool clean = pid1901 > 0 && vigem == 0 && !hidHideHiding;
            // Stale = something a Stop()/unhide cycle can remove. pid1901=0 alone is NOT stale — the
            // device is simply not enumerated yet — so it doesn't justify the self-heal.
            staleStatePresent = vigem > 0 || hidHideHiding;
            diag = $"pid1901={pid1901} vigem={vigem} hidHideKnown={hidHideKnown} cloaking={cloaking} blocked={blocked}";
            return clean;
        }

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
                        // Count DISTINCT Xbox360 controllers, not every PnP node. A single pad
                        // (ViGEm *or* VIIPER xbox360) enumerates as a bare USB parent
                        // (…PID_028E\<serial>) PLUS child interfaces (…PID_028E&IG_0x…, HID\…),
                        // so counting every node inflates one pad to 2-3. Only the bare parent —
                        // a backslash right after PID_028E — counts as one controller. (Diagnosed
                        // 2026-06-15: a single VIIPER xbox360 device showed up as 3 nodes → "2 active".)
                        if (id.IndexOf("VID_045E", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            id.IndexOf("PID_028E\\", StringComparison.OrdinalIgnoreCase) >= 0)
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

            // ── VIIPER virtual devices (helper-internal truth) ────────────────
            // A VIIPER xbox360 device presents as the SAME VID_045E&PID_028E as a ViGEm pad, so
            // the WMI count above can't tell them apart. The helper knows its own mounts exactly.
            int viiper = 0;
            string viiperType = "";
            try
            {
                Labs.ClawButtonMonitor cbm;
                lock (clawButtonMonitorLock) cbm = clawButtonMonitor;
                if (cbm != null && cbm.ViiperMounted)
                {
                    viiper++;
                    viiperType = cbm.ViiperActiveDeviceType ?? "";
                }
                // Legion path (non-Claw) — its own VIIPER manager.
                if (viiperEmulationManager != null && viiperEmulationManager.IsRunning)
                    viiper++;
            }
            catch (Exception ex) { Logger.Debug($"ControllerState: VIIPER probe failed: {ex.Message}"); }

            // Don't double-count a VIIPER xbox360 device as a ViGEm pad (shared VID/PID).
            if (viiperType == "xbox360" && vigem > 0)
                vigem--;

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
            else if ((vigem > 0 || viiper > 0) && hidHideHiding)
                state = 1; // a virtual pad (ViGEm or VIIPER) is present and the physical one is hidden
            else if (vigem == 0 && viiper == 0 && pid1901 > 0 && hidHideKnown && !cloaking && blocked == 0)
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
            return $"{state}|{vigem}|{pid1901}|{pid1902}|{blockedOut}|{xinput}|{viiper}|{viiperType}";
        }
    }
}
