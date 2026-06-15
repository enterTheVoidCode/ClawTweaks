namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// Single source of truth for every VIIPER virtual-device input wire format. Maps an
    /// XInput-style gamepad state (<see cref="ViiperXInputGamepad"/>) — the same shape both the
    /// Legion poll loop and the MSI Claw submit point produce — into the byte report libviiper
    /// expects for each device type.
    ///
    /// Used by:
    ///   • <see cref="ViiperInputForwarder"/> (Legion Go) — passes <see cref="Extras"/> for the
    ///     extra channels it has (back-paddles, touchpad, IMU).
    ///   • ClawButtonMonitor (MSI Claw, DInput path) — passes <see cref="Extras.None"/> since the
    ///     Claw has no Legion aux buttons / touchpad, and its gyro is already folded into the stick.
    ///
    /// Button bits in <see cref="ViiperXInput"/> are the standard XUSB mask, so an assembled
    /// XInput state maps in 1:1.
    /// </summary>
    internal static class ViiperWireFormat
    {
        /// <summary>
        /// Optional per-frame extras that only the Legion forwarder supplies. The MSI Claw passes
        /// <see cref="None"/>. Aux is the reference VIIPER <see cref="LegionAux"/> bitfield; touch
        /// coordinates are raw Legion units (0–1023); IMU counts are device gyro counts + raw accel
        /// (accel is scaled per device inside the builders).
        /// </summary>
        internal struct Extras
        {
            public ushort Aux;
            public bool TouchActive;
            public ushort TouchRawX, TouchRawY;
            public bool HaveImu;
            public short GyroX, GyroY, GyroZ;
            public short AccelX, AccelY, AccelZ;

            public static readonly Extras None = default;
        }

        /// <summary>Dispatches to the correct builder for a libviiper device type tag.</summary>
        public static byte[] BuildForDeviceType(string deviceType, ViiperXInputGamepad gp, in Extras x)
        {
            switch (deviceType)
            {
                case "xbox360":        return BuildXbox360(gp);
                case "dualshock4":     return BuildDualShock4(gp, in x);
                case "dualsenseedge":  return BuildDualSenseEdge(gp, in x);
                case "xboxelite2":
                case "xbox-one":
                case "xbox-elite":
                case "steamdeck-generic":
                case "steam-generic":
                case "steam-controller": return BuildXboxElite2(gp, in x);
                case "switchpro":
                case "joycon-left":
                case "joycon-right":
                case "joycon-pair":    return BuildSwitchPro(gp);
                default:               return BuildXbox360(gp);
            }
        }

        // ── Xbox 360 (20 bytes) ───────────────────────────────────────────────
        public const int Xbox360Length = 20;

        /// <summary>
        /// Writes an xbox360 report into <paramref name="buf"/> (≥ <see cref="Xbox360Length"/>).
        /// Lets the Claw hot path reuse a single buffer to avoid per-frame allocation.
        /// </summary>
        public static void WriteXbox360(byte[] buf, ushort buttons, byte leftTrigger, byte rightTrigger,
                                        short thumbLX, short thumbLY, short thumbRX, short thumbRY)
        {
            WriteU32(buf, 0, buttons);
            buf[4] = leftTrigger;
            buf[5] = rightTrigger;
            WriteI16(buf, 6, thumbLX);
            WriteI16(buf, 8, thumbLY);
            WriteI16(buf, 10, thumbRX);
            WriteI16(buf, 12, thumbRY);
            buf[14] = 0; buf[15] = 0; buf[16] = 0; buf[17] = 0; buf[18] = 0; buf[19] = 0;
        }

        public static byte[] BuildXbox360(ushort buttons, byte leftTrigger, byte rightTrigger,
                                          short thumbLX, short thumbLY, short thumbRX, short thumbRY)
        {
            var buf = new byte[Xbox360Length];
            WriteXbox360(buf, buttons, leftTrigger, rightTrigger, thumbLX, thumbLY, thumbRX, thumbRY);
            return buf;
        }

        public static byte[] BuildXbox360(ViiperXInputGamepad gp)
        {
            return BuildXbox360(gp.Buttons, gp.LeftTrigger, gp.RightTrigger,
                                gp.ThumbLX, gp.ThumbLY, gp.ThumbRX, gp.ThumbRY);
        }

        // ── DualShock 4 (31 bytes) ────────────────────────────────────────────
        public static byte[] BuildDualShock4(ViiperXInputGamepad gp, in Extras x)
        {
            var data = new byte[31];
            // DS4 sticks are int8. Y-axis: XInput positive=UP, DS4 positive=DOWN → negate.
            data[0] = (byte)(gp.ThumbLX >> 8);
            data[1] = (byte)(NegateClamp(gp.ThumbLY) >> 8);
            data[2] = (byte)(gp.ThumbRX >> 8);
            data[3] = (byte)(NegateClamp(gp.ThumbRY) >> 8);

            ushort ds4Buttons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) ds4Buttons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.B) != 0) ds4Buttons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.X) != 0) ds4Buttons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.Y) != 0) ds4Buttons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LB) != 0) ds4Buttons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RB) != 0) ds4Buttons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Back) != 0) ds4Buttons |= 0x1000;
            if ((gp.Buttons & ViiperXInput.Start) != 0) ds4Buttons |= 0x2000;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) ds4Buttons |= 0x4000;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) ds4Buttons |= 0x8000;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) ds4Buttons |= 0x0001;

            ushort aux = x.Aux;
            if ((aux & LegionAux.Mode) != 0) ds4Buttons |= 0x0001;   // Mode -> PS
            if ((aux & LegionAux.Share) != 0) ds4Buttons |= 0x0002;  // Share -> Touchpad click
            WriteU16(data, 4, ds4Buttons);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[6] = dpad;

            data[7] = gp.LeftTrigger;
            data[8] = gp.RightTrigger;

            // Touchpad bytes (DS4): X at 9-10, Y at 11-12, active flag at 13.
            if (x.TouchActive)
            {
                WriteU16(data, 9, ScaleTouchAxis(x.TouchRawX, 1919));
                WriteU16(data, 11, ScaleTouchAxis(x.TouchRawY, 942));
                data[13] = 1;
            }

            // IMU bytes at offsets 19-30 (gyroX,Y,Z then accelX,Y,Z as int16).
            if (x.HaveImu)
            {
                WriteI16(data, 19, x.GyroX);
                WriteI16(data, 21, x.GyroY);
                WriteI16(data, 23, x.GyroZ);
                WriteI16(data, 25, ScaleDs4Accel(x.AccelX));
                WriteI16(data, 27, ScaleDs4Accel(x.AccelY));
                WriteI16(data, 29, ScaleDs4Accel(x.AccelZ));
            }
            return data;
        }

        // ── DualSense Edge (33 bytes) ─────────────────────────────────────────
        public static byte[] BuildDualSenseEdge(ViiperXInputGamepad gp, in Extras x)
        {
            var data = new byte[33];
            data[0] = (byte)(gp.ThumbLX >> 8);
            data[1] = (byte)(NegateClamp(gp.ThumbLY) >> 8);
            data[2] = (byte)(gp.ThumbRX >> 8);
            data[3] = (byte)(NegateClamp(gp.ThumbRY) >> 8);

            uint dseButtons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) dseButtons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.B) != 0) dseButtons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.X) != 0) dseButtons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.Y) != 0) dseButtons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LB) != 0) dseButtons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RB) != 0) dseButtons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Back) != 0) dseButtons |= 0x1000;
            if ((gp.Buttons & ViiperXInput.Start) != 0) dseButtons |= 0x2000;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) dseButtons |= 0x4000;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) dseButtons |= 0x8000;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) dseButtons |= 0x00010000;

            ushort aux = x.Aux;
            if ((aux & LegionAux.Y1) != 0) dseButtons |= 0x00100000;    // Y1 (left upper)  -> ExtraL2
            if ((aux & LegionAux.Y2) != 0) dseButtons |= 0x00400000;    // Y2 (left lower)  -> ExtraL1
            if ((aux & LegionAux.Y3) != 0) dseButtons |= 0x00200000;    // Y3 (right upper) -> ExtraR1
            if ((aux & LegionAux.M3) != 0) dseButtons |= 0x00800000;    // M3 (right lower) -> ExtraL3
            if ((aux & LegionAux.Mode) != 0) dseButtons |= 0x00010000;  // Mode  -> PS
            if ((aux & LegionAux.Share) != 0) dseButtons |= 0x00020000; // Share -> Touchpad click
            WriteU32(data, 4, dseButtons);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[8] = dpad;

            data[9] = gp.LeftTrigger;
            data[10] = gp.RightTrigger;

            // Touchpad bytes (DSE): X at 11-12, Y at 13-14, active flag at 15.
            if (x.TouchActive)
            {
                WriteU16(data, 11, ScaleTouchAxis(x.TouchRawX, 1920));
                WriteU16(data, 13, ScaleTouchAxis(x.TouchRawY, 1080));
                data[15] = 1;
            }

            // DSE IMU bytes at offsets 21..32.
            if (x.HaveImu)
            {
                WriteI16(data, 21, x.GyroX);
                WriteI16(data, 23, x.GyroY);
                WriteI16(data, 25, x.GyroZ);
                WriteI16(data, 27, ScaleDs4Accel(x.AccelX));
                WriteI16(data, 29, ScaleDs4Accel(x.AccelY));
                WriteI16(data, 31, ScaleDs4Accel(x.AccelZ));
            }
            return data;
        }

        // ── Xbox Elite 2 (33 bytes) — also Steam Generic / Deck / Controller ──
        public static byte[] BuildXboxElite2(ViiperXInputGamepad gp, in Extras x)
        {
            var data = new byte[33];
            ushort buttons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) buttons |= 0x0001;
            if ((gp.Buttons & ViiperXInput.B) != 0) buttons |= 0x0002;
            if ((gp.Buttons & ViiperXInput.X) != 0) buttons |= 0x0004;
            if ((gp.Buttons & ViiperXInput.Y) != 0) buttons |= 0x0008;
            if ((gp.Buttons & ViiperXInput.LB) != 0) buttons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.RB) != 0) buttons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.Back) != 0) buttons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.Start) != 0) buttons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) buttons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) buttons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) buttons |= 0x0400;

            ushort aux = x.Aux;
            if ((aux & LegionAux.Mode) != 0) buttons |= 0x0400; // Mode → Guide
            if ((aux & LegionAux.Y1) != 0)   buttons |= 0x8000; // Y1 (back L upper) → P4 (L4)
            if ((aux & LegionAux.Y3) != 0)   buttons |= 0x4000; // Y3 (back R upper) → P3 (R4)
            if ((aux & LegionAux.Y2) != 0)   buttons |= 0x2000; // Y2 (back L lower) → P2 (L5)
            if ((aux & LegionAux.M3) != 0)   buttons |= 0x1000; // M3 (back R lower) → P1 (R5)
            WriteU16(data, 0, buttons);

            data[2] = gp.LeftTrigger;
            data[3] = gp.RightTrigger;

            WriteI16(data, 4, gp.ThumbLX);
            WriteI16(data, 6, gp.ThumbLY);
            WriteI16(data, 8, gp.ThumbRX);
            WriteI16(data, 10, gp.ThumbRY);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[12] = dpad;

            if ((aux & LegionAux.Share) != 0) data[13] |= 0x01;
            return data;
        }

        // ── Switch Pro / Joy-Con (24 bytes) ───────────────────────────────────
        public static byte[] BuildSwitchPro(ViiperXInputGamepad gp)
        {
            var data = new byte[24];
            uint buttons = 0;

            // Face buttons — positional (Xbox A → Switch B, both bottom).
            if ((gp.Buttons & ViiperXInput.A) != 0) buttons |= 0x000004;  // B (bottom)
            if ((gp.Buttons & ViiperXInput.B) != 0) buttons |= 0x000008;  // A (right)
            if ((gp.Buttons & ViiperXInput.X) != 0) buttons |= 0x000001;  // Y (left)
            if ((gp.Buttons & ViiperXInput.Y) != 0) buttons |= 0x000002;  // X (top)
            if ((gp.Buttons & ViiperXInput.LB) != 0) buttons |= 0x400000; // L
            if ((gp.Buttons & ViiperXInput.RB) != 0) buttons |= 0x000040; // R

            // Switch has no analog triggers — map >50% press to ZL/ZR digital.
            if (gp.LeftTrigger > 128) buttons |= 0x800000;  // ZL
            if (gp.RightTrigger > 128) buttons |= 0x000080; // ZR

            if ((gp.Buttons & ViiperXInput.Back) != 0) buttons |= 0x000100;       // Minus
            if ((gp.Buttons & ViiperXInput.Start) != 0) buttons |= 0x000200;      // Plus
            if ((gp.Buttons & ViiperXInput.Guide) != 0) buttons |= 0x001000;      // Home
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) buttons |= 0x000800;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) buttons |= 0x000400;

            if ((gp.Buttons & ViiperXInput.DPadUp) != 0)    buttons |= 0x020000;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0)  buttons |= 0x010000;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0)  buttons |= 0x080000;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) buttons |= 0x040000;

            WriteU32(data, 0, buttons);

            WriteI16(data, 4, gp.ThumbLX);
            WriteI16(data, 6, gp.ThumbLY);
            WriteI16(data, 8, gp.ThumbRX);
            WriteI16(data, 10, gp.ThumbRY);
            // Bytes 12-23 are IMU (gyro XYZ + accel XYZ) — left zeroed.
            return data;
        }

        // ── Scaling / byte helpers (ported 1:1 from ViiperInputForwarder) ─────
        private static int NegateClamp(short value)
        {
            int neg = -(int)value;
            if (neg > short.MaxValue) neg = short.MaxValue;
            if (neg < short.MinValue) neg = short.MinValue;
            return neg;
        }

        private static short ScaleDs4Accel(short raw)
        {
            int scaled = raw * 2;
            if (scaled > short.MaxValue) return short.MaxValue;
            if (scaled < short.MinValue) return short.MinValue;
            return (short)scaled;
        }

        /// <summary>Legion touchpad raw range is 0-1023 (10-bit). Scale to the host's max and clamp.</summary>
        private static ushort ScaleTouchAxis(ushort raw, int maxOut)
        {
            int scaled = raw * maxOut / 1023;
            if (scaled < 0) return 0;
            if (scaled > maxOut) return (ushort)maxOut;
            return (ushort)scaled;
        }

        private static void WriteU16(byte[] buf, int offset, ushort value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteI16(byte[] buf, int offset, short value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteU32(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
