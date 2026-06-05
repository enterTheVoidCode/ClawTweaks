using System;
using HidSharp;
using NLog;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Controls the MSI Claw controller LED color via HID vendor commands.
    /// Ported from Handheld Companion fork: ClawA1M.cs GetRGB() / SetLedColor().
    ///
    /// The same 64-byte HID output report format is used for all Claw generations;
    /// only the firmware-specific RGB start address differs between firmware lines.
    /// We try both known addresses so we don't need explicit firmware-version detection.
    /// </summary>
    internal static class MsiClawLedController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Firmware-specific RGB start address pairs (from HC DeviceVersion table).
        // Newer firmware first: 0x166+, 0x217+, 0x308 (A2VM / newer A1M).
        // Older fallback: 0x163, 0x211 (original Claw 1 / Claw 8).
        private static readonly byte[][] RgbAddresses = {
            new byte[] { 0x02, 0x4A },
            new byte[] { 0x01, 0xFA },
        };

        /// <summary>
        /// Sets all controller LED zones to a uniform RGB color.
        /// Brightness is fixed at 100 (max) — the user controls color only.
        /// </summary>
        /// <param name="r">Red 0-255</param>
        /// <param name="g">Green 0-255</param>
        /// <param name="b">Blue 0-255</param>
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

                // Try both known RGB addresses; stop on first success.
                foreach (var addr in RgbAddresses)
                {
                    try
                    {
                        byte[] msg = BuildRgbPacket(r, g, b, brightness: 100, addr1: addr[0], addr2: addr[1]);
                        using (HidStream stream = device.Open())
                            stream.Write(msg);

                        Logger.Info($"[MsiClawLed] Color set R={r} G={g} B={b} via addr [{addr[0]:X2},{addr[1]:X2}]");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"[MsiClawLed] addr [{addr[0]:X2},{addr[1]:X2}] failed: {ex.Message}");
                    }
                }

                Logger.Warn("[MsiClawLed] All RGB address attempts failed");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"[MsiClawLed] TrySetLedColor failed: {ex.Message}");
                return false;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds a 64-byte HID output report for SolidColor LED mode.
        /// Packet format ported from HC ClawA1M.GetRGB() — 9 zones × [R,G,B].
        /// </summary>
        private static byte[] BuildRgbPacket(byte r, byte g, byte b, byte brightness, byte addr1, byte addr2)
        {
            byte[] msg = new byte[64];
            // Preamble
            msg[0] = 0x0F; msg[1] = 0x00; msg[2] = 0x00; msg[3] = 0x3C;
            // Write first profile command
            msg[4] = 0x21; msg[5] = 0x01;
            // Firmware-specific RGB start address
            msg[6] = addr1; msg[7] = addr2;
            // Write 31 bytes (0x20 = 32, but HC uses 0x20)
            msg[8] = 0x20;
            // Index=0, Frame=1, Effect=0x09 (SolidColor), Speed=3, Brightness=0-100
            msg[9] = 0x00; msg[10] = 0x01; msg[11] = 0x09; msg[12] = 0x03;
            msg[13] = Math.Min((byte)100, brightness);
            // 9 zones: zones 0-3 = right side LEDs, zones 4-8 = left side + buttons
            // For SolidColor all zones use the same MainColor (HC: MainColor for zones 4-8,
            // SecondaryColor for 0-3; in solid mode both are equal, so use same R,G,B).
            for (int zone = 0; zone < 9; zone++)
            {
                msg[14 + zone * 3 + 0] = r;
                msg[14 + zone * 3 + 1] = g;
                msg[14 + zone * 3 + 2] = b;
            }
            return msg;
        }
    }
}
