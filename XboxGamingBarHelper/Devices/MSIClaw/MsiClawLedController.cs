using System;
using HidSharp;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Controls the MSI Claw controller LED color via HID vendor commands.
    /// Ported 1:1 from Handheld Companion: ClawA1M.cs GetRGB() / SetLedColor() / IsReady().
    ///
    /// Firmware version is read from HidDevice.ReleaseNumberBcd (= USB bcdDevice field),
    /// matching HC's  Firmware = device.Attributes.Version  in ClawA1M.IsReady().
    /// The version selects the correct firmware-specific RGB start address from the same
    /// deviceVersions table that HC uses; nearest-match (MinBy |v.Firmware - Firmware|) is
    /// the fallback when the exact firmware is not listed.
    /// </summary>
    internal static class MsiClawLedController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ── Firmware version → RGB start-address table ────────────────────────────
        // 1:1 from HC ClawA1M.cs deviceVersions field.
        // Source: https://github.com/.../Devices/MSI/ClawA1M.cs
        private struct FwVersion
        {
            public int  Firmware;   // bcdDevice (USB device descriptor release number)
            public byte Add1;       // High byte of RGB EEPROM write address
            public byte Add2;       // Low  byte of RGB EEPROM write address
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
        };

        // Fallback addresses when firmware is unknown (matches the 0x163/0x211 default in HC).
        private const byte DefaultAdd1 = 0x01;
        private const byte DefaultAdd2 = 0xFA;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets all controller LED zones to a uniform solid RGB color.
        /// Brightness is fixed at 100 (max) — the user controls color via the widget.
        ///
        /// Mirrors HC ClawA1M.SetLedColor(SolidColor): both MainColor and SecondaryColor
        /// are set to the same value, producing uniform lighting across all nine zones.
        /// </summary>
        /// <param name="r">Red   0–255</param>
        /// <param name="g">Green 0–255</param>
        /// <param name="b">Blue  0–255</param>
        /// <returns>true on success, false if device not found or write fails.</returns>
        public static bool TrySetLedColor(byte r, byte g, byte b)
        {
            try
            {
                HidDevice device = MSIClawHidController.FindClawHidDeviceInternal();
                if (device == null)
                {
                    Logger.Warn("[MsiClawLed] HID device not found — cannot set LED color");
                    return false;
                }

                // Read firmware version from the USB device descriptor's bcdDevice field.
                // HC pattern: Firmware = device.Attributes.Version  (ClawA1M.IsReady())
                // HidSharp 2.x equivalent: HidDevice.ReleaseNumberBcd
                int fwVersion = device.ReleaseNumberBcd;

                // Resolve the firmware-specific RGB write address (MinBy |fw - fwVersion|).
                // HC pattern: FirmwareDevice = deviceVersions.MinBy(v => Math.Abs(v.Firmware - Firmware))
                byte add1 = DefaultAdd1;
                byte add2 = DefaultAdd2;
                int  bestDist = int.MaxValue;
                foreach (FwVersion entry in FirmwareTable)
                {
                    int dist = Math.Abs(entry.Firmware - fwVersion);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        add1 = entry.Add1;
                        add2 = entry.Add2;
                    }
                }

                bool exactMatch = bestDist == 0;
                Logger.Info($"[MsiClawLed] FW=0x{fwVersion:X3} → RGB addr [{add1:X2},{add2:X2}]" +
                            (exactMatch ? "" : $" (nearest match, dist={bestDist})"));

                byte[] msg = BuildRgbPacket(r, g, b, brightness: 100, addr1: add1, addr2: add2);
                using (HidStream stream = device.Open())
                    stream.Write(msg);

                Logger.Info($"[MsiClawLed] LED set R={r} G={g} B={b}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[MsiClawLed] TrySetLedColor failed: {ex.Message}");
                return false;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds the 64-byte HID output report for SolidColor LED mode.
        ///
        /// Packet structure ported 1:1 from HC ClawA1M.GetRGB():
        ///   [0..3]   Preamble:       0x0F 0x00 0x00 0x3C
        ///   [4..5]   WriteProfile:   0x21 0x01
        ///   [6..7]   RGB start addr: addr1 addr2  (firmware-specific)
        ///   [8]      Write length:   0x20 (32 bytes)
        ///   [9]      Zone index:     0x00
        ///   [10]     Frame number:   0x01
        ///   [11]     Effect:         0x09  (Static / SolidColor)
        ///   [12]     Speed:          0x03
        ///   [13]     Brightness:     0–100
        ///   [14..40] 9 × [R,G,B]:   all zones same color (SolidColor mode)
        ///   [41..63] Zero padding
        /// </summary>
        private static byte[] BuildRgbPacket(byte r, byte g, byte b, byte brightness, byte addr1, byte addr2)
        {
            byte[] msg = new byte[64];

            // Preamble
            msg[0] = 0x0F; msg[1] = 0x00; msg[2] = 0x00; msg[3] = 0x3C;
            // WriteProfile command, profile slot 1
            msg[4] = 0x21; msg[5] = 0x01;
            // Firmware-specific RGB EEPROM start address
            msg[6] = addr1; msg[7] = addr2;
            // Write length = 0x20 (32 bytes)
            msg[8] = 0x20;
            // Zone=0, Frame=1, Effect=Static(0x09), Speed=3, Brightness clamped 0-100
            msg[9]  = 0x00;
            msg[10] = 0x01;
            msg[11] = 0x09;
            msg[12] = 0x03;
            msg[13] = Math.Min((byte)100, brightness);

            // 9 zone RGB triplets (zones 0-3 = right side, zones 4-7 = left side, zone 8 = buttons).
            // HC SolidColor: both MainColor and SecondaryColor are the same value.
            for (int zone = 0; zone < 9; zone++)
            {
                msg[14 + zone * 3 + 0] = r;
                msg[14 + zone * 3 + 1] = g;
                msg[14 + zone * 3 + 2] = b;
            }

            // Bytes [41..63] remain 0x00 (array initialisation default).
            return msg;
        }
    }
}
