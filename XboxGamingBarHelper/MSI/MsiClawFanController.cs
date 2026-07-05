using System;
using NLog;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// MSI Claw 8 AI+ (A2VM, Intel Lunar Lake) fan control via the ACPI-WMI platform interface
    /// (root\WMI / MSI_ACPI, ACPI\PNP0C14\0_0 — firmware/EC level, works with MSI Center services off).
    ///
    /// Model (reverse-engineered from the real firmware, see Doku/RE_MSI_FanCurve.md):
    ///   - The fan curve is TWO parallel EC tables per fan block:
    ///       Set_Fan     (8 bytes) = duty %, RAW EC byte (MSI shows the raw byte as "%", NO ×1.5).
    ///                     [0] backup(=d1) [1] 0°C=0 [2..6] duty @ breakpoints [7] = [6] (dup).
    ///       Set_Thermal (7 bytes) = the temperature breakpoints °C: [0, t1..t5, t5(dup)].
    ///     So a curve is 5 editable (temp,duty) points on the MSI axis (default temps 44/54/64/74/82).
    ///   - "Software" fan mode = our tables are followed and fan-control is enabled (AP bit7).
    ///   - "Hardware" fan mode = a baseline table is written and the firmware keeps control.
    ///   - Both physical fans (block 1 + block 2) are written IDENTICALLY (we expose one curve).
    /// </summary>
    internal static class MsiClawFanController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ── A2VM Lunar Lake hardware fan tables (raw EC bytes) ─────────────────────
        // Used only for the firmware hand-back ("fan off" / disabled). 8 bytes on the legacy
        // assumed-axis layout — these are baseline firmware tables, not the software curve model.
        private static readonly byte[] LLFanTable_BetterBattery     = { 0, 0, 0,  0, 18, 48,  90, 140 };
        private static readonly byte[] LLFanTable_BetterPerformance = { 0, 0, 0, 14, 33, 63,  98, 150 };
        private static readonly byte[] LLFanTable_BestPerformance   = { 10, 0, 10, 26, 46, 78, 113, 150 };

        // ── MSI software fan curves: 5 (temp,duty) points on the real firmware axis ──────
        // Temps default to the MSI Center M breakpoints [44,54,64,74,82] °C; duty is the RAW EC byte
        // 0–100 (MSI's own default caps at 75). Presets 0/1 share the default axis and differ only in
        // duty; "Cooling" shifts the whole axis down 10 °C so the fan spins earlier.
        public static readonly int[] MsiTemps_Default = { 44, 54, 64, 74, 82 };
        public static readonly int[] MsiTemps_Cooling = { 34, 44, 54, 64, 72 }; // −10 °C: early ramp

        public static readonly int[] MsiDuty_Default   = { 40, 49, 58, 67, 75 }; // MSI Center M default
        public static readonly int[] MsiDuty_QuietIdle = { 20, 30, 45, 67, 75 }; // quieter low band, MSI top
        public static readonly int[] MsiDuty_Cooling   = { 40, 49, 58, 67, 75 }; // same duty, earlier temps

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
        /// Apply an MSI software fan curve: 5 (temp,duty) points on the real firmware axis. Writes the
        /// duty table (Set_Fan) AND the temperature breakpoints (Set_Thermal) to BOTH fan blocks, then
        /// hands fan control to our tables. Duty is the RAW EC byte 0–100 (MSI scale, no ×1.5).
        /// temps/duties must each have 5 entries; temps strictly increasing, duties clamped 0–100.
        /// </summary>
        public static bool ApplyMsiCurve(int[] temps5, int[] duties5)
        {
            if (temps5 == null || temps5.Length < 5 || duties5 == null || duties5.Length < 5)
            {
                Logger.Warn("MsiClawFanController.ApplyMsiCurve: temps and duties must each have 5 points");
                return false;
            }

            byte[] fan = BuildFanTable(duties5);
            byte[] thermal = BuildThermalTable(temps5);

            SetThermalTable(thermal); // temperature breakpoints (X-axis) — MSI's fixed axis, now editable
            SetFanTable(fan);         // duty per breakpoint (Y-axis)
            SetFanControl(true);      // software mode → EC follows our tables
            // Critical: clear the "full speed" override (block 152 bit7). If MSI Center or a prior state
            // left it set, the fan runs 100% regardless of our table — the EC ignores the curve entirely.
            SetFanFullSpeed(false);
            Logger.Info($"MsiClawFanController: applied MSI fan curve fan=[{string.Join(",", fan)}] thermal=[{string.Join(",", thermal)}] (full-speed cleared)");
            return true;
        }

        /// <summary>Builds the 8-byte Set_Fan duty table from 5 duty %: [0, 0, d1..d5, d5(dup)].
        /// Byte0 is 0 to match MSI Center M exactly — MSI always writes 0 in the CPU-fan block's
        /// leading byte (in both Auto and Custom mode), so we mirror that instead of duplicating d1
        /// there (the old layout, which diverged from MSI on the CPU block).</summary>
        public static byte[] BuildFanTable(int[] duties5)
        {
            byte D(int i) => (byte)Math.Max(0, Math.Min(100, duties5[i]));
            return new byte[8] { 0, 0, D(0), D(1), D(2), D(3), D(4), D(4) };
        }

        /// <summary>Builds the 7-byte Set_Thermal breakpoint table from 5 temps °C: [0, t1..t5, t5(dup)].</summary>
        public static byte[] BuildThermalTable(int[] temps5)
        {
            byte T(int i) => (byte)Math.Max(0, Math.Min(120, temps5[i]));
            return new byte[7] { 0, T(0), T(1), T(2), T(3), T(4), T(4) };
        }

        /// <summary>
        /// MSI-clean firmware hand-back — replicates exactly what MSI Center M leaves behind in its
        /// "Auto" mode: the MSI default duty curve (40/49/58/67/75 on the 44/54/64/74/82 axis, byte0=0)
        /// written to BOTH fan blocks, full-speed override cleared, and fan-control OFF (212 bit7=0) so
        /// the firmware regulates. Unlike <see cref="ApplyHardwareTable"/> this uses the real MSI-axis
        /// 0–100 layout — no legacy ×1.5 / wrong-axis bytes (e.g. 150) that could leave the EC in a
        /// state where a fan won't spin. This is the correct way to DISABLE our software fan control.
        /// </summary>
        public static bool ApplyFirmwareAutoBaseline()
        {
            byte[] fan = BuildFanTable(MsiDuty_Default);
            byte[] thermal = BuildThermalTable(MsiTemps_Default);

            SetFanFullSpeed(false);   // never hand back with a latched full-speed override
            SetThermalTable(thermal); // MSI default temperature axis
            SetFanTable(fan);         // MSI default duty curve (byte0=0, MSI-identical)
            SetFanControl(false);     // firmware/Auto → EC regulates (212 bit7 = 0)
            Logger.Info($"MsiClawFanController: firmware hand-back (MSI Auto baseline) fan=[{string.Join(",", fan)}] thermal=[{string.Join(",", thermal)}] control=OFF");
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

        /// <summary>Reads the current temperature-breakpoint axis (Get_Thermal block 1) as 7 bytes
        /// [0, t1..t5, t5]. Returns a 7-byte array (zeros on failure).</summary>
        public static byte[] ReadThermal()
        {
            var result = new byte[7];
            try
            {
                byte[] th = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Thermal", 1, 32, out bool ok);
                if (ok && th != null) Array.Copy(th, result, Math.Min(7, th.Length));
            }
            catch (Exception ex) { Logger.Debug($"MsiClawFan.ReadThermal: {ex.Message}"); }
            return result;
        }

        /// <summary>Maps a 5-point (duty %) curve to the 8-byte EC table — the same mapping used when
        /// writing, so callers (e.g. the widget's "Check applied values") can compute the expected
        /// table for verification. Alias of <see cref="BuildFanTable"/>.</summary>
        public static byte[] CurveToTable(int[] duties5) => BuildFanTable(duties5);

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

        /// <summary>Writes the 7-byte temperature-breakpoint table (Set_Thermal) to both fan blocks and
        /// reads it back, so the temperature axis (X) is verifiable. Mirrors <see cref="SetFanTable"/>.
        /// This is the differentiator over MSI Center M, which keeps the temp axis fixed.</summary>
        private static void SetThermalTable(byte[] thermalTable)
        {
            for (byte iDataBlockIndex = 1; iDataBlockIndex <= 2; iDataBlockIndex++)
            {
                string blk = iDataBlockIndex == 1 ? "CPU" : "GPU";

                byte[] before = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Thermal", iDataBlockIndex, 32, out bool readBefore);
                Logger.Info($"MsiClawThermal[{blk}]: before write (read ok={readBefore}) axis=[{Hex(before, 7)}]");

                byte[] fullPackage = new byte[32];
                fullPackage[0] = iDataBlockIndex;
                Array.Copy(thermalTable, 0, fullPackage, 1, thermalTable.Length);
                Logger.Info($"MsiClawThermal[{blk}]: writing axis=[{string.Join(",", thermalTable)}]");

                var setResult = MsiClawWmi.Set(MsiClawWmi.Scope, MsiClawWmi.Path, "Set_Thermal", fullPackage);
                if (setResult == null)
                    Logger.Warn($"MsiClawThermal[{blk}]: Set_Thermal returned null (WMI call may have failed)");

                byte[] after = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path, "Get_Thermal", iDataBlockIndex, 32, out bool readAfter);
                bool matches = readAfter && after.Length >= 7 && ArrayPrefixMatches(thermalTable, after, 7);
                Logger.Info($"MsiClawThermal[{blk}]: read-back (ok={readAfter}) axis=[{Hex(after, 7)}] -> {(matches ? "MATCH" : "MISMATCH")}");
            }
        }

        private static bool ArrayPrefixMatches(byte[] wanted, byte[] readBack, int count)
        {
            if (wanted == null || readBack == null || wanted.Length < count || readBack.Length < count) return false;
            for (int i = 0; i < count; i++)
                if (wanted[i] != readBack[i]) return false;
            return true;
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
