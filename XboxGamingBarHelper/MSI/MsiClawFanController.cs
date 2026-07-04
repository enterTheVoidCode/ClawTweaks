using System;
using NLog;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// MSI Claw 8 AI+ (A2VM, Intel Lunar Lake) fan control via the ACPI-WMI platform interface.
    /// Ported from the Handheld Companion fork (ClawA1M + the A2VM Lunar Lake optimizations).
    ///
    /// Concepts:
    ///   - The EC fan table is 8 bytes on a 0–150 scale, sampled at fixed temperatures:
    ///     [0] backup(40°C) [1] 0°C [2] 20°C [3] 50°C [4] 60°C [5] 80°C [6] 90°C [7] 100°C
    ///   - "Software" fan mode = our table is followed and fan-control is enabled.
    ///   - "Hardware" fan mode = a baseline table is written and the firmware keeps control.
    ///   - Lunar Lake is near-fanless at idle; A2VM tables/curves keep the fan off at low
    ///     temps and ramp gently, instead of the louder firmware default seen with Center M off.
    /// </summary>
    internal static class MsiClawFanController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ── A2VM Lunar Lake hardware fan tables (0–150 MSI scale) ──────────────────
        // [0] backup  [1] 0°C  [2] 20°C  [3] 50°C  [4] 60°C  [5] 80°C  [6] 90°C  [7] 100°C
        private static readonly byte[] LLFanTable_BetterBattery     = { 0, 0, 0,  0, 18, 48,  90, 140 };
        private static readonly byte[] LLFanTable_BetterPerformance = { 0, 0, 0, 14, 33, 63,  98, 150 };
        private static readonly byte[] LLFanTable_BestPerformance   = { 10, 0, 10, 26, 46, 78, 113, 150 };

        // ── A2VM Lunar Lake software fan curves (11 points: 0,10,…,100 °C → 0–100 %) ──
        // Non-decreasing, silent at idle/low load. The KEY tuning constraint: the 80-100 °C end must
        // cool at firmware level. If our curve under-cools at the top during sustained full load, the
        // CPU climbs into the EC's thermal-protection region; the EC then seizes the fan with its own
        // max override (full-speed bit / active-scenario fan), runs LOUDER than both our table and the
        // firmware curve, and LATCHES there — only a firmware hand-back clears it. Re-writing or
        // lowering our table does NOT release the latch (and lowering cools even less). So we keep the
        // 0-60 °C band quiet but ramp the 80/90/100 °C points to ≥ firmware so the trip never happens.
        // With FanTableScale = 150 these map to the full EC 0-150 range, e.g. resulting EC tables:
        //   Quiet      80/90/100 °C -> 60/94/130   Default 75/112/145   Aggressive 94/127/150
        // (firmware reference: BetterPerformance 63/98/150, BestPerformance 78/113/150).
        //                                                 0  10  20  30  40  50  60  70  80  90  100 °C
        public static readonly double[] Curve_Quiet      = { 0, 0, 0,  0,  0,  0,  8, 22, 40, 63,  87 };
        public static readonly double[] Curve_Default    = { 0, 0, 0,  0,  2,  4, 14, 30, 50, 75,  97 };
        public static readonly double[] Curve_Aggressive = { 0, 0, 0,  3,  6, 10, 22, 40, 63, 85, 100 };

        // "Cooling" (early-ramp): the EC table is temperature-indexed and only samples
        // 0/20/40/50/60/80/90/100 °C — at idle/low load the fan speed comes ONLY from the 50/60 °C
        // points. Quiet/Default keep 50-60 °C near silent, so the fan barely moves until ~80 °C and a
        // game's fast heat-up overshoots into the EC's ~90 °C thermal-protection latch. This preset
        // front-loads the 50-70 °C band so the fan is already spinning before the game pushes past 80,
        // keeping temp under the ~85 °C latch threshold, while staying silent at <=40 °C (idle).
        // Resulting EC table (x1.5): 50 °C->33, 60 °C->57, 80 °C->97, 90 °C->127, 100 °C->150.
        //                                                 0  10  20  30  40  50  60  70  80  90  100 °C
        public static readonly double[] Curve_Cooling    = { 0, 0, 0,  0,  3, 22, 38, 50, 65, 85, 100 };

        /// <summary>
        /// EXPERIMENTAL: read the CURRENT native fan config straight from the EC and format a report.
        /// Unlike the write-only LED path, the EC is readable — so this reflects whatever set the fan
        /// last, INCLUDING MSI Center M. Reads the CPU fan table (block 1) + control bit, the
        /// power-shift scenario (210), the software-control enable (212) and the full-speed override
        /// (152.7), and matches the table to a known preset when possible. Used by the Debug harness to
        /// learn each MSI Center M fan mode by its EC signature (set mode in MSI Center M → read here).
        /// </summary>
        public static string DetectNativeReport()
        {
            try
            {
                ReadStatus(out byte[] cpu, out bool ctrlOn);
                int shift  = ReadDataBlock(210);
                int enable = ReadDataBlock(212);
                bool full  = ReadFullSpeedBit();

                string shiftName =
                    shift == 0xC0 ? "Comfort/None" :
                    shift == 0xC1 ? "Green" :
                    shift == 0xC2 ? "ECO" :
                    shift == 0xC4 ? "Sport" :
                    shift < 0     ? "n/a" : $"0x{shift:X2}";

                string cpuHex = "n/a";
                if (cpu != null && cpu.Length > 0)
                {
                    var csb = new System.Text.StringBuilder();
                    for (int i = 0; i < 8 && i < cpu.Length; i++) { if (i > 0) csb.Append(','); csb.Append(cpu[i]); }
                    cpuHex = csb.ToString();
                }

                return $"control={(ctrlOn ? "software (our curve)" : "firmware/EC")}; " +
                       $"scenario(210)={shiftName}; enableBit(212)={(enable < 0 ? "n/a" : "0x" + enable.ToString("X2"))}; " +
                       $"fullSpeed(152.7)={(full ? "ON" : "off")}; preset~={MatchFanTable(cpu)}; cpuTable(1)=[{cpuHex}]";
            }
            catch (System.Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        private static string MatchFanTable(byte[] t)
        {
            if (t == null || t.Length < 8) return "unknown";
            bool Eq(byte[] a) { for (int i = 0; i < 8; i++) if (t[i] != a[i]) return false; return true; }
            if (Eq(LLFanTable_BetterBattery))     return "BetterBattery (firmware)";
            if (Eq(LLFanTable_BetterPerformance)) return "BetterPerformance (firmware)";
            if (Eq(LLFanTable_BestPerformance))   return "BestPerformance (firmware)";
            return "custom";
        }

        /// <summary>
        /// A2VM fan scale: the 0–100 % UI curve maps onto the full MSI EC range 0–150, so 100 % UI =
        /// MSI 150 = hardware max. This was 100 (≈67 % of HW max), which structurally capped software
        /// cooling below the firmware curve and, under sustained full load, let the CPU climb until the
        /// EC seized the fan and latched it loud (see curve comment above). Full range is required so
        /// the high-temp end of the curves can actually deliver firmware-level cooling and keep the EC
        /// from ever taking over. Quiet at low/mid load is preserved by the curve shape, not by capping
        /// the scale.
        /// </summary>
        private const double FanTableScale = 150.0;

        /// <summary>
        /// Apply a software fan curve (11 points, 0…100 °C, values 0–100 %) and hand fan
        /// control to our table. Maps the 11-point curve to the 8-byte EC table exactly like
        /// the HC fork (PowerProfileManager_Applied).
        /// </summary>
        public static bool ApplySoftwareCurve(double[] curve11)
        {
            if (curve11 == null || curve11.Length < 11)
            {
                Logger.Warn("MsiClawFanController.ApplySoftwareCurve: curve must have 11 points");
                return false;
            }

            double scale = FanTableScale / 100.0d;
            byte[] table = new byte[8];
            table[0] = (byte)(curve11[4]  * scale);   // 40°C (backup)
            table[1] = (byte)(curve11[0]  * scale);   // 0°C
            table[2] = (byte)(curve11[2]  * scale);   // 20°C
            table[3] = (byte)(curve11[5]  * scale);   // 50°C
            table[4] = (byte)(curve11[6]  * scale);   // 60°C
            table[5] = (byte)(curve11[8]  * scale);   // 80°C
            table[6] = (byte)(curve11[9]  * scale);   // 90°C
            table[7] = (byte)(curve11[10] * scale);   // 100°C

            SetFanTable(table);
            SetFanControl(true);   // software mode → EC follows our table
            // Critical: clear the "full speed" override (block 152 bit7). If MSI Center or a
            // prior state left it set, the fan runs 100% regardless of our table — the EC
            // ignores the curve entirely. Without this, even a Quiet curve can be deafening.
            SetFanFullSpeed(false);
            Logger.Info($"MsiClawFanController: applied software fan curve [{string.Join(",", table)}] (full-speed cleared)");
            return true;
        }

        /// <summary>
        /// Clean firmware hand-back: write a real baseline firmware fan table AND return control to the
        /// firmware (hardware mode), clearing any forced full-speed override first. This is the robust
        /// way to leave software fan mode — unlike a bare control-bit clear, it does NOT leave our last
        /// (possibly quiet) software table in the EC. So even if the firmware keeps reading the data
        /// block in hardware mode, it now reads a sane firmware curve instead of our leftover bytes.
        /// profileKey: "BetterBattery" | "BetterPerformance" | "BestPerformance" (default: BetterPerformance).
        /// </summary>
        public static bool ApplyHardwareTable(string profileKey)
        {
            byte[] table;
            switch (profileKey)
            {
                case "BetterBattery":   table = LLFanTable_BetterBattery;   break;
                case "BestPerformance": table = LLFanTable_BestPerformance; break;
                default:                table = LLFanTable_BetterPerformance; break;
            }
            // Clear any leftover full-speed override (e.g. from the Full Blast diagnostic) so handing
            // back to the firmware never leaves the fan pinned to max.
            SetFanFullSpeed(false);
            SetFanTable(table);
            SetFanControl(false);  // hardware mode → firmware keeps control
            Logger.Info($"MsiClawFanController: firmware hand-back — applied hardware fan table '{profileKey}' [{string.Join(",", table)}] (full-speed cleared)");
            return true;
        }

        /// <summary>
        /// Reads back the current CPU fan table (8 bytes) and the fan-control enable bit so the
        /// widget can verify the applied values against the graph. Returns false if the read failed.
        /// </summary>
        public static bool ReadStatus(out byte[] cpuTable, out bool controlOn)
        {
            cpuTable = new byte[8];
            controlOn = false;
            try
            {
                byte[] fan = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Fan", 1, 32, out bool okFan);
                if (okFan && fan.Length >= 8) Array.Copy(fan, cpuTable, 8);

                byte[] ap = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_AP", 1, MsiClawWmi.GetAPLength(1), out bool okAp);
                controlOn = okAp && ap.Length > 0 && ((ap[0] & (1 << 7)) != 0);

                Logger.Info($"MsiClawFan: ReadStatus table=[{Hex(cpuTable, 8)}] controlOn={controlOn} (read ok={okFan})");
                return okFan;
            }
            catch (Exception ex)
            {
                Logger.Warn($"MsiClawFan.ReadStatus: {ex.Message}");
                return false;
            }
        }

        /// <summary>Maps an 11-point curve (0…100 °C, 0–100 %) to the 8-byte EC table — the same
        /// mapping used when writing, so callers can compute the "expected" table for verification.</summary>
        public static byte[] CurveToTable(double[] curve11)
        {
            double scale = FanTableScale / 100.0d;
            byte[] t = new byte[8];
            if (curve11 == null || curve11.Length < 11) return t;
            t[0] = (byte)(curve11[4]  * scale);
            t[1] = (byte)(curve11[0]  * scale);
            t[2] = (byte)(curve11[2]  * scale);
            t[3] = (byte)(curve11[5]  * scale);
            t[4] = (byte)(curve11[6]  * scale);
            t[5] = (byte)(curve11[8]  * scale);
            t[6] = (byte)(curve11[9]  * scale);
            t[7] = (byte)(curve11[10] * scale);
            return t;
        }

        /// <summary>Return fan control to the firmware (used on shutdown / revert).</summary>
        public static void RestoreFirmwareControl()
        {
            // Clear the full-speed override first so handing back to firmware leaves NO leftover
            // override engaged — otherwise a latched full-speed bit keeps the fan high even though
            // the firmware curve has resumed (the "got quieter but never reached idle" symptom).
            SetFanFullSpeed(false);
            SetFanControl(false);
            Logger.Info("MsiClawFanController: fan control returned to firmware (full-speed cleared)");
        }

        // ── Low-level WMI operations (ported 1:1 from ClawA1M) ─────────────────────

        /// <summary>Writes the 8-byte fan table to both CPU (block 1) and GPU (block 2), then
        /// reads it back and logs the comparison so the actually-applied values are verifiable.</summary>
        private static void SetFanTable(byte[] fanTable)
        {
            for (byte iDataBlockIndex = 1; iDataBlockIndex <= 2; iDataBlockIndex++)
            {
                string blk = iDataBlockIndex == 1 ? "CPU" : "GPU";

                // Read current (keeps firmware happy) then write the full 32-byte package.
                byte[] before = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Fan", iDataBlockIndex, 32, out bool readBefore);
                Logger.Info($"MsiClawFan[{blk}]: before write (read ok={readBefore}) table=[{Hex(before, 8)}]");

                byte[] fullPackage = new byte[32];
                fullPackage[0] = iDataBlockIndex;
                Array.Copy(fanTable, 0, fullPackage, 1, fanTable.Length);
                Logger.Info($"MsiClawFan[{blk}]: writing table=[{string.Join(",", fanTable)}]");

                var setResult = MsiClawWmi.Set(MsiClawWmi.Scope, MsiClawWmi.Path, "Set_Fan", fullPackage);
                if (setResult == null)
                    Logger.Warn($"MsiClawFan[{blk}]: Set_Fan returned null (WMI call may have failed)");

                // Verify: read the table back and compare to what we asked for.
                byte[] after = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Fan", iDataBlockIndex, 32, out bool readAfter);
                bool matches = readAfter && after.Length >= 8 && TableMatches(fanTable, after);
                Logger.Info($"MsiClawFan[{blk}]: read-back (ok={readAfter}) table=[{Hex(after, 8)}] -> {(matches ? "MATCH" : "MISMATCH")}");
            }
        }

        /// <summary>Enables (software) / disables (hardware) custom fan control. Logs the
        /// AP bit before/after so the applied control state is verifiable.</summary>
        public static void SetFanControl(bool enable)
        {
            byte iDataBlockIndex = 1;
            byte[] data = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_AP", iDataBlockIndex,
                MsiClawWmi.GetAPLength(iDataBlockIndex), out bool readSuccess);

            byte value = (readSuccess && data.Length > 0) ? data[0] : (byte)0;
            byte newValue = SetBit(value, 7, enable);
            Logger.Info($"MsiClawFan: SetFanControl({enable}) Get_AP ok={readSuccess} cur=0x{value:X2} -> 0x{newValue:X2}");

            byte[] fullPackage = new byte[32];
            fullPackage[0] = 212;   // fan-control enable data block
            fullPackage[1] = newValue;
            var setResult = MsiClawWmi.Set(MsiClawWmi.Scope, MsiClawWmi.Path, "Set_Data", fullPackage);
            if (setResult == null)
                Logger.Warn("MsiClawFan: SetFanControl Set_Data returned null");

            // Verify the AP bit took.
            byte[] verify = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_AP", iDataBlockIndex,
                MsiClawWmi.GetAPLength(iDataBlockIndex), out bool verifyOk);
            bool bitSet = verifyOk && verify.Length > 0 && ((verify[0] & (1 << 7)) != 0);
            Logger.Info($"MsiClawFan: SetFanControl verify (ok={verifyOk}) AP0=0x{(verify.Length > 0 ? verify[0] : (byte)0):X2} bit7={bitSet} (wanted {enable})");
        }

        private static bool TableMatches(byte[] wanted, byte[] readBack)
        {
            for (int i = 0; i < 8; i++)
                if (wanted[i] != readBack[i]) return false;
            return true;
        }

        private static string Hex(byte[] data, int count)
        {
            if (data == null) return "";
            int n = Math.Min(count, data.Length);
            var parts = new string[n];
            for (int i = 0; i < n; i++) parts[i] = data[i].ToString();
            return string.Join(",", parts);
        }

        /// <summary>Reads the "full speed" override (block 152, bit 7). True = the EC is forcing the fan
        /// to absolute max regardless of the curve table. Used by the diagnostics to compare our table
        /// max (=150) against the EC's true full-speed ceiling.</summary>
        public static bool ReadFullSpeedBit()
        {
            try
            {
                byte[] data = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Data", 152, 1, out bool ok);
                return ok && data.Length > 0 && ((data[0] & (1 << 7)) != 0);
            }
            catch (Exception ex)
            {
                Logger.Debug($"MsiClawFan.ReadFullSpeedBit: {ex.Message}");
                return false;
            }
        }

        /// <summary>Diagnostic: write a raw byte to an EC data block (Set_Data). Used by the fan-override
        /// probe to hunt for a proportional fan-duty register (e.g. block 152 low bits, or a sibling).</summary>
        public static void WriteDataBlock(byte block, byte value)
        {
            byte[] pkg = new byte[32];
            pkg[0] = block;
            pkg[1] = value;
            MsiClawWmi.Set(MsiClawWmi.Scope, MsiClawWmi.Path, "Set_Data", pkg);
            Logger.Info($"MsiClawFan: WriteDataBlock({block}) = {value} (0x{value:X2})");
        }

        /// <summary>Diagnostic: read one byte from an EC data block (Get_Data). Returns -1 on failure.</summary>
        public static int ReadDataBlock(byte block)
        {
            byte[] d = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Data", block, 1, out bool ok);
            return (ok && d.Length > 0) ? d[0] : -1;
        }

        /// <summary>Forces the fan to full speed (block 152, bit 7).</summary>
        public static void SetFanFullSpeed(bool enable)
        {
            byte iDataBlockIndex = 152;
            byte[] data = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Data", iDataBlockIndex, 1, out bool readSuccess);

            byte value = (readSuccess && data.Length > 0) ? data[0] : (byte)0;
            value = SetBit(value, 7, enable);

            byte[] fullPackage = new byte[32];
            fullPackage[0] = iDataBlockIndex;
            fullPackage[1] = value;
            MsiClawWmi.Set(MsiClawWmi.Scope, MsiClawWmi.Path, "Set_Data", fullPackage);
        }

        private static byte SetBit(byte value, int bit, bool on)
        {
            return on ? (byte)(value | (1 << bit)) : (byte)(value & ~(1 << bit));
        }
    }
}
