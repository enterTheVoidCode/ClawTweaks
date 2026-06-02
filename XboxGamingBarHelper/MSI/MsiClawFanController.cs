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
        // Strictly ascending, starting at 0 so the fan is silent until the CPU is warm.
        public static readonly double[] Curve_Quiet      = { 0, 0, 0,  0,  0, 12, 22, 38, 58,  80, 100 };
        public static readonly double[] Curve_Default    = { 0, 0, 0,  0,  5, 20, 40, 62, 82, 100, 100 };
        public static readonly double[] Curve_Aggressive = { 0, 0, 0,  5, 15, 35, 55, 75, 90, 100, 100 };

        /// <summary>
        /// A2VM fan scale: HC's 0–100 % UI is mapped onto MSI 0–100 (instead of 0–150),
        /// giving more adjustment room in the quiet range. 100 % UI = MSI 100 ≈ 67 % of
        /// hardware max — ample for Lunar Lake's lower sustained power envelope.
        /// </summary>
        private const double FanTableScale = 100.0;

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
            Logger.Info($"MsiClawFanController: applied software fan curve [{string.Join(",", table)}]");
            return true;
        }

        /// <summary>
        /// Write a baseline hardware fan table for the given profile and return control to
        /// the firmware (hardware mode). profileKey: "BetterBattery" | "BetterPerformance"
        /// | "BestPerformance" (default: BetterPerformance).
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
            SetFanTable(table);
            SetFanControl(false);  // hardware mode → firmware keeps control
            Logger.Info($"MsiClawFanController: applied hardware fan table '{profileKey}' [{string.Join(",", table)}]");
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
            SetFanControl(false);
            Logger.Info("MsiClawFanController: fan control returned to firmware");
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
