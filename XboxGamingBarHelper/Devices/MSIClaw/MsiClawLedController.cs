using System;
using HidSharp;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Controls the MSI Claw controller LED via HID vendor commands (WriteProfile 0x21 to a
    /// firmware-specific EEPROM address). Two write shapes are used:
    ///   • a "block" write (<see cref="BuildBlock"/>) carries the effect header (mode/speed/brightness)
    ///     plus the first 27-byte frame at the base RGB address;
    ///   • "raw" writes (<see cref="BuildRawBlock"/>) place the remaining 27-byte frames at CONTIGUOUS
    ///     EEPROM addresses (base+32, +59, +86, …) — the firmware then plays/interpolates the frame slots.
    ///
    /// Effects:
    ///   Static/Breathing → <see cref="TrySetSolidEffect"/>/<see cref="TrySetZonesEffect"/> (mode byte
    ///     Static=1 / Breathing=6, one solid/zone frame filled into all slots).
    ///   Wave  → <see cref="TrySetWaveEffect"/> (mode 4, a rotating 4-colour ring across the frames).
    ///   Cycle → <see cref="TrySetCycleEffect"/> (mode 4, one palette colour per frame slot).
    ///   Composite (per-zone) → <see cref="TrySetFrames"/> (mode 4, 4 pre-rendered frames from LedCompositor,
    ///     plus two zero frames to clear stale slots).
    ///
    /// Reverse-engineered from the real firmware; the block layout (byte[10]=mode, byte[11]=9,
    /// byte[12]=speed, byte[13]=brightness, byte[14..40]=27-byte frame) and the CONTIGUOUS raw-frame
    /// addressing are the exact protocol the controller accepts — writing full header blocks to the same
    /// address instead corrupts the LED config until a reboot.
    ///
    /// Firmware version comes from HidDevice.ReleaseNumberBcd (USB bcdDevice); it selects the
    /// firmware-specific RGB start address (nearest-match), 1:1 from Handheld Companion's ClawA1M table.
    /// </summary>
    internal static class MsiClawLedController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ── Firmware version → RGB start-address table (1:1 from HC ClawA1M deviceVersions) ──
        private struct FwVersion
        {
            public int  Firmware;   // bcdDevice (USB device descriptor release number)
            public byte Add1;       // High byte of the RGB EEPROM start address
            public byte Add2;       // Low  byte of the RGB EEPROM start address
        }

        private static readonly FwVersion[] FirmwareTable =
        {
            // ── Claw 1 (Meteor Lake) ──────────────────────────
            new FwVersion { Firmware = 0x163, Add1 = 0x01, Add2 = 0xFA },
            new FwVersion { Firmware = 0x166, Add1 = 0x02, Add2 = 0x4A },
            new FwVersion { Firmware = 0x167, Add1 = 0x02, Add2 = 0x4A },
            // ── Claw 8 (Meteor Lake) ──────────────────────────
            new FwVersion { Firmware = 0x211, Add1 = 0x01, Add2 = 0xFA },
            new FwVersion { Firmware = 0x217, Add1 = 0x02, Add2 = 0x4A },
            new FwVersion { Firmware = 0x219, Add1 = 0x02, Add2 = 0x4A },
            // ── Claw A8 / Claw 7+8 AI+ A2VM (Lunar Lake) ─────
            new FwVersion { Firmware = 0x308, Add1 = 0x02, Add2 = 0x4A },
            // ── Claw 8 AI+ EX (Panther Lake, MS-1T91) ────────
            // Not from HC (no EX support upstream). Measured on-device 2026-07-05: fw
            // bcdDevice 0x0411; the nearest-match fallback picked [02,4A] and the controller
            // LEDs VISIBLY responded to on/off/colour writes (human-verified, port log
            // "evening" entry) — promoted to an exact entry so the EX no longer depends on
            // nearest-match behavior.
            new FwVersion { Firmware = 0x411, Add1 = 0x02, Add2 = 0x4A },
        };

        private const byte DefaultAdd1 = 0x01;
        private const byte DefaultAdd2 = 0xFA;

        /// <summary>Firmware effect-mode byte (goes into block byte[10]).</summary>
        public enum LedEffectMode : byte
        {
            Static    = 1,
            Breathing = 6,
            Wave      = 4,
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Firmware speed byte for a given effect + UI speed index (0 slow / 1 med / 2 fast).</summary>
        public static byte SpeedByteFor(LedEffectMode mode, int speedIndex)
        {
            switch (mode)
            {
                case LedEffectMode.Breathing:
                    return speedIndex <= 0 ? (byte)7 : speedIndex == 1 ? (byte)5 : (byte)3;
                case LedEffectMode.Wave:
                    return speedIndex <= 0 ? (byte)10 : speedIndex == 1 ? (byte)6 : (byte)3;
                default:
                    return 3;
            }
        }

        /// <summary>Firmware speed byte for the Color Cycle effect.</summary>
        public static byte CycleSpeedByte(int speedIndex)
            => speedIndex <= 0 ? (byte)7 : speedIndex == 1 ? (byte)5 : (byte)3;

        /// <summary>
        /// Sets all controller LED zones to a uniform solid RGB colour (Static). brightness 0 = off.
        /// Routed through the effect path so it uses the exact same block layout the firmware expects.
        /// </summary>
        public static bool TrySetLedColor(byte r, byte g, byte b, byte brightness = 100)
            => TrySetSolidEffect(LedEffectMode.Static, r, g, b, SpeedByteFor(LedEffectMode.Static, 1), brightness);

        /// <summary>Solid single-colour effect (all 9 zones the same colour) in the given mode.</summary>
        public static bool TrySetSolidEffect(LedEffectMode mode, byte r, byte g, byte b, byte speed, byte brightness)
        {
            byte[] zones = new byte[27];
            for (int i = 0; i < 9; i++)
            {
                zones[i * 3]     = r;
                zones[i * 3 + 1] = g;
                zones[i * 3 + 2] = b;
            }
            return TrySetZonesEffect(mode, zones, speed, brightness);
        }

        /// <summary>
        /// Writes a single 27-byte zone frame in the given mode: the header block at the base address
        /// carries mode/speed/brightness + the frame, then the SAME frame is written to the 3 following
        /// contiguous frame slots so a static/breathing effect fills all slots uniformly.
        /// </summary>
        public static bool TrySetZonesEffect(LedEffectMode mode, byte[] zones27, byte speed, byte brightness)
        {
            try
            {
                if (zones27 == null || zones27.Length < 27) return false;

                // Respect the detected device's RGB capability flag. LED writes go to controller
                // EEPROM at a firmware-version-specific address; on a device with
                // SupportsRgbLighting=false (e.g. a future Claw variant whose firmware is not yet
                // in FirmwareTable) a write would land on an unverified address. Single choke
                // point: covers the pipe handler, MsiLedBoot and LED-by-SoC callers, none of which
                // checked the flag (observed ungated writes on the EX, 2026-07-05).
                if (!DeviceDetector.DetectDevice().SupportsRgbLighting)
                {
                    Logger.Warn("[MsiClawLed] LED write blocked: detected device reports SupportsRgbLighting=false");
                    return false;
                }

                HidDevice device = MSIClawHidController.FindClawHidDeviceInternal();
                if (device == null)
                {
                    Logger.Warn("[MsiClawLed] HID device not found - cannot set LED effect");
                    return false;
                }

                ResolveRgbAddr(device, out byte add1, out byte add2);
                int baseAddr = (add1 << 8) | add2;
                byte bri = Math.Min((byte)100, brightness);

                byte[] header = BuildBlock(add1, add2, (byte)mode, speed, bri, zones27);
                using (HidStream stream = device.Open())
                    stream.Write(header);

                int[] slotAddrs = { baseAddr + 32, baseAddr + 32 + 27, baseAddr + 32 + 54 };
                for (int i = 0; i < 3; i++)
                {
                    int a = slotAddrs[i];
                    byte[] raw = BuildRawBlock((byte)((a >> 8) & 0xFF), (byte)(a & 0xFF), zones27);
                    using (HidStream stream = device.Open())
                        stream.Write(raw);
                }

                Logger.Info($"[MsiClawLed] effect={mode} spd=0x{speed:X2} bri={bri} (4-frame fill)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[MsiClawLed] TrySetZonesEffect failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Wave effect: a 4-colour ring rotated across the 4 frame slots (mode 4).</summary>
        public static bool TrySetWaveEffect((byte r, byte g, byte b)[] ring, bool clockwise, byte speed, byte brightness)
        {
            try
            {
                if (ring == null || ring.Length < 4)
                {
                    Logger.Warn("[MsiClawLed] wave needs 4 ring colours");
                    return false;
                }

                HidDevice device = MSIClawHidController.FindClawHidDeviceInternal();
                if (device == null)
                {
                    Logger.Warn("[MsiClawLed] HID device not found - cannot set wave");
                    return false;
                }

                ResolveRgbAddr(device, out byte add1, out byte add2);
                int baseAddr = (add1 << 8) | add2;
                int dir = clockwise ? 1 : -1;
                byte bri = Math.Min((byte)100, brightness);

                byte[] header = BuildBlock(add1, add2, (byte)LedEffectMode.Wave, speed, bri, WaveFrame(ring, 0, dir));
                using (HidStream stream = device.Open())
                    stream.Write(header);

                int[] slotAddrs = { baseAddr + 32, baseAddr + 32 + 27, baseAddr + 32 + 54 };
                for (int i = 1; i <= 3; i++)
                {
                    int a = slotAddrs[i - 1];
                    byte[] raw = BuildRawBlock((byte)((a >> 8) & 0xFF), (byte)(a & 0xFF), WaveFrame(ring, i, dir));
                    using (HidStream stream = device.Open())
                        stream.Write(raw);
                }

                Logger.Info($"[MsiClawLed] wave cw={clockwise} spd=0x{speed:X2} bri={bri}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[MsiClawLed] TrySetWaveEffect failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Writes a pre-rendered per-zone composite: 4 frames (each 27 bytes = 9 zones × RGB) from
        /// LedCompositor. Frame 0 goes in the header block (mode 4); frames 1-3 plus TWO zero frames are
        /// written to the following contiguous slots (the zero frames clear any stale slot content so the
        /// firmware's timeline doesn't play leftover garbage). Brightness is baked into the RGB by the
        /// compositor, so the header brightness is 100.
        /// </summary>
        public static bool TrySetFrames(byte[][] frames, byte speed)
        {
            try
            {
                if (frames == null || frames.Length < 4) return false;

                HidDevice device = MSIClawHidController.FindClawHidDeviceInternal();
                if (device == null)
                {
                    Logger.Warn("[MsiClawLed] HID device not found - cannot set frames");
                    return false;
                }

                ResolveRgbAddr(device, out byte add1, out byte add2);
                int baseAddr = (add1 << 8) | add2;

                byte[] header = BuildBlock(add1, add2, (byte)LedEffectMode.Wave, speed, 100, frames[0]);
                using (HidStream stream = device.Open())
                    stream.Write(header);

                int a = baseAddr + 32;
                for (int i = 1; i <= 5; i++)
                {
                    byte[] payload = (i <= 3) ? frames[i] : new byte[27];
                    byte[] raw = BuildRawBlock((byte)((a >> 8) & 0xFF), (byte)(a & 0xFF), payload);
                    using (HidStream stream = device.Open())
                        stream.Write(raw);
                    a += 27;
                }

                Logger.Info($"[MsiClawLed] composite frames written (spd=0x{speed:X2})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[MsiClawLed] TrySetFrames failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Color Cycle effect: one uniform palette colour per frame slot (mode 4).</summary>
        public static bool TrySetCycleEffect((byte r, byte g, byte b)[] palette, byte speed, byte brightness)
        {
            try
            {
                if (palette == null || palette.Length < 4) return false;

                HidDevice device = MSIClawHidController.FindClawHidDeviceInternal();
                if (device == null)
                {
                    Logger.Warn("[MsiClawLed] HID device not found - cannot set cycle");
                    return false;
                }

                ResolveRgbAddr(device, out byte add1, out byte add2);
                int baseAddr = (add1 << 8) | add2;
                byte bri = Math.Min((byte)100, brightness);

                byte[] header = BuildBlock(add1, add2, (byte)LedEffectMode.Wave, speed, bri, UniformFrame(palette[0]));
                using (HidStream stream = device.Open())
                    stream.Write(header);

                int[] slotAddrs = { baseAddr + 32, baseAddr + 32 + 27, baseAddr + 32 + 54 };
                for (int i = 1; i <= 3; i++)
                {
                    int a = slotAddrs[i - 1];
                    byte[] raw = BuildRawBlock((byte)((a >> 8) & 0xFF), (byte)(a & 0xFF), UniformFrame(palette[i]));
                    using (HidStream stream = device.Open())
                        stream.Write(raw);
                }

                Logger.Info($"[MsiClawLed] cycle spd=0x{speed:X2} bri={bri}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[MsiClawLed] TrySetCycleEffect failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Reads the controller firmware version from the HID descriptor (bcdDevice) as a dotted string,
        /// e.g. 0x0308 → "3.08". Returns null when no Claw HID device is present. Used by the driver card.
        /// </summary>
        public static string TryGetControllerFirmwareVersion()
        {
            try
            {
                HidDevice device = MSIClawHidController.FindClawHidDeviceInternal();
                if (device == null) return null;
                int bcd = device.ReleaseNumberBcd;
                if (bcd <= 0) return null;
                return $"{(bcd >> 8) & 0xFF:X}.{bcd & 0xFF:X2}";
            }
            catch (Exception ex)
            {
                Logger.Debug("[MsiClawLed] TryGetControllerFirmwareVersion failed: " + ex.Message);
                return null;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static byte[] UniformFrame((byte r, byte g, byte b) c)
        {
            byte[] frame = new byte[27];
            for (int i = 0; i < 9; i++)
            {
                frame[i * 3]     = c.r;
                frame[i * 3 + 1] = c.g;
                frame[i * 3 + 2] = c.b;
            }
            return frame;
        }

        /// <summary>Resolves the firmware-specific RGB EEPROM start address (nearest-match on bcdDevice).</summary>
        private static void ResolveRgbAddr(HidDevice device, out byte add1, out byte add2)
        {
            int fw = device.ReleaseNumberBcd;
            add1 = DefaultAdd1;
            add2 = DefaultAdd2;
            int bestDist = int.MaxValue;
            foreach (FwVersion entry in FirmwareTable)
            {
                int dist = Math.Abs(entry.Firmware - fw);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    add1 = entry.Add1;
                    add2 = entry.Add2;
                }
            }
            Logger.Info($"[MsiClawLed] FW=0x{fw:X3} → RGB addr [{add1:X2},{add2:X2}]" +
                        (bestDist == 0 ? "" : $" (nearest match, dist={bestDist})"));
        }

        /// <summary>One frame of the Wave effect: the 4-colour ring rotated by k in direction dir,
        /// mirrored across the two stick rings (zones 0-3 and 4-7) with zone 8 following the head.</summary>
        private static byte[] WaveFrame((byte r, byte g, byte b)[] ring, int k, int dir)
        {
            byte[] frame = new byte[27];
            for (int i = 0; i < 4; i++)
            {
                int idx = ((i + dir * k) % 4 + 4) % 4;
                var c = ring[idx];
                frame[i * 3]           = c.r; frame[i * 3 + 1]           = c.g; frame[i * 3 + 2]           = c.b;
                frame[(4 + i) * 3]     = c.r; frame[(4 + i) * 3 + 1]     = c.g; frame[(4 + i) * 3 + 2]     = c.b;
            }
            var head = ring[(dir * k % 4 + 4) % 4];
            frame[24] = head.r; frame[25] = head.g; frame[26] = head.b;
            return frame;
        }

        /// <summary>
        /// Builds the 64-byte HID output report carrying the effect header + the first 27-byte frame.
        /// Layout: [0]=0x0F [3]=0x3C [4]=0x21 (WriteProfile) [5]=0x01 [6..7]=RGB addr [8]=0x20 (write len)
        /// [9]=0 (zone) [10]=mode [11]=0x09 (const) [12]=speed [13]=brightness [14..40]=27-byte frame.
        /// </summary>
        private static byte[] BuildBlock(byte add1, byte add2, byte mode, byte speed, byte brightness, byte[] frame27)
        {
            byte[] msg = new byte[64];
            msg[0] = 0x0F; msg[3] = 0x3C;
            msg[4] = 0x21; msg[5] = 0x01;
            msg[6] = add1; msg[7] = add2;
            msg[8] = 0x20;              // write length 32
            msg[9]  = 0x00;            // zone base
            msg[10] = mode;
            msg[11] = 0x09;            // constant
            msg[12] = speed;
            msg[13] = Math.Min((byte)100, brightness);
            Array.Copy(frame27, 0, msg, 14, 27);
            return msg;
        }

        /// <summary>Builds a raw 27-byte frame write to a contiguous EEPROM address (no effect header).
        /// Layout: [0]=0x0F [3]=0x3C [4]=0x21 [5]=0x01 [6..7]=addr [8]=len [9..]=payload.</summary>
        private static byte[] BuildRawBlock(byte add1, byte add2, byte[] payload)
        {
            byte[] msg = new byte[64];
            msg[0] = 0x0F; msg[3] = 0x3C;
            msg[4] = 0x21; msg[5] = 0x01;
            msg[6] = add1; msg[7] = add2;
            msg[8] = (byte)payload.Length;
            Array.Copy(payload, 0, msg, 9, payload.Length);
            return msg;
        }
    }
}
