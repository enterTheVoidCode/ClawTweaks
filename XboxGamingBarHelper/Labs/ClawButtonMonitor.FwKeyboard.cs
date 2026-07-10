using System;
using System.Collections.Generic;
using System.Threading;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Firmware button → keyboard remapping for the MSI Claw (A2VM only).
    ///
    /// Unlike the software keyboard path (KeyboardChordCallback → InputInjector, VK-only, invisible to
    /// DirectInput/RawInput games), the controller firmware turns a stored keyboard code into a REAL
    /// HID keyboard event on the composite keyboard interface — so it is seen inside games. This is an
    /// OPTIONAL backend (user toggle in the Controller status), gated to A2VM, running ALONGSIDE the
    /// existing software path (which stays for tiles/hotkeys/actions that are not bound to a physical
    /// button). Only button-bound keyboard shortcuts (global + per-game controller profile) can use it,
    /// because the firmware only emits a key when its mapped physical button is pressed.
    ///
    /// Protocol / addresses / keycode table: reverse_engineered/RE_MSI_ButtonRemap.md
    /// ("Button → KEYBOARD remap"). Verified on-device 2026-07-10 (A2VM fw 0x229). Coexists with
    /// ViGEm/Viiper + HidHide (firmware emits on the keyboard collection, which HidHide does not hide).
    /// </summary>
    internal partial class ClawButtonMonitor
    {
        // ── Capability + mode ────────────────────────────────────────────────────
        // Capable: DeviceType.MSIClaw && model contains "A2VM" (set from Program via DeviceInfo).
        // A1M/A8/Claw 8 EX stay false — their EEPROM layout is not verified.
        private volatile bool _fwKeyboardCapable;
        // User toggle (Controller status). When false, the software path handles everything (default).
        private volatile bool _fwKeyboardModeEnabled;

        // Debounced writer (real EEPROM writes → finite endurance; coalesce a burst of profile/UI
        // changes into one flush ~400ms after the last change), mirroring WriteVibrationCeilingToFw.
        private const int FwKbWriteDebounceMs = 400;
        private readonly object _fwKbLock = new object();
        private Timer _fwKbWriteTimer;
        // Desired keyboard map: canonical button name → HID usage codes (modifiers first). Empty/absent
        // = that button is NOT keyboard-remapped (its slot is written back to default so it works as a
        // normal button → ViGEm/Viiper). Assembled by the profile-application code (Step 4).
        private readonly Dictionary<string, int[]> _fwKbDesired =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        // Last payload actually written per slot, so we never re-write an unchanged slot (stresses the
        // EEPROM and, on triggers, collides with the 0x26 poll if we tried to read back). Key = button.
        private readonly Dictionary<string, byte[]> _fwKbLastWritten =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether firmware keyboard remap is available on this device (A2VM only).</summary>
        public bool FirmwareKeyboardCapable => _fwKeyboardCapable;

        /// <summary>Whether the user has switched keyboard remaps to the firmware backend.</summary>
        public bool FirmwareKeyboardModeEnabled => _fwKeyboardModeEnabled;

        /// <summary>
        /// Set by the helper startup from DeviceInfo (A2VM only). No-op writes; just gates the feature.
        /// </summary>
        public void SetFirmwareKeyboardCapable(bool capable)
        {
            _fwKeyboardCapable = capable;
            Logger.Info($"ClawButtonMonitor: FW keyboard remap capable = {capable}");
        }

        /// <summary>
        /// Switch the keyboard-remap backend. Enabling re-applies the current desired map to the
        /// firmware; disabling clears ALL firmware slots back to their defaults so no key lingers.
        /// Ignored if the device is not capable.
        /// </summary>
        public void SetFirmwareKeyboardMode(bool enabled)
        {
            if (!_fwKeyboardCapable)
            {
                if (enabled) Logger.Warn("ClawButtonMonitor: FW keyboard mode requested but device is not capable — ignored.");
                _fwKeyboardModeEnabled = false;
                return;
            }

            bool changed = _fwKeyboardModeEnabled != enabled;
            _fwKeyboardModeEnabled = enabled;
            Logger.Info($"ClawButtonMonitor: FW keyboard mode = {enabled}");
            if (!changed) return;

            if (enabled)
            {
                // Rebuild the desired map from the CURRENT profile config (24-button swaps + M1/M2),
                // then flush. Using Recompute (not just a flush) makes enabling robust at boot, where
                // the mode may be applied before/after the profile's button mappings are pushed.
                RecomputeFirmwareKeyboardDesired();
            }
            else
            {
                // Full reset: every face+paddle slot → default (also undoes stray MSI Center M remaps).
                ClearAllFirmwareKeyboardSlots();
            }
        }

        /// <summary>
        /// Declare the COMPLETE set of active button → keyboard mappings for the current profile
        /// (global or per-game). Codes are the widget's HID usage codes (same as
        /// <see cref="ConfigureBackButtonMapping"/> Type=1 values). Buttons absent from
        /// <paramref name="desired"/> are treated as "no keyboard remap" (slot → default). Diffed
        /// against what is already on the firmware; only changed slots are written (debounced).
        /// Safe to call on every profile apply / game start / game end.
        /// </summary>
        public void SyncFirmwareKeyboardMap(IReadOnlyDictionary<string, int[]> desired)
        {
            lock (_fwKbLock)
            {
                _fwKbDesired.Clear();
                if (desired != null)
                {
                    foreach (var kv in desired)
                    {
                        if (kv.Value != null && kv.Value.Length > 0)
                        {
                            _fwKbDesired[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            ScheduleFwKeyboardFlush();
        }

        // Per-source keyboard codes (HID usage). M1/M2 come from ConfigureBackButtonMapping; the 16
        // standard buttons come from the 24-button swap table (_gamepadSwaps). Merged by
        // RecomputeFirmwareKeyboardDesired into the single desired map on every profile change.
        private volatile int[] _fwKbM1Codes;
        private volatile int[] _fwKbM2Codes;

        /// <summary>
        /// Called by <see cref="ConfigureBackButtonMapping"/> when an M1/M2 keyboard mapping changes:
        /// codes = HID usage codes for Type=1, or null when the paddle is not a keyboard mapping.
        /// </summary>
        private void UpdateFirmwareKeyboardPaddle(string button, int[] codes)
        {
            if (string.Equals(button, "M1", StringComparison.OrdinalIgnoreCase)) _fwKbM1Codes = codes;
            else if (string.Equals(button, "M2", StringComparison.OrdinalIgnoreCase)) _fwKbM2Codes = codes;
            RecomputeFirmwareKeyboardDesired();
        }

        /// <summary>
        /// Rebuilds the complete desired button→keyboard map from the two live sources (the 24-button
        /// swap table + the M1/M2 paddle config) and hands it to the diff-writer. Called on every
        /// profile change (global / per-game) — the same events that already rebuild the swap table —
        /// so the firmware follows the active profile. The changed-cache in the writer skips slots that
        /// did not actually change, so an unchanged profile costs no EEPROM writes.
        /// </summary>
        private void RecomputeFirmwareKeyboardDesired()
        {
            if (!_fwKeyboardCapable) return;

            var merged = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

            // 16 standard buttons from the current swap table (keyboard-target entries only).
            GamepadSwapEntry[] swaps = _gamepadSwaps;
            if (swaps != null)
            {
                foreach (GamepadSwapEntry e in swaps)
                {
                    if (e != null && e.IsKeyboard && !string.IsNullOrEmpty(e.SourceButtonName)
                        && e.KeyboardHidCodes != null && e.KeyboardHidCodes.Length > 0)
                    {
                        merged[e.SourceButtonName] = e.KeyboardHidCodes;
                    }
                }
            }

            // M1/M2 paddles.
            if (_fwKbM1Codes != null && _fwKbM1Codes.Length > 0) merged["M1"] = _fwKbM1Codes;
            if (_fwKbM2Codes != null && _fwKbM2Codes.Length > 0) merged["M2"] = _fwKbM2Codes;

            SyncFirmwareKeyboardMap(merged);
        }

        /// <summary>
        /// True when the given canonical button currently has a firmware keyboard remap active — used
        /// by the poll loop to suppress the software keyboard injection for that button (no double-fire).
        /// </summary>
        public bool IsButtonFirmwareKeyboardMapped(string button)
        {
            if (!_fwKeyboardCapable || !_fwKeyboardModeEnabled || string.IsNullOrEmpty(button)) return false;
            lock (_fwKbLock)
            {
                return _fwKbDesired.TryGetValue(button, out int[] codes) && codes != null && codes.Length > 0
                       && FwSlots.ContainsKey(button);
            }
        }

        private void ScheduleFwKeyboardFlush()
        {
            lock (_fwKbLock)
            {
                _fwKbWriteTimer?.Dispose();
                _fwKbWriteTimer = new Timer(_ => FlushFirmwareKeyboardMap(), null, FwKbWriteDebounceMs, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Schedules a FULL reset: every face+paddle slot → default (button does its own function).
        /// Deliberately does NOT clear <see cref="_fwKbDesired"/> so a later re-enable restores the
        /// user's configured remaps without waiting for a fresh profile push.
        /// </summary>
        private void ClearAllFirmwareKeyboardSlots()
        {
            lock (_fwKbLock)
            {
                _fwKbWriteTimer?.Dispose();
                _fwKbWriteTimer = new Timer(_ => FlushFirmwareKeyboardMap(force: true), null, FwKbWriteDebounceMs, Timeout.Infinite);
            }
        }

        /// <summary>
        /// While FW mode is ON, ClawTweaks OWNS the firmware face+paddle button map: every such slot is
        /// driven to either its configured keyboard remap or its DEFAULT ("button does its own thing").
        /// Writing defaults to the unmapped slots is deliberate — it restores buttons to normal and
        /// undoes any stray MSI Center M firmware remap the user cannot otherwise reach. While OFF (or
        /// <paramref name="force"/>), every slot is driven to default = a full reset to normal.
        ///
        /// Triggers (L2/R2) are NOT in <see cref="FwSlots"/> and are never touched: their analog-restore
        /// bytes are unverified and writing a remap marker + edge-deadzone kills the axis. LT/RT keyboard
        /// remaps stay on the software path until that restore is debugged on-device.
        ///
        /// Only slots whose bytes actually change are written (per-slot cache), so an unchanged profile
        /// costs no EEPROM writes.
        /// </summary>
        private void FlushFirmwareKeyboardMap(bool force = false)
        {
            if (!_fwKeyboardCapable) return;
            bool modeOn = _fwKeyboardModeEnabled;
            if (!modeOn && !force) return;

            // Snapshot desired under the lock; do the (slow) HID writes outside it.
            Dictionary<string, int[]> desiredSnapshot;
            lock (_fwKbLock)
            {
                desiredSnapshot = new Dictionary<string, int[]>(_fwKbDesired, StringComparer.OrdinalIgnoreCase);
            }

            int writes = 0;
            foreach (var kv in FwSlots)   // face + paddle only (triggers are excluded from FwSlots)
            {
                string button = kv.Key;
                FwSlot slot = kv.Value;

                byte[] payload;
                if (modeOn && desiredSnapshot.TryGetValue(button, out int[] hidCodes) &&
                    TryTranslateCodes(hidCodes, out byte[] msiCodes))
                {
                    payload = BuildSlotPayload(slot, msiCodes);   // configured keyboard remap
                }
                else
                {
                    payload = BuildSlotPayload(slot, null);       // default = button does its own function
                }

                byte[] last;
                lock (_fwKbLock) { _fwKbLastWritten.TryGetValue(button, out last); }
                if (last != null && PayloadsEqual(last, payload)) continue;   // unchanged → don't stress EEPROM

                bool ok = SendRawCmd(BuildSlotWriteFrame(slot, payload));
                Logger.Info($"ClawButtonMonitor: FW slot {button} @0x{slot.Address:X4} len={payload.Length} ← {ToHex(payload)} (ok={ok})");
                if (ok)
                {
                    lock (_fwKbLock) { _fwKbLastWritten[button] = payload; }
                    writes++;
                    Thread.Sleep(30);
                }
                else
                {
                    Logger.Warn($"ClawButtonMonitor: FW keyboard write failed for {button} (addr 0x{slot.Address:X4}).");
                }
            }

            if (writes > 0)
            {
                bool syncOk = SendRawCmd(BuildSyncToRomCmd());
                Logger.Info($"ClawButtonMonitor: FW keyboard map flushed ({writes} slot(s){(force ? ", full reset" : "")}, sync={syncOk}).");
            }
        }

        private static string ToHex(byte[] b)
        {
            if (b == null) return "(null)";
            var sb = new System.Text.StringBuilder(b.Length * 3);
            for (int i = 0; i < b.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(b[i].ToString("X2")); }
            return sb.ToString();
        }

        // ── Slot model ───────────────────────────────────────────────────────────
        private enum FwSlotClass { Paddle, Face, Trigger }

        private struct FwSlot
        {
            public ushort Address;
            public FwSlotClass Class;
            public byte OwnCode;   // face default = 00 0C <ownCode>; paddle default byte for M1/M2
        }

        // Paddle default bytes (M1=0x11, M2=0x12) per RE_MSI_ButtonRemap.md.
        private const byte M1_DEFAULT_CODE = 0x11;
        private const byte M2_DEFAULT_CODE = 0x12;
        // Trigger deadzone / edge-deadzone bytes MSI ships (R2→Alt+Tab captured 06 0C 03 60 …).
        private const byte TRIGGER_DZ = 0x03;
        private const byte TRIGGER_EDZ = 0x60;

        // Canonical widget button name → firmware slot. Start/Select (View/Menu) are intentionally
        // absent: their slots were not reverse-engineered, so they always fall back to software even
        // in FW mode. Names match TryGetSwapSource (generic swaps) + ConfigureBackButtonMapping (M1/M2).
        private static readonly Dictionary<string, FwSlot> FwSlots =
            new Dictionary<string, FwSlot>(StringComparer.OrdinalIgnoreCase)
            {
                { "M1",        new FwSlot { Address = 0x00BD, Class = FwSlotClass.Paddle,  OwnCode = M1_DEFAULT_CODE } },
                { "M2",        new FwSlot { Address = 0x0166, Class = FwSlotClass.Paddle,  OwnCode = M2_DEFAULT_CODE } },
                { "DPadUp",    new FwSlot { Address = 0x003B, Class = FwSlotClass.Face,    OwnCode = 1  } },
                { "DPadDown",  new FwSlot { Address = 0x0043, Class = FwSlotClass.Face,    OwnCode = 2  } },
                { "DPadLeft",  new FwSlot { Address = 0x004B, Class = FwSlotClass.Face,    OwnCode = 3  } },
                { "DPadRight", new FwSlot { Address = 0x0053, Class = FwSlotClass.Face,    OwnCode = 4  } },
                { "LB",        new FwSlot { Address = 0x005B, Class = FwSlotClass.Face,    OwnCode = 5  } },
                { "RB",        new FwSlot { Address = 0x0063, Class = FwSlotClass.Face,    OwnCode = 6  } },
                { "LSClick",   new FwSlot { Address = 0x006B, Class = FwSlotClass.Face,    OwnCode = 7  } },
                { "RSClick",   new FwSlot { Address = 0x0073, Class = FwSlotClass.Face,    OwnCode = 8  } },
                { "A",         new FwSlot { Address = 0x007B, Class = FwSlotClass.Face,    OwnCode = 9  } },
                { "B",         new FwSlot { Address = 0x0083, Class = FwSlotClass.Face,    OwnCode = 10 } },
                { "X",         new FwSlot { Address = 0x008B, Class = FwSlotClass.Face,    OwnCode = 11 } },
                { "Y",         new FwSlot { Address = 0x0093, Class = FwSlotClass.Face,    OwnCode = 12 } },
                // NOTE: L2/R2 triggers (0x020D/0x021C, class Trigger) are intentionally NOT listed —
                // their analog-restore bytes are not verified, and writing the remap marker + a default
                // edge-deadzone kills the analog axis. LT/RT keyboard remaps therefore stay on the
                // software path. FwSlotClass.Trigger + BuildSlotPayload's Trigger case are kept for when
                // a verified restore is available.
            };

        /// <summary>
        /// Builds the variable-length slot payload (the bytes after the address/len header), matching
        /// the byte formats that are proven/observed to fire:
        ///   • Paddle (M1/M2): raw codes in an 8-byte zeroed block — exactly the on-device-PROVEN probe
        ///     (Write-ButtonMappingTest.ps1) that fired. Default = the paddle's own default byte
        ///     (0x11/0x12) in an 8-byte block. (MSI itself uses a 55-byte block + companion zeroing;
        ///     the short block fires and is what we verified, so we use it.)
        ///   • Face/dpad/shoulder/stick-click: EXACTLY MSI's observed `04 0C <codes>` (len 2+N, NO
        ///     padding — MSI's Y→Win+D was len 4 = `04 0C 75 5E`). Default = `00 0C <ownCode>` (len 3).
        /// </summary>
        private static byte[] BuildSlotPayload(FwSlot slot, byte[] msiCodes)
        {
            switch (slot.Class)
            {
                case FwSlotClass.Paddle:
                {
                    byte[] p = new byte[8];
                    if (msiCodes != null && msiCodes.Length > 0)
                        Array.Copy(msiCodes, p, Math.Min(msiCodes.Length, p.Length));   // raw codes, rest zeroed
                    else
                        p[0] = slot.OwnCode;                                            // default byte, rest zeroed
                    return p;
                }
                case FwSlotClass.Face:
                {
                    if (msiCodes != null && msiCodes.Length > 0)
                    {
                        byte[] p = new byte[2 + msiCodes.Length];   // 04 0C <codes>, exactly like MSI
                        p[0] = 0x04; p[1] = 0x0C;
                        Array.Copy(msiCodes, 0, p, 2, msiCodes.Length);
                        return p;
                    }
                    return new byte[] { 0x00, 0x0C, slot.OwnCode };  // 00 0C <ownCode> default
                }
                case FwSlotClass.Trigger:
                {
                    byte[] p = new byte[9];
                    p[0] = 0x06;         // trigger flag
                    p[1] = 0x0C;         // const marker
                    p[2] = TRIGGER_DZ;
                    p[3] = TRIGGER_EDZ;
                    if (msiCodes != null && msiCodes.Length > 0)
                    {
                        Array.Copy(msiCodes, 0, p, 4, Math.Min(msiCodes.Length, p.Length - 4));
                    }
                    // else: codes zeroed → plain analog trigger restored.
                    return p;
                }
                default:
                    return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Wraps a slot payload in the 64-byte 0x21 write frame:
        /// <c>0F 00 00 3C 21 01 &lt;addrHi&gt; &lt;addrLo&gt; &lt;len&gt; &lt;payload…&gt;</c>.
        /// </summary>
        private static byte[] BuildSlotWriteFrame(FwSlot slot, byte[] payload)
        {
            byte[] cmd = new byte[64];
            cmd[0] = REPORT_ID; cmd[1] = 0x00; cmd[2] = 0x00; cmd[3] = 0x3C;
            cmd[4] = 0x21; cmd[5] = 0x01;
            cmd[6] = (byte)(slot.Address >> 8);
            cmd[7] = (byte)(slot.Address & 0xFF);
            cmd[8] = (byte)payload.Length;
            Array.Copy(payload, 0, cmd, 9, Math.Min(payload.Length, cmd.Length - 9));
            return cmd;
        }

        private static bool PayloadsEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ── HID usage → MSI firmware keycode ─────────────────────────────────────
        /// <summary>
        /// Translates a full chord of the widget's HID usage codes into MSI physical-layout firmware
        /// codes (0x32–0x83, RE_MSI_ButtonRemap.md). Returns false if ANY code has no MSI equivalent
        /// (e.g. PrtSc/Pause/volume) — the caller then keeps that button on the software path instead
        /// of writing a partial chord. Max 5 codes (firmware MapValue slots).
        /// </summary>
        private static bool TryTranslateCodes(int[] hidCodes, out byte[] msiCodes)
        {
            msiCodes = null;
            if (hidCodes == null || hidCodes.Length == 0) return false;
            var list = new List<byte>(hidCodes.Length);
            foreach (int hid in hidCodes)
            {
                if (!TryHidToMsiCode(hid, out byte msi)) return false;
                list.Add(msi);
                if (list.Count >= 5) break; // firmware holds up to 5 keys
            }
            if (list.Count == 0) return false;
            msiCodes = list.ToArray();
            return true;
        }

        private static bool TryHidToMsiCode(int hid, out byte msi)
        {
            msi = 0;
            // Letters A–Z (HID 0x04–0x1D) → positional MSI code via the char map.
            if (hid >= 0x04 && hid <= 0x1D) return TryCharToMsiCode((char)('A' + (hid - 0x04)), out msi);
            // Digits 1–9 (HID 0x1E–0x26) → MSI 0x40–0x48; 0 (HID 0x27) → 0x49.
            if (hid >= 0x1E && hid <= 0x26) { msi = (byte)(0x40 + (hid - 0x1E)); return true; }
            if (hid == 0x27) { msi = 0x49; return true; }
            // F1–F12 (HID 0x3A–0x45) → MSI 0x33–0x3E.
            if (hid >= 0x3A && hid <= 0x45) { msi = (byte)(0x33 + (hid - 0x3A)); return true; }

            switch (hid)
            {
                case 0x28: msi = 0x67; return true; // Enter
                case 0x29: msi = 0x32; return true; // Esc
                case 0x2C: msi = 0x77; return true; // Space
                case 0x2B: msi = 0x4D; return true; // Tab
                case 0x2A: msi = 0x4C; return true; // Backspace
                case 0x2D: msi = 0x4A; return true; // - (minus)
                case 0x2E: msi = 0x4B; return true; // = (equals)
                case 0x2F: msi = 0x58; return true; // [
                case 0x30: msi = 0x59; return true; // ]
                case 0x31: msi = 0x5A; return true; // backslash
                case 0x33: msi = 0x65; return true; // ;
                case 0x34: msi = 0x66; return true; // '
                case 0x35: msi = 0x3F; return true; // ` (backquote)
                case 0x36: msi = 0x70; return true; // ,
                case 0x37: msi = 0x71; return true; // .
                case 0x38: msi = 0x72; return true; // /
                case 0x39: msi = 0x5B; return true; // CapsLock
                case 0x49: msi = 0x7A; return true; // Insert
                case 0x4A: msi = 0x7B; return true; // Home
                case 0x4B: msi = 0x7C; return true; // PageUp
                case 0x4C: msi = 0x7D; return true; // Delete
                case 0x4D: msi = 0x7E; return true; // End
                case 0x4E: msi = 0x7F; return true; // PageDown
                case 0x4F: msi = 0x83; return true; // Right
                case 0x50: msi = 0x82; return true; // Left
                case 0x51: msi = 0x81; return true; // Down
                case 0x52: msi = 0x80; return true; // Up
                case 0xE0: msi = 0x74; return true; // LCtrl
                case 0xE1: msi = 0x68; return true; // LShift
                case 0xE2: msi = 0x76; return true; // LAlt
                case 0xE3: msi = 0x75; return true; // LWin (single Win in firmware)
                case 0xE4: msi = 0x79; return true; // RCtrl
                case 0xE5: msi = 0x73; return true; // RShift
                case 0xE6: msi = 0x78; return true; // RAlt
                case 0xE7: msi = 0x75; return true; // RWin → same Win code
                default: return false;              // no MSI equivalent → keep software
            }
        }

        private static bool TryCharToMsiCode(char c, out byte msi)
        {
            switch (char.ToUpperInvariant(c))
            {
                // QWERTY top letter row
                case 'Q': msi = 0x4E; return true;
                case 'W': msi = 0x4F; return true;
                case 'E': msi = 0x50; return true;
                case 'R': msi = 0x51; return true;
                case 'T': msi = 0x52; return true;
                case 'Y': msi = 0x53; return true;
                case 'U': msi = 0x54; return true;
                case 'I': msi = 0x55; return true;
                case 'O': msi = 0x56; return true;
                case 'P': msi = 0x57; return true;
                // Home row
                case 'A': msi = 0x5C; return true;
                case 'S': msi = 0x5D; return true;
                case 'D': msi = 0x5E; return true;
                case 'F': msi = 0x5F; return true;
                case 'G': msi = 0x60; return true;
                case 'H': msi = 0x61; return true;
                case 'J': msi = 0x62; return true;
                case 'K': msi = 0x63; return true;
                case 'L': msi = 0x64; return true;
                // Bottom row
                case 'Z': msi = 0x69; return true;
                case 'X': msi = 0x6A; return true;
                case 'C': msi = 0x6B; return true;
                case 'V': msi = 0x6C; return true;
                case 'B': msi = 0x6D; return true;
                case 'N': msi = 0x6E; return true;
                case 'M': msi = 0x6F; return true;
                default: msi = 0; return false;
            }
        }
    }
}
