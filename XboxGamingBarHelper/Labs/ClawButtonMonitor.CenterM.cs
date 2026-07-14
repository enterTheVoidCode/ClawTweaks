using System;
using System.Threading;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// "Tiny Center M" — read/write the MSI Claw's hardware controller config (stick + trigger
    /// deadzones/limits, stick swap, hardware gyro) directly in firmware, the way MSI Center M does.
    /// Reuses the existing command-stream primitives (ReadFwSlotRaw / BuildEepromByteWriteCmd /
    /// BuildSyncToRomCmd / SendRawCmd). Each SET also mirrors MSI's profile.rec (see MsiCenterMProfile)
    /// so Center M stays consistent and doesn't clobber us on its next apply.
    ///
    /// Addresses + encodings reverse-engineered on A2VM fw 0x229 / EX fw 0x414 (2026-07-14) — see
    /// reverse_engineered/RE_MSI_ButtonRemap.md "Tiny Center M — consolidated mapping".
    /// </summary>
    internal partial class ClawButtonMonitor
    {
        // Deadzones / limits — raw percent 0..100.
        private const ushort AddrLSDZ  = 0x022C, AddrLSEDZ = 0x0234, AddrRSDZ = 0x023C, AddrRSEDZ = 0x0244;
        private const ushort AddrLTDZ  = 0x020F, AddrLTEDZ = 0x0210, AddrRTDZ = 0x021E, AddrRTEDZ = 0x021F;
        // Stick swap — factory = all 0; swap on = flag 0x01 + source byte (LS←right 0x02, RS←left 0x01).
        private const ushort AddrLSSwapFlag = 0x022D, AddrLSSwapSrc = 0x0230;
        private const ushort AddrRSSwapFlag = 0x023D, AddrRSSwapSrc = 0x0240;
        // Gyro enable = bit0 of the MTP output byte.
        private const ushort AddrGyroByte0 = 0x0029;

        private readonly object _centerMLock = new object();

        /// <summary>Snapshot of the hardware controller config surfaced to the widget.</summary>
        internal sealed class HwControllerConfig
        {
            public int LSDZ, LSEDZ, RSDZ, RSEDZ;   // stick inner/outer, %
            public int LTDZ, LTEDZ, RTDZ, RTEDZ;   // trigger deadzone/limit, %
            public bool StickSwap;                 // true = MSI stick swap active (disables remaps)
            public bool GyroActive;                // true = firmware gyro enabled
            public bool Valid;                     // false = read failed (device busy / not present)
        }

        private byte ReadByte(ushort addr, out bool ok)
        {
            byte[] d = ReadFwSlotRaw(addr, 1);
            ok = d != null && d.Length >= 1;
            return ok ? d[0] : (byte)0;
        }

        /// <summary>Reads all Tiny-Center-M-relevant bytes in one pass. Valid=false if any read failed.</summary>
        public HwControllerConfig ReadHwControllerConfig()
        {
            lock (_centerMLock)
            {
                var c = new HwControllerConfig();
                bool ok, all = true;
                c.LSDZ  = ReadByte(AddrLSDZ,  out ok); all &= ok;
                c.LSEDZ = ReadByte(AddrLSEDZ, out ok); all &= ok;
                c.RSDZ  = ReadByte(AddrRSDZ,  out ok); all &= ok;
                c.RSEDZ = ReadByte(AddrRSEDZ, out ok); all &= ok;
                c.LTDZ  = ReadByte(AddrLTDZ,  out ok); all &= ok;
                c.LTEDZ = ReadByte(AddrLTEDZ, out ok); all &= ok;
                c.RTDZ  = ReadByte(AddrRTDZ,  out ok); all &= ok;
                c.RTEDZ = ReadByte(AddrRTEDZ, out ok); all &= ok;
                byte lsSwap = ReadByte(AddrLSSwapFlag, out ok); all &= ok;
                byte rsSwap = ReadByte(AddrRSSwapFlag, out ok); all &= ok;
                byte gyro0  = ReadByte(AddrGyroByte0,  out ok); all &= ok;
                c.StickSwap = lsSwap != 0 || rsSwap != 0;
                c.GyroActive = (gyro0 & 0x01) != 0;
                c.Valid = all;
                return c;
            }
        }

        private static byte Clamp100(int v) => (byte)Math.Max(0, Math.Min(100, v));

        /// <summary>
        /// Writes one or more EEPROM bytes (opcode 0x21 len 1 each) followed by a SINGLE SyncToROM —
        /// same batched pattern as the vibration ceiling. Returns true if every write + the sync went out.
        /// </summary>
        private bool WriteBytesAndSync(params (ushort addr, byte value)[] writes)
        {
            lock (_centerMLock)
            {
                if (_cmdDevice == null) return false;
                bool allOk = true;
                foreach (var w in writes)
                {
                    allOk &= SendRawCmd(BuildEepromByteWriteCmd(w.addr, w.value));
                    Thread.Sleep(40);
                }
                bool sync = SendRawCmd(BuildSyncToRomCmd());
                Thread.Sleep(60);
                return allOk && sync;
            }
        }

        /// <summary>Set a single stick/trigger deadzone or limit byte (clamped 0..100) + mirror profile.rec.</summary>
        public bool SetHwDeadzone(string field, int percent)
        {
            byte v = Clamp100(percent);
            ushort addr;
            switch (field)
            {
                case "LSDZ":  addr = AddrLSDZ;  break;
                case "LSEDZ": addr = AddrLSEDZ; break;
                case "RSDZ":  addr = AddrRSDZ;  break;
                case "RSEDZ": addr = AddrRSEDZ; break;
                case "LTDZ":  addr = AddrLTDZ;  break;
                case "LTEDZ": addr = AddrLTEDZ; break;
                case "RTDZ":  addr = AddrRTDZ;  break;
                case "RTEDZ": addr = AddrRTEDZ; break;
                default: Logger.Warn($"[TinyCenterM] SetHwDeadzone: unknown field '{field}'"); return false;
            }
            bool fw = WriteBytesAndSync((addr, v));
            MSI.MsiCenterMProfile.MirrorDeadzone(field, v);
            Logger.Info($"[TinyCenterM] {field} → {v}% (fw={fw})");
            return fw;
        }

        /// <summary>Reset stick deadzones to MSI factory (inner 5, outer 100) + mirror profile.rec.</summary>
        public bool ResetSticks()
        {
            bool fw = WriteBytesAndSync((AddrLSDZ, 5), (AddrLSEDZ, 100), (AddrRSDZ, 5), (AddrRSEDZ, 100));
            MSI.MsiCenterMProfile.MirrorSticksFactory();
            Logger.Info($"[TinyCenterM] Reset sticks to factory (fw={fw})");
            return fw;
        }

        /// <summary>Reset trigger deadzones/limits to MSI factory (deadzone 0, limit 100) + mirror profile.rec.</summary>
        public bool ResetTriggers()
        {
            bool fw = WriteBytesAndSync((AddrLTDZ, 0), (AddrLTEDZ, 100), (AddrRTDZ, 0), (AddrRTEDZ, 100));
            MSI.MsiCenterMProfile.MirrorTriggersFactory();
            Logger.Info($"[TinyCenterM] Reset triggers to factory (fw={fw})");
            return fw;
        }

        /// <summary>Disable the firmware gyro (clear enable bit0, read-modify-write) + mirror MTP.outputType=0.</summary>
        public bool DisableHwGyro()
        {
            byte b0 = ReadByte(AddrGyroByte0, out bool ok);
            if (!ok) { Logger.Warn("[TinyCenterM] DisableHwGyro: could not read gyro byte0"); return false; }
            bool fw = WriteBytesAndSync((AddrGyroByte0, (byte)(b0 & 0xFE)));
            MSI.MsiCenterMProfile.MirrorGyroOff();
            Logger.Info($"[TinyCenterM] HW gyro disabled (byte0 0x{b0:X2}→0x{(byte)(b0 & 0xFE):X2}, fw={fw})");
            return fw;
        }

        /// <summary>Turn MSI stick swap off (zero the 4 swap bytes) + mirror LSS/RSS=false. Swap disables remaps.</summary>
        public bool DisableStickSwap()
        {
            bool fw = WriteBytesAndSync((AddrLSSwapFlag, 0), (AddrLSSwapSrc, 0), (AddrRSSwapFlag, 0), (AddrRSSwapSrc, 0));
            MSI.MsiCenterMProfile.MirrorStickSwapOff();
            Logger.Info($"[TinyCenterM] Stick swap disabled (fw={fw})");
            return fw;
        }
    }
}
