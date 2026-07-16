using System;
using System.Collections.Generic;
using System.Threading;
using XboxGamingBarHelper.Devices.Libraries.Legion;   // RemapAction (gamepad→gamepad firmware targets)

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
        // Capable: any device MSIClawConfig matches — A2VM (Lunar Lake) and the Claw 8 EX
        // (CG3EM, Panther Lake), which shares the A2VM controller EEPROM 1:1 (set from Program
        // via DeviceInfo). A1M/A8 stay false — their EEPROM layout is not verified.
        private volatile bool _fwKeyboardCapable;
        // User toggle (Controller status). When false, the software path handles everything (default).
        private volatile bool _fwKeyboardModeEnabled;

        // Debounced writer (real EEPROM writes → finite endurance; coalesce a burst of profile/UI
        // changes into one flush ~400ms after the last change), mirroring WriteVibrationCeilingToFw.
        private const int FwKbWriteDebounceMs = 400;
        private readonly object _fwKbLock = new object();
        private Timer _fwKbWriteTimer;
        // Boot race / transient re-enumeration: SetFirmwareKeyboardMode(true) schedules a flush at
        // startup, but the monitor acquires the vendor command interface (_cmdDevice) ~2s later, so the
        // first flush writes against a null device and every write silently fails (ok=False) — the map
        // never lands (UI shows it, firmware doesn't). Retry the flush (bounded) until the channel is up
        // and the writes stick. Cached (already-written) slots are skipped on retry, so no EEPROM storm.
        private int _fwKbFlushRetries;
        private const int FwKbMaxFlushRetries = 10;
        private const int FwKbRetryDelayMs = 800;
        // Desired keyboard map: canonical button name → HID usage codes (modifiers first). Empty/absent
        // = that button is NOT keyboard-remapped (its slot is written back to default so it works as a
        // normal button → ViGEm/Viiper). Assembled by the profile-application code (Step 4).
        private readonly Dictionary<string, int[]> _fwKbDesired =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        // Desired gamepad→gamepad map: source canonical button name → target physical button code
        // (1..12, the FwSlot.OwnCode of the target). Same firmware slot/format as a keyboard remap
        // (face `04 0C <code>`, paddle `01 04 0C <code>`) but the code is a button code, not a key.
        // Only applied in Hardware Controller mode (virtual pad off); in virtual mode the software
        // swap path owns gamepad→gamepad, so this stays unwritten there to avoid a double-remap.
        private readonly Dictionary<string, byte> _fwGpDesired =
            new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
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
        /// Enables/disables writing gamepad→gamepad remaps to firmware. Driven by the controller mode
        /// (Hardware = true, Virtual = false). On change, re-evaluates + re-flushes the map so a mode
        /// switch takes effect immediately (this is what clears X→A / M1→A when returning to Virtual).
        /// </summary>
        public void SetFirmwareGamepadEnabled(bool enabled)
        {
            if (!_fwKeyboardCapable) { _fwGamepadEnabled = false; return; }
            if (_fwGamepadEnabled == enabled) return;
            _fwGamepadEnabled = enabled;
            Logger.Info($"ClawButtonMonitor: FW gamepad→gamepad remap = {enabled}");
            RecomputeFirmwareKeyboardDesired();   // re-evaluate desired + schedule flush
        }

        /// <summary>
        /// Re-asserts the firmware map after the virtual monitor's Start, which rewrites the M1/M2 paddle
        /// slots to their defaults (HC OpenClawInterfaces init) and clobbers any firmware paddle remap.
        /// Invalidates just the M1/M2 write-cache (their firmware bytes changed underneath us) so the
        /// flush re-writes them; face slots are untouched by that init, so the cache keeps them skipped.
        /// </summary>
        public void ReassertFirmwareMapAfterStart()
        {
            if (!_fwKeyboardCapable) return;
            lock (_fwKbLock)
            {
                _fwKbLastWritten.Remove("M1");
                _fwKbLastWritten.Remove("M2");
            }
            Logger.Info("ClawButtonMonitor: re-asserting FW map after monitor start (M1/M2 paddle repair)");
            RecomputeFirmwareKeyboardDesired();
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
        // Per-paddle gamepad→gamepad target code (1..12; 0 = none). M1/M2 take the ConfigureBackButtonMapping
        // path, not the 24-button swap table, so their gamepad targets need their own source (mirrors the
        // keyboard M1/M2 codes above). Merged into _fwGpDesired by RecomputeFirmwareKeyboardDesired.
        private volatile byte _fwGpM1Code;
        private volatile byte _fwGpM2Code;
        // Whether gamepad→gamepad remaps should be written to firmware. Set explicitly by the helper
        // from the CONTROLLER MODE (Hardware = true, Virtual = false) — NOT from `_running`, which flips
        // asynchronously ~6 s after a Start and raced the flush. In Virtual mode the software swap path
        // owns gamepad→gamepad, so firmware gamepad targets are cleared to default there.
        private volatile bool _fwGamepadEnabled;

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
        /// Called by <see cref="ConfigureBackButtonMapping"/> when an M1/M2 gamepad mapping changes:
        /// action = the target XInput button for Type=0, or Disabled when the paddle is not a gamepad
        /// mapping. Only single-button targets are firmware-expressible (sticks/triggers/Guide clear it).
        /// </summary>
        private void UpdateFirmwareGamepadPaddle(string button, RemapAction action)
        {
            byte code = TryGamepadActionToTargetCode(action, out byte c) ? c : (byte)0;
            if (string.Equals(button, "M1", StringComparison.OrdinalIgnoreCase)) _fwGpM1Code = code;
            else if (string.Equals(button, "M2", StringComparison.OrdinalIgnoreCase)) _fwGpM2Code = code;
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

            // Gamepad→gamepad targets from the same swap table (single-button targets only, e.g. A→Y).
            // Multi-action, stick-direction, trigger, and Guide/Desktop/Page targets can't be expressed
            // as a firmware button code and are skipped here (they fall back to software in virtual mode).
            var mergedGp = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            if (swaps != null)
            {
                foreach (GamepadSwapEntry e in swaps)
                {
                    if (e == null || e.IsKeyboard || e.IsGuide || string.IsNullOrEmpty(e.SourceButtonName)) continue;
                    if (e.Actions == null || e.Actions.Length != 1) continue;   // firmware = exactly one target
                    if (!TryGamepadActionToTargetCode(e.Actions[0], out byte targetCode)) continue;
                    // Source must be a firmware slot we can write (face/paddle; triggers as source are keyboard-only).
                    if (!FwSlots.TryGetValue(e.SourceButtonName, out FwSlot srcSlot) || srcSlot.Class == FwSlotClass.Trigger) continue;
                    mergedGp[e.SourceButtonName] = targetCode;
                }
            }

            // M1/M2 paddle gamepad targets (from ConfigureBackButtonMapping, not the swap table).
            if (_fwGpM1Code != 0) mergedGp["M1"] = _fwGpM1Code;
            if (_fwGpM2Code != 0) mergedGp["M2"] = _fwGpM2Code;

            lock (_fwKbLock)
            {
                _fwGpDesired.Clear();
                foreach (var kv in mergedGp) _fwGpDesired[kv.Key] = kv.Value;
            }

            SyncFirmwareKeyboardMap(merged);
        }

        /// <summary>
        /// Maps an XInput remap target to the MSI firmware physical button code (1..12), matching the
        /// <see cref="FwSlots"/> OwnCode numbering. Only the 12 discrete buttons the firmware can target
        /// are expressible; sticks/triggers/Guide/etc. return false.
        /// </summary>
        private static bool TryGamepadActionToTargetCode(RemapAction action, out byte code)
        {
            switch (action)
            {
                case RemapAction.DpadUp:          code = 1;  return true;
                case RemapAction.DpadDown:        code = 2;  return true;
                case RemapAction.DpadLeft:        code = 3;  return true;
                case RemapAction.DpadRight:       code = 4;  return true;
                case RemapAction.LeftBumper:      code = 5;  return true;
                case RemapAction.RightBumper:     code = 6;  return true;
                case RemapAction.LeftStickClick:  code = 7;  return true;
                case RemapAction.RightStickClick: code = 8;  return true;
                case RemapAction.A:               code = 9;  return true;
                case RemapAction.B:               code = 10; return true;
                case RemapAction.X:               code = 11; return true;
                case RemapAction.Y:               code = 12; return true;
                default:                          code = 0;  return false;
            }
        }

        // Reverse of TryGamepadActionToTargetCode: firmware button code (1..12) → friendly name, for the
        // "Re-read firmware" report so a gamepad→gamepad remap shows as "A → Y".
        private static string TargetCodeToButtonName(byte code)
        {
            switch (code)
            {
                case 1:  return "D-Pad Up";
                case 2:  return "D-Pad Down";
                case 3:  return "D-Pad Left";
                case 4:  return "D-Pad Right";
                case 5:  return "LB";
                case 6:  return "RB";
                case 7:  return "L3";
                case 8:  return "R3";
                case 9:  return "A";
                case 10: return "B";
                case 11: return "X";
                case 12: return "Y";
                default: return null;
            }
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
                _fwKbFlushRetries = 0;   // fresh user/profile-triggered flush → full retry budget
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
                _fwKbFlushRetries = 0;
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
        /// Triggers (L2/R2) are included: remap = `06 0C 03 60 <codes>`, default (analog) =
        /// `02 0C 03 60 <ownCode>` (verified on-device). Their dead-zone bytes (0x03/0x60) are MSI's
        /// defaults, so a custom trigger dead-zone is reset when a trigger is (un)mapped.
        ///
        /// Only slots whose bytes actually change are written (per-slot cache), so an unchanged profile
        /// costs no EEPROM writes.
        /// </summary>
        private void FlushFirmwareKeyboardMap(bool force = false)
        {
            if (!_fwKeyboardCapable) return;
            bool modeOn = _fwKeyboardModeEnabled;
            if (!modeOn && !force) return;

            // Firmware-only channel (HW controller mode): the virtual monitor loop isn't running, so
            // _cmdDevice was never acquired by Start(). The vendor command interface (PID_1901/0xFFA0)
            // is present in ALL firmware modes, so acquire it on demand here — this is what lets the
            // firmware remaps land in Hardware Controller mode without mounting ViGEm/HidHide.
            if (_cmdDevice == null)
            {
                try { _cmdDevice = FindCommandDevice(); }
                catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: FW-only command channel probe threw: {ex.Message}"); }
            }
            // Boot race: the vendor command interface may still not be acquired yet (transient
            // re-enumeration). Defer + retry until it is, instead of writing against a null device.
            if (_cmdDevice == null)
            {
                RescheduleFwKeyboardFlush(force, "command channel not ready");
                return;
            }

            // Snapshot desired under the lock; do the (slow) HID writes outside it.
            Dictionary<string, int[]> desiredSnapshot;
            Dictionary<string, byte> gamepadSnapshot;
            lock (_fwKbLock)
            {
                desiredSnapshot = new Dictionary<string, int[]>(_fwKbDesired, StringComparer.OrdinalIgnoreCase);
                gamepadSnapshot = new Dictionary<string, byte>(_fwGpDesired, StringComparer.OrdinalIgnoreCase);
            }

            // Gamepad→gamepad firmware remaps only apply in Hardware Controller mode (see field comment).
            // In virtual mode the software swap path owns them, so they are cleared to default here.
            // Keyboard remaps apply in both modes as before.
            bool applyGamepad = modeOn && _fwGamepadEnabled;

            int writes = 0;
            int failures = 0;
            foreach (var kv in FwSlots)   // face + paddle + triggers
            {
                string button = kv.Key;
                FwSlot slot = kv.Value;

                byte[] payload;
                if (modeOn && desiredSnapshot.TryGetValue(button, out int[] hidCodes) &&
                    TryTranslateCodes(hidCodes, out byte[] msiCodes))
                {
                    payload = BuildSlotPayload(slot, msiCodes);   // configured keyboard remap
                }
                else if (applyGamepad && gamepadSnapshot.TryGetValue(button, out byte targetCode))
                {
                    // Same slot format as a keyboard remap, but the code is the target BUTTON code (1..12).
                    payload = BuildSlotPayload(slot, new byte[] { targetCode });   // gamepad→gamepad remap
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
                    failures++;
                    Logger.Warn($"ClawButtonMonitor: FW keyboard write failed for {button} (addr 0x{slot.Address:X4}).");
                }
            }

            if (writes > 0)
            {
                bool syncOk = SendRawCmd(BuildSyncToRomCmd());
                Logger.Info($"ClawButtonMonitor: FW keyboard map flushed ({writes} slot(s){(force ? ", full reset" : "")}, sync={syncOk}).");
            }

            // Any failed slot (transient re-enumeration, brief exclusive handle) → retry the remaining
            // ones. Successful slots are cached in _fwKbLastWritten and skipped, so this never re-writes
            // good slots — only the ones that still need to land.
            if (failures > 0)
                RescheduleFwKeyboardFlush(force, $"{failures} slot write(s) failed");
            else
                _fwKbFlushRetries = 0;
        }

        /// <summary>Bounded retry of <see cref="FlushFirmwareKeyboardMap"/> — bridges the startup gap
        /// before the command channel is up and covers transient write failures.</summary>
        private void RescheduleFwKeyboardFlush(bool force, string reason)
        {
            if (_fwKbFlushRetries >= FwKbMaxFlushRetries)
            {
                Logger.Warn($"ClawButtonMonitor: FW keyboard flush giving up after {_fwKbFlushRetries} retries ({reason}).");
                _fwKbFlushRetries = 0;
                return;
            }
            _fwKbFlushRetries++;
            Logger.Info($"ClawButtonMonitor: FW keyboard flush retry {_fwKbFlushRetries}/{FwKbMaxFlushRetries} in {FwKbRetryDelayMs}ms ({reason}).");
            lock (_fwKbLock)
            {
                _fwKbWriteTimer?.Dispose();
                _fwKbWriteTimer = new Timer(_ => FlushFirmwareKeyboardMap(force), null, FwKbRetryDelayMs, Timeout.Infinite);
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
                // Paddle SLOT START (verified on-device 2026-07-10): the mapping is the 4-byte struct
                // `01 04 0C <code>` beginning at 0x00BA (M1) / 0x0163 (M2) — NOT a single register at
                // 0x00BD/0x0166 (that is only the code byte, +3 into the slot). Writing there without the
                // `01 04 0C` header left the remap marker at 00 00 and the firmware emitted nothing.
                { "M1",        new FwSlot { Address = 0x00BA, Class = FwSlotClass.Paddle,  OwnCode = M1_DEFAULT_CODE } },
                { "M2",        new FwSlot { Address = 0x0163, Class = FwSlotClass.Paddle,  OwnCode = M2_DEFAULT_CODE } },
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
                // Triggers L2/R2 — verified on-device 2026-07-10: remap `06 0C 03 60 <codes>`, default
                // (analog) `02 0C 03 60 <ownCode>` (L2 code 0x13 / R2 0x14). Names match TryGetSwapSource
                // ("LT"/"RT"). NOTE: bytes 2/3 (0x03/0x60) double as the trigger dead-zone/edge-dead-zone
                // — a custom trigger dead-zone is reset to MSI's default here (acceptable v1).
                { "LT",        new FwSlot { Address = 0x020D, Class = FwSlotClass.Trigger, OwnCode = 0x13 } },
                { "RT",        new FwSlot { Address = 0x021C, Class = FwSlotClass.Trigger, OwnCode = 0x14 } },
            };

        /// <summary>
        /// Builds the variable-length slot payload (the bytes after the address/len header), matching
        /// the byte formats that are proven/observed to fire:
        ///   • Paddle (M1/M2): the 4-byte struct `01 04 0C <code(s)>` (RE'd against MSI on-device
        ///     2026-07-10): default M1 = 01 04 0C 11, M2 = 01 04 0C 12; M1→Win+D = 01 04 0C 75 5E,
        ///     M2→Alt+Tab = 01 04 0C 76 4D. The `01` prefix + `04 0C` remap marker are MANDATORY —
        ///     writing only the code byte(s) (previously, at 0x00BD) left the marker at 00 00 and the
        ///     firmware ignored the codes (no key emitted). Written as `01 04 0C` + up to 5 code slots,
        ///     zero-padded, so switching to a shorter chord can't leave a ghost key in the trailing bytes.
        ///   • Face/dpad/shoulder/stick-click: EXACTLY MSI's observed `04 0C <codes>` (len 2+N, NO
        ///     padding — MSI's Y→Win+D was len 4 = `04 0C 75 5E`). Default = `00 0C <ownCode>` (len 3).
        /// </summary>
        private static byte[] BuildSlotPayload(FwSlot slot, byte[] msiCodes)
        {
            switch (slot.Class)
            {
                case FwSlotClass.Paddle:
                {
                    // 01 04 0C <up to 5 codes>, zero-padded to a fixed 8-byte slot so a shorter chord
                    // later can't leave a ghost key in the trailing code bytes. Default = own code
                    // (0x11/0x12) in the first code slot: reads back as MSI's default 01 04 0C 11/12.
                    byte[] p = new byte[8];
                    p[0] = 0x01; p[1] = 0x04; p[2] = 0x0C;
                    if (msiCodes != null && msiCodes.Length > 0)
                        Array.Copy(msiCodes, 0, p, 3, Math.Min(msiCodes.Length, 5));
                    else
                        p[3] = slot.OwnCode;
                    return p;
                }
                case FwSlotClass.Face:
                {
                    // Fixed 7-byte slot: `<flag> 0C` + up to 5 code slots, zero-padded. The padding is
                    // ESSENTIAL — writing only `04 0C <codes>` at MSI's len 2+N leaves the trailing code
                    // bytes of a PREVIOUS longer chord in the firmware slot (a shorter write doesn't
                    // overwrite them), so e.g. Ctrl+T → Home read back as "Home + T". Zero-padding
                    // overwrites those bytes. A Face slot is 8 bytes wide, so 7 bytes stays in-slot.
                    // The decoder stops at the first 0x00, so the trailing zeros are ignored on read.
                    byte[] p = new byte[7];
                    p[1] = 0x0C;
                    if (msiCodes != null && msiCodes.Length > 0)
                    {
                        p[0] = 0x04;   // remapped-to-keyboard/gamepad
                        Array.Copy(msiCodes, 0, p, 2, Math.Min(msiCodes.Length, 5));
                    }
                    else
                    {
                        p[0] = 0x00;             // default = button does its own function
                        p[2] = slot.OwnCode;     // 00 0C <ownCode>
                    }
                    return p;
                }
                case FwSlotClass.Trigger:
                {
                    // 8-byte block. Remap: `06 0C <DZ> <EDZ> <up to 4 codes>`. Default (analog):
                    // `02 0C <DZ> <EDZ> <ownCode> 0 0 0`. Verified vs MSI: L2→Win+D = 06 0C 03 60 75 5E,
                    // default L2 = 02 0C 03 60 13 / R2 = …14. The flag switches 02↔06; the trailing `01`
                    // marker further in the slot is left untouched (we never write that far).
                    byte[] p = new byte[8];
                    p[1] = 0x0C;
                    p[2] = TRIGGER_DZ;
                    p[3] = TRIGGER_EDZ;
                    if (msiCodes != null && msiCodes.Length > 0)
                    {
                        p[0] = 0x06;   // remapped-to-keyboard
                        Array.Copy(msiCodes, 0, p, 4, Math.Min(msiCodes.Length, 4));
                    }
                    else
                    {
                        p[0] = 0x02;             // default analog
                        p[4] = slot.OwnCode;     // L2=0x13 / R2=0x14 → restores the plain analog trigger
                    }
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

        // ── Read-back: decode the live firmware button map (for the Controller-status refresh) ──
        /// <summary>
        /// Reads every known firmware slot (face/paddle/trigger) via the 0x04 ReadProfile opcode and
        /// returns a human-readable report of the buttons that currently carry a keyboard remap, e.g.
        /// "M1 → Win+D". Buttons at their default function are omitted. Returns a short status string
        /// when nothing is mapped or the device isn't capable. Safe to call on demand (Controller
        /// status refresh); each slot is a separate open/read on the vendor command channel.
        /// </summary>
        public string BuildFirmwareButtonMapReport()
        {
            if (!_fwKeyboardCapable)
                return "Firmware key remap not supported on this device.";
            // Firmware-only channel: acquire the always-present command interface on demand so the
            // report works in Hardware Controller mode too (no virtual monitor running).
            if (_cmdDevice == null)
            {
                try { _cmdDevice = FindCommandDevice(); } catch { /* fall through to the guard below */ }
            }
            if (_cmdDevice == null)
                return "Controller command channel unavailable.";

            var mapped = new List<string>();
            int readFails = 0;
            foreach (var kv in FwSlots)
            {
                byte[] d = ReadFwSlotRaw(kv.Value.Address, 7);
                if (d == null) { readFails++; continue; }
                // Gamepad→gamepad remap (target button code 1..12) is checked first — it shares the
                // `04 0C <code>` / `01 04 0C <code>` slot format with a keyboard remap but the code is a
                // button code, so the keyboard decoder would misread it.
                string gpTarget = DecodeSlotGamepadTarget(kv.Value, d);
                if (!string.IsNullOrEmpty(gpTarget))
                {
                    mapped.Add($"{FriendlyButtonName(kv.Key)} → {gpTarget}");
                    continue;
                }
                string keys = DecodeSlotKeys(kv.Value, d);
                if (!string.IsNullOrEmpty(keys))
                    mapped.Add($"{FriendlyButtonName(kv.Key)} → {keys}");
            }

            string body = mapped.Count > 0 ? string.Join("\n", mapped) : "All buttons at default.";
            if (readFails > 0) body += $"\n({readFails} slot(s) could not be read)";
            return body;
        }

        /// <summary>Reads <paramref name="len"/> data bytes at <paramref name="addr"/> via opcode 0x04;
        /// returns null on failure. Data starts at index 9 of the reply (10 00 00 3C 05 01 addr len …).</summary>
        private byte[] ReadFwSlotRaw(ushort addr, byte len)
        {
            var dev = _cmdDevice;
            if (dev == null) return null;
            // Retry the exclusive open a few times: the same command interface is polled by the HW-mouse
            // GamepadMode watcher (and rumble), so a single open can lose the sub-100ms race and throw.
            // Without this a "Re-read firmware" during the ~7s post-mode-switch window failed for every slot.
            Exception last = null;
            for (int openAttempt = 0; openAttempt < 6; openAttempt++)
            {
                try
                {
                    using (var stream = dev.Open())
                    {
                        stream.WriteTimeout = 600;
                        stream.ReadTimeout = 600;
                        byte[] cmd = new byte[64];
                        cmd[0] = REPORT_ID; cmd[3] = 0x3C; cmd[4] = 0x04; cmd[5] = 0x00;
                        cmd[6] = (byte)(addr >> 8); cmd[7] = (byte)(addr & 0xFF); cmd[8] = len;
                        stream.Write(cmd);

                        // Accept only the ReadProfile echo for THIS address (skip unrelated input reports).
                        for (int attempt = 0; attempt < 6; attempt++)
                        {
                            byte[] resp = new byte[64];
                            int n = stream.Read(resp);
                            if (n >= 9 + len && resp[4] == 0x05 &&
                                resp[6] == (byte)(addr >> 8) && resp[7] == (byte)(addr & 0xFF))
                            {
                                byte[] data = new byte[len];
                                Array.Copy(resp, 9, data, 0, len);
                                return data;
                            }
                        }
                        return null; // opened + wrote fine but no matching echo — don't churn the open
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(25);
                }
            }
            Logger.Warn($"ClawButtonMonitor: ReadFwSlotRaw 0x{addr:X4} failed after retries: {last?.Message}");
            return null;
        }

        /// <summary>Decodes a slot's raw bytes into a "+"-joined key-combo string, or "" if the slot is
        /// at its default (no keyboard remap). Mirrors <see cref="BuildSlotPayload"/>'s formats.</summary>
        private static string DecodeSlotKeys(FwSlot slot, byte[] d)
        {
            if (d == null || d.Length < 3) return string.Empty;
            switch (slot.Class)
            {
                case FwSlotClass.Face:      // <flag> 0C <code…> — 0x00 default, 0x04 keyboard remap
                    if (d[1] != 0x0C || d[0] != 0x04) return string.Empty;
                    return DecodeMsiCodes(d, 2);
                case FwSlotClass.Paddle:    // 01 04 0C <code…> — default when code == ownCode, or MSI's
                                            // own "NC"/unassigned sentinel 0xFF (Center M can write this
                                            // directly, e.g. via its own reset — see RE_MSI_ButtonRemap.md,
                                            // "Per-button delete = <addr> 02 FF 00"). Both mean "no remap".
                    if (d.Length < 4 || d[2] != 0x0C) return string.Empty;
                    if (d[3] == 0xFF) return string.Empty;
                    if (d[3] == slot.OwnCode && (d.Length < 5 || d[4] == 0x00)) return string.Empty;
                    return DecodeMsiCodes(d, 3);
                case FwSlotClass.Trigger:   // <flag> 0C <DZ> <EDZ> <code…> — 0x02 analog, 0x06 remap
                    if (d[1] != 0x0C || d[0] != 0x06) return string.Empty;
                    return DecodeMsiCodes(d, 4);
                default:
                    return string.Empty;
            }
        }

        /// <summary>Decodes a slot's raw bytes into a target gamepad-button name when it holds a
        /// gamepad→gamepad remap (`04 0C &lt;code&gt;` face / `01 04 0C &lt;code&gt;` paddle with a button
        /// code 1..12), else "". Button codes (1..12) are disjoint from keyboard codes (0x32+) and from
        /// the paddle default own-codes (0x11/0x12), so this is unambiguous.</summary>
        private static string DecodeSlotGamepadTarget(FwSlot slot, byte[] d)
        {
            if (d == null || d.Length < 3) return string.Empty;
            switch (slot.Class)
            {
                case FwSlotClass.Face:      // 04 0C <code>
                    if (d[0] != 0x04 || d[1] != 0x0C) return string.Empty;
                    return TargetCodeToButtonName(d[2]) ?? string.Empty;
                case FwSlotClass.Paddle:    // 01 04 0C <code>
                    if (d.Length < 4 || d[2] != 0x0C) return string.Empty;
                    return TargetCodeToButtonName(d[3]) ?? string.Empty;
                default:                    // triggers are never gamepad-remap targets
                    return string.Empty;
            }
        }

        private static string DecodeMsiCodes(byte[] d, int start)
        {
            var parts = new List<string>();
            for (int i = start; i < d.Length && parts.Count < 5; i++)
            {
                if (d[i] == 0x00) break;
                parts.Add(MsiCodeToName(d[i]));
            }
            return parts.Count > 0 ? string.Join("+", parts) : string.Empty;
        }

        /// <summary>Reverse of the MSI physical-keycode table (RE_MSI_ButtonRemap.md, 0x32–0x83).</summary>
        private static string MsiCodeToName(byte c)
        {
            if (c >= 0x33 && c <= 0x3E) return "F" + (c - 0x33 + 1);           // F1–F12
            if (c >= 0x40 && c <= 0x48) return ((char)('1' + (c - 0x40))).ToString(); // 1–9
            if (c >= 0x4E && c <= 0x57) return "QWERTYUIOP".Substring(c - 0x4E, 1);
            if (c >= 0x5C && c <= 0x64) return "ASDFGHJKL".Substring(c - 0x5C, 1);
            if (c >= 0x69 && c <= 0x6F) return "ZXCVBNM".Substring(c - 0x69, 1);
            switch (c)
            {
                case 0x32: return "Esc";
                case 0x3F: return "`";
                case 0x49: return "0";
                case 0x4A: return "-";
                case 0x4B: return "=";
                case 0x4C: return "Backspace";
                case 0x4D: return "Tab";
                case 0x58: return "[";
                case 0x59: return "]";
                case 0x5A: return "\\";
                case 0x5B: return "CapsLock";
                case 0x65: return ";";
                case 0x66: return "'";
                case 0x67: return "Enter";
                case 0x68: return "Shift";
                case 0x70: return ",";
                case 0x71: return ".";
                case 0x72: return "/";
                case 0x73: return "RShift";
                case 0x74: return "Ctrl";
                case 0x75: return "Win";
                case 0x76: return "Alt";
                case 0x77: return "Space";
                case 0x78: return "RAlt";
                case 0x79: return "RCtrl";
                case 0x7A: return "Insert";
                case 0x7B: return "Home";
                case 0x7C: return "PgUp";
                case 0x7D: return "Delete";
                case 0x7E: return "End";
                case 0x7F: return "PgDn";
                case 0x80: return "Up";
                case 0x81: return "Down";
                case 0x82: return "Left";
                case 0x83: return "Right";
                default:   return $"0x{c:X2}";
            }
        }

        private static string FriendlyButtonName(string key)
        {
            switch (key)
            {
                case "DPadUp": return "D-Pad Up";
                case "DPadDown": return "D-Pad Down";
                case "DPadLeft": return "D-Pad Left";
                case "DPadRight": return "D-Pad Right";
                case "LSClick": return "L-Stick Click";
                case "RSClick": return "R-Stick Click";
                default: return key;   // A/B/X/Y, LB/RB, M1/M2, LT/RT
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
