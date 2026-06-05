using NLog;
using XboxGamingBarHelper.MSI;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Controls the MSI Claw battery charge limit via ACPI-WMI.
    /// Ported from Handheld Companion fork: ClawA1M.cs SetBatteryMaster() /
    /// SetBatteryChargeLimit() / GetBatteryChargeLimit().
    ///
    /// Data-block layout for index 215:
    ///   Bit 7       : enable flag (1=limit active, 0=disabled)
    ///   Bits 6-0    : charge-limit percentage (60/80/100)
    /// </summary>
    internal static class MsiClawBatteryManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>WMI data-block index for battery charge settings (HC: dataBlockIndex=215).</summary>
        private const byte BatteryDataBlock = 215;

        // Supported percentages (HC: min=60, max=100, step=20)
        public const int MinPercent  = 60;
        public const int MaxPercent  = 100;
        public const int StepPercent = 20;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Enables or disables the charge-limit feature (ACPI bit 7 of data block 215).
        /// When disabled the battery charges to 100% as normal.
        /// </summary>
        public static bool SetEnabled(bool enable)
        {
            byte[] data = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path,
                                         "Get_Data", BatteryDataBlock, 1, out bool ok);
            if (!ok || data == null || data.Length == 0)
            {
                Logger.Warn("[BattMgr] SetEnabled: failed to read data block");
                return false;
            }

            byte current = data[0];
            byte updated = enable
                ? (byte)(current | 0x80)   // set bit 7
                : (byte)(current & 0x7F);  // clear bit 7

            var pkg = new byte[32];
            pkg[0] = BatteryDataBlock;
            pkg[1] = updated;

            bool success = MsiClawWmi.Set(MsiClawWmi.Scope, MsiClawWmi.Path, "Set_Data", pkg) != null;
            Logger.Info($"[BattMgr] SetEnabled({enable}): byte {current:X2} → {updated:X2}, ok={success}");
            return success;
        }

        /// <summary>
        /// Sets the charge-limit percentage (bits 6-0 of data block 215).
        /// Valid values: 60, 80, 100 (step=20). The enable flag is preserved.
        /// </summary>
        public static bool SetPercent(int percent)
        {
            if (percent < MinPercent || percent > MaxPercent || percent % StepPercent != 0)
            {
                Logger.Warn($"[BattMgr] SetPercent: invalid value {percent} (must be 60/80/100)");
                return false;
            }

            byte[] data = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path,
                                         "Get_Data", BatteryDataBlock, 1, out bool ok);
            if (!ok || data == null || data.Length == 0)
            {
                Logger.Warn("[BattMgr] SetPercent: failed to read data block");
                return false;
            }

            byte current = data[0];
            byte enableBit = (byte)(current & 0x80);     // preserve bit 7
            byte updated   = (byte)(enableBit | (byte)(percent & 0x7F));

            var pkg = new byte[32];
            pkg[0] = BatteryDataBlock;
            pkg[1] = updated;

            bool success = MsiClawWmi.Set(MsiClawWmi.Scope, MsiClawWmi.Path, "Set_Data", pkg) != null;
            Logger.Info($"[BattMgr] SetPercent({percent}%): byte {current:X2} → {updated:X2}, ok={success}");
            return success;
        }

        /// <summary>
        /// Reads the current charge-limit configuration from ACPI.
        /// Returns the percentage (60/80/100) and sets <paramref name="enabled"/> accordingly.
        /// Returns 80 as a safe default on read failure.
        /// </summary>
        public static int GetConfig(out bool enabled)
        {
            enabled = false;
            byte[] data = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path,
                                         "Get_Data", BatteryDataBlock, 1, out bool ok);
            if (!ok || data == null || data.Length == 0)
            {
                Logger.Warn("[BattMgr] GetConfig: failed to read data block — returning defaults");
                return 80;
            }

            enabled = (data[0] & 0x80) != 0;
            int pct = data[0] & 0x7F;

            // Clamp/round to nearest valid step
            if (pct <= 70) pct = 60;
            else if (pct <= 90) pct = 80;
            else pct = 100;

            Logger.Debug($"[BattMgr] GetConfig: byte={data[0]:X2} → enabled={enabled}, percent={pct}");
            return pct;
        }
    }
}
