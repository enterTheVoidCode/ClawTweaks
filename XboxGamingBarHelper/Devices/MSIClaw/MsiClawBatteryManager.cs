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
    ///   Bits 6-0    : charge-limit percentage (granular; any value in [MinPercent..100])
    /// </summary>
    internal static class MsiClawBatteryManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>WMI data-block index for battery charge settings (HC: dataBlockIndex=215).</summary>
        private const byte BatteryDataBlock = 215;

        // Granular range. The EC byte carries the percentage in bits 6-0, so any value up to 100
        // is representable. We keep a sane floor of 20% (matches the msi-ec valid range 10-100).
        public const int MinPercent  = 20;
        public const int MaxPercent  = 100;
        public const int StepPercent = 5;

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
        /// Sets the charge-limit percentage (bits 6-0 of data block 215). Granular: any value in
        /// [MinPercent..MaxPercent] is accepted and clamped. The enable flag (bit 7) is preserved.
        /// </summary>
        public static bool SetPercent(int percent)
        {
            // Clamp into the valid range rather than rejecting — granular slider sends any value.
            if (percent < MinPercent) percent = MinPercent;
            if (percent > MaxPercent) percent = MaxPercent;

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
        /// Reads the current charge-limit configuration straight from the ACPI EC byte (the actual
        /// hardware value — same read-back pattern as the fan controller). Returns the raw percentage
        /// and sets <paramref name="enabled"/> from bit 7. <paramref name="readOk"/> is false when the
        /// EC read failed (e.g. device not ready), so the caller can show "unknown" instead of a guess.
        /// </summary>
        public static int GetConfig(out bool enabled, out bool readOk)
        {
            enabled = false;
            byte[] data = MsiClawWmi.Get(MsiClawWmi.Scope, MsiClawWmi.Path,
                                         "Get_Data", BatteryDataBlock, 1, out bool ok);
            if (!ok || data == null || data.Length == 0)
            {
                Logger.Warn("[BattMgr] GetConfig: failed to read data block");
                readOk = false;
                return 80;
            }

            readOk  = true;
            enabled = (data[0] & 0x80) != 0;
            int pct = data[0] & 0x7F;
            if (pct < 0)   pct = 0;
            if (pct > 100) pct = 100;

            Logger.Debug($"[BattMgr] GetConfig: byte={data[0]:X2} → enabled={enabled}, percent={pct}");
            return pct;
        }

        /// <summary>Back-compat overload (ignores read-success).</summary>
        public static int GetConfig(out bool enabled) => GetConfig(out enabled, out _);
    }
}
