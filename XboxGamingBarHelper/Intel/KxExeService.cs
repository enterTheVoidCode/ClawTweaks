using NLog;
using System;
using System.Diagnostics;
using System.IO;

namespace XboxGamingBarHelper.Intel
{
    /// <summary>
    /// Intel TDP control via kx.exe (MCHBAR physical memory access).
    /// Supports Intel Core Ultra 200V series (Lunar Lake) PL1/PL2 power limits.
    ///
    /// kx.exe must be present in the same directory as XboxGamingBarHelper.exe.
    /// Obtain from HandheldCompanion (github.com/Valkirie/HandheldCompanion) — it ships kx.exe as a redistributable.
    ///
    /// MCHBAR layout for Lunar Lake (0xFEDC0000 base):
    ///   0xFEDC59A0 = PACKAGE_RAPL_LIMIT PL2 (short-duration / turbo)
    ///   0xFEDC59A4 = PACKAGE_RAPL_LIMIT PL1 (long-duration / sustained)
    ///   Power unit: 1/8 W (bits [14:0] * 0.125 = watts)
    /// </summary>
    internal class KxExeService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const uint MCHBARBase   = 0xFEDC0000u;
        private const uint PL2Offset    = 0x59A0u;
        private const uint PL1Offset    = 0x59A4u;

        // Bits [14:0] are the power limit field; upper bits are control/lock/time
        private const uint PowerLimitMask = 0x7FFFu;
        // 1 unit = 1/8 W
        private const double PowerUnitW  = 0.125;

        private readonly string _kxPath;

        public bool IsAvailable { get; }

        public KxExeService()
        {
            _kxPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kx.exe");
            IsAvailable = File.Exists(_kxPath);
            Logger.Info($"KxExeService: kx.exe={_kxPath}, available={IsAvailable}");
        }

        /// <summary>
        /// Sets Intel CPU package power limits.
        /// Enforces PL2 >= PL1 + 1W per HC / MSI Claw conventions.
        /// </summary>
        public bool SetPowerLimits(int pl1Watts, int pl2Watts)
        {
            if (!IsAvailable)
            {
                Logger.Warn("KxExeService: kx.exe not found — cannot set TDP");
                return false;
            }

            // PL2 must always be at least 1W above PL1
            if (pl2Watts < pl1Watts + 1)
                pl2Watts = pl1Watts + 1;

            Logger.Info($"KxExeService: SetPowerLimits PL1={pl1Watts}W PL2={pl2Watts}W");

            // Write PL2 first (turbo), then PL1 (sustained) to avoid transient violations
            bool pl2Ok = WritePowerLimit(MCHBARBase + PL2Offset, pl2Watts);
            bool pl1Ok = WritePowerLimit(MCHBARBase + PL1Offset, pl1Watts);

            if (pl1Ok && pl2Ok)
                Logger.Info("KxExeService: TDP set successfully");
            else
                Logger.Warn($"KxExeService: partial failure — PL1={pl1Ok} PL2={pl2Ok}");

            return pl1Ok && pl2Ok;
        }

        /// <summary>
        /// Reads current PL1 (sustained) limit in whole watts. Returns -1 on failure.
        /// </summary>
        public int ReadPL1Watts() => ReadPowerLimitWatts(MCHBARBase + PL1Offset);

        /// <summary>
        /// Reads current PL2 (turbo) limit in whole watts. Returns -1 on failure.
        /// </summary>
        public int ReadPL2Watts() => ReadPowerLimitWatts(MCHBARBase + PL2Offset);

        // ── Private helpers ─────────────────────────────────────────────────────────

        private bool WritePowerLimit(uint physAddr, int watts)
        {
            try
            {
                // Read-modify-write: preserve the upper control bits (clamp, time window, lock)
                uint current = ReadRegister(physAddr);
                if (current == uint.MaxValue)
                {
                    // Fall back: construct a minimal register value with power limit field only
                    current = 0u;
                    Logger.Warn($"KxExeService: RMW read failed at 0x{physAddr:X8}; writing limit field only");
                }

                uint limitRaw = (uint)Math.Round(watts / PowerUnitW) & PowerLimitMask;
                uint newValue = (current & ~PowerLimitMask) | limitRaw;

                return WriteRegister(physAddr, newValue);
            }
            catch (Exception ex)
            {
                Logger.Error($"KxExeService: WritePowerLimit exception at 0x{physAddr:X8}: {ex.Message}");
                return false;
            }
        }

        private int ReadPowerLimitWatts(uint physAddr)
        {
            try
            {
                uint raw = ReadRegister(physAddr);
                if (raw == uint.MaxValue) return -1;
                return (int)Math.Round((raw & PowerLimitMask) * PowerUnitW);
            }
            catch (Exception ex)
            {
                Logger.Warn($"KxExeService: ReadPowerLimitWatts exception at 0x{physAddr:X8}: {ex.Message}");
                return -1;
            }
        }

        /// <summary>Reads a 32-bit DWORD from physical memory via kx.exe /rm.</summary>
        private uint ReadRegister(uint physAddr)
        {
            string output = RunKx($"/rm 0x{physAddr:X8}");
            if (output == null) return uint.MaxValue;

            // Expected output: "0xFEDC59A4 = 0x000080C8" — parse after '='
            int eqIdx = output.LastIndexOf('=');
            if (eqIdx < 0) { Logger.Warn($"KxExeService: /rm parse failed: '{output}'"); return uint.MaxValue; }

            string valuePart = output.Substring(eqIdx + 1).Trim();
            if (valuePart.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                valuePart = valuePart.Substring(2);

            return uint.TryParse(valuePart, System.Globalization.NumberStyles.HexNumber, null, out uint v)
                ? v
                : uint.MaxValue;
        }

        /// <summary>Writes a 32-bit DWORD to physical memory via kx.exe /wm.</summary>
        private bool WriteRegister(uint physAddr, uint value)
        {
            string output = RunKx($"/wm 0x{physAddr:X8} 0x{value:X8}");
            return output != null; // null = process error; empty string = success (kx.exe writes nothing on success)
        }

        private string RunKx(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = _kxPath,
                    Arguments              = args,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                    WorkingDirectory       = Path.GetDirectoryName(_kxPath)
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) { Logger.Error("KxExeService: Process.Start returned null"); return null; }

                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();

                    if (!proc.WaitForExit(2000))
                    {
                        Logger.Error($"KxExeService: kx.exe timeout on '{args}'");
                        try { proc.Kill(); } catch { }
                        return null;
                    }

                    if (proc.ExitCode != 0)
                    {
                        Logger.Warn($"KxExeService: kx.exe '{args}' exit={proc.ExitCode} stderr='{stderr.Trim()}'");
                        return null;
                    }

                    return stdout.Trim();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"KxExeService: RunKx('{args}') exception: {ex.Message}");
                return null;
            }
        }
    }
}
