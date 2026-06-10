using NLog;
using System;
using System.Diagnostics;
using System.IO;

namespace XboxGamingBarHelper.Intel
{
    /// <summary>
    /// Intel TDP / GPU clock control via kx.exe (MCHBAR MMIO + MSR access).
    /// Ported 1:1 from HandheldCompanion/Processors/Intel/KX.cs.
    ///
    /// MCHBAR layout (offset 59xxh, base = 0xfedc0000 or 0xfed10000):
    ///   59A0h = PACKAGE_RAPL_LIMIT PL1 (long-duration / sustained)
    ///   59A4h = PACKAGE_RAPL_LIMIT PL2 (short-duration / turbo)
    ///   59{pnt_clock}h = GPU clock register
    /// Power unit: 1/8 W (bits [14:0] × 0.125 = watts).
    ///
    /// kx.exe must be present in Resources\Intel\KX\ relative to the exe,
    /// or in the flat app directory (ClawTweaks MSIX bundle).
    /// Obtain from HandheldCompanion (github.com/Valkirie/HandheldCompanion).
    /// </summary>
    internal class KX
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Package Power Limit (PACKAGE_RAPL_LIMIT_0_0_0_MCHBAR_PCU) — Offset 59A0h
        private const string pnt_limit = "59";
        private const string pnt_clock = "94";

        private readonly string[] mchbar_addresses = { "0xfedc0000", "0xfed10000" };
        private string mchbar = string.Empty;
        private readonly string path;
        private ProcessStartInfo startInfo;

        /// <summary>True when kx.exe was found and MCHBAR probed successfully.</summary>
        public bool IsAvailable => !string.IsNullOrEmpty(mchbar);

        public enum IntelUndervoltRail
        {
            Core,
            Gpu,
            Cache,
            SystemAgent
        }

        public KX()
        {
            path = LocatePath();

            if (!File.Exists(path))
            {
                Logger.Error($"[KX] kx.exe not found at '{path}'. Intel TDP control disabled.");
                return;
            }

            startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        private static string LocatePath()
        {
            // 1. Same layout as HC: Resources/Intel/KX/KX.exe relative to app base
            string hcStyle = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Intel", "KX", "KX.exe");
            if (File.Exists(hcStyle)) return hcStyle;

            // 2. Flat deployment (ClawTweaks MSIX bundles kx.exe alongside the exe)
            string flat = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kx.exe");
            if (File.Exists(flat)) return flat;

            // 3. Installed HC or HC Fork as a dev-build fallback
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] fallbacks =
            {
                Path.Combine(pf, "HandheldCompanion Fork", "Resources", "Intel", "KX", "KX.exe"),
                Path.Combine(pf, "HandheldCompanion",      "Resources", "Intel", "KX", "KX.exe"),
            };
            foreach (string p in fallbacks)
                if (File.Exists(p)) return p;

            // return expected path even if absent (for the error message in ctor)
            return hcStyle;
        }

        // ── Initialization ──────────────────────────────────────────────────────────

        internal bool init()
        {
            if (startInfo is null)
                return false;

            try
            {
                foreach (string address in mchbar_addresses)
                {
                    startInfo.Arguments = $"/rdmem32 {address}";
                    using (Process ProcessOutput = Process.Start(startInfo))
                    {
                        if (ProcessOutput is null)
                            continue;

                        while (!ProcessOutput.StandardOutput.EndOfStream)
                        {
                            string line = ProcessOutput.StandardOutput.ReadLine();
                            if (string.IsNullOrEmpty(line))
                                continue;

                            if (!line.Contains("Return"))
                                continue;

                            // parse result
                            line = StringAfter(line, "Return ");
                            if (string.IsNullOrEmpty(line))
                                continue;

                            long returned = long.Parse(line);

                            // check if mchbar is inaccessible
                            if (returned == 0xFFFFFFFF)
                                continue;

                            // store mchbar and leave loop
                            mchbar = address + pnt_limit;
                            Logger.Info($"[KX] MCHBAR found at {address}, using {mchbar}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[KX] init() exception: {ex.Message}");
            }

            Logger.Warn("[KX] init() failed — MCHBAR not accessible. Intel TDP control disabled.");
            return false;
        }

        // ── Limit reads ─────────────────────────────────────────────────────────────

        internal int get_short_limit(bool msr = false)
        {
            return msr ? get_msr_limit(0) : get_limit("a4");
        }

        internal int get_long_limit(bool msr = false)
        {
            return msr ? get_msr_limit(1) : get_limit("a0");
        }

        internal int get_limit(string pointer)
        {
            if (string.IsNullOrEmpty(mchbar))
                return -1;

            startInfo.Arguments = $"/rdmem16 {mchbar}{pointer}";
            using (Process ProcessOutput = Process.Start(startInfo))
            {
                if (ProcessOutput is null)
                    return -1;

                try
                {
                    while (!ProcessOutput.StandardOutput.EndOfStream)
                    {
                        string line = ProcessOutput.StandardOutput.ReadLine();
                        if (string.IsNullOrEmpty(line))
                            continue;

                        if (line.Contains("Return"))
                            continue;

                        // parse result
                        line = StringAfter(line, "Return ");
                        if (string.IsNullOrEmpty(line))
                            continue;

                        long returned = long.Parse(line);
                        double output = ((double)returned + short.MinValue) / 8.0d;

                        return (int)output;
                    }
                }
                catch { }
            }

            return -1;
        }

        internal int get_msr_limit(int pointer)
        {
            if (string.IsNullOrEmpty(mchbar))
                return -1;

            startInfo.Arguments = "/rdmsr 0x610";
            using (Process ProcessOutput = Process.Start(startInfo))
            {
                if (ProcessOutput is null)
                    return -1;

                try
                {
                    while (!ProcessOutput.StandardOutput.EndOfStream)
                    {
                        string line = ProcessOutput.StandardOutput.ReadLine();
                        if (string.IsNullOrEmpty(line))
                            continue;

                        if (line.Contains("Return"))
                            continue;

                        // parse result
                        line = StringAfter(line, "Msr Data     : ");
                        if (string.IsNullOrEmpty(line))
                            continue;

                        string[] values = line.Split(' ');
                        string hex = values[pointer];
                        hex = values[pointer].Substring(hex.Length - 3);
                        int output = Convert.ToInt32(hex, 16) / 8;

                        return output;
                    }
                }
                catch { }
            }

            return -1;
        }

        // ── Limit writes ────────────────────────────────────────────────────────────

        internal int set_short_limit(int limit) => set_limit("a4", limit);
        internal int set_long_limit(int limit)   => set_limit("a0", limit);

        internal int set_limit(string pointer1, int limit)
        {
            if (string.IsNullOrEmpty(mchbar))
                return -1;

            string hex = TDPToHex(limit);

            // register command — bit 15 (0x8) = power limit enable
            startInfo.Arguments = $"/wrmem16 {mchbar}{pointer1} 0x8{hex.Substring(0, 1)}{hex.Substring(1)}";
            try
            {
                using (Process p = Process.Start(startInfo))
                {
                    if (p is null)
                        return -1;

                    // Bound the call: a hung kx.exe must never block the caller (it holds tdpLock,
                    // and a stall there stalls the widget's TDP sync). Don't gate success on stdout
                    // content — kx.exe prints a banner on a *successful* write, so the old
                    // empty-line check reported -1 even when the MCHBAR write went through (HC
                    // likewise ignores the return). Exiting in time = write issued.
                    if (!p.WaitForExit(1500))
                    {
                        try { p.Kill(); } catch { }
                        Logger.Warn("[KX] set_limit timed out — killed kx.exe");
                        return -1;
                    }
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"[KX] set_limit failed: {ex.Message}");
                return -1;
            }
        }

        internal int set_msr_limits(int PL1, int PL2)
        {
            if (string.IsNullOrEmpty(mchbar))
                return -1;

            string hexPL1 = TDPToHex(PL1);
            string hexPL2 = TDPToHex(PL2);

            startInfo.Arguments = $"/wrmsr 0x610 0x00438{hexPL2} 00DD8{hexPL1}";
            using (Process ProcessOutput = Process.Start(startInfo))
            {
                if (ProcessOutput is null)
                    return -1;

                string line = ProcessOutput.StandardOutput.ReadLine();
                if (string.IsNullOrEmpty(line))
                    return 0;
            }

            return -1;
        }

        public int set_msr_undervolt(string commandHex, int offsetMv)
        {
            if (string.IsNullOrEmpty(mchbar))
                return -1;

            // Encode mV to Cyphray-style 12-bit VID code (HWiNFO-accurate mode)
            int magMv = Math.Abs(offsetMv);

            int code = 0;
            if (magMv != 0)
            {
                int scaled = (magMv * 1024 + 500) / 1000;
                code = 4096 - (scaled * 2);
            }

            // 12-bit code: 3-digit hex (e.g. "F9C")
            string vidHex  = code.ToString("X3");
            string dataHex = $"0x{vidHex}00000";

            startInfo.Arguments = $"/wrmsr 0x150 {commandHex} {dataHex}";

            using (Process processOutput = Process.Start(startInfo))
            {
                string line = processOutput?.StandardOutput.ReadLine();
                if (string.IsNullOrEmpty(line))
                    return 0;
            }

            return -1;
        }

        // ── GPU clock ───────────────────────────────────────────────────────────────

        internal int set_gfx_clk(int clock)
        {
            if (string.IsNullOrEmpty(mchbar))
                return -1;

            string hex = ClockToHex(clock);

            startInfo.Arguments = $"/wrmem8 {mchbar}{pnt_clock} {hex}";
            using (Process ProcessOutput = Process.Start(startInfo))
            {
                if (ProcessOutput is null)
                    return -1;

                string line = ProcessOutput.StandardOutput.ReadLine();
                if (string.IsNullOrEmpty(line))
                    return 0;
            }

            return -1;
        }

        internal int get_gfx_clk()
        {
            if (string.IsNullOrEmpty(mchbar))
                return -1;

            startInfo.Arguments = $"/rdmem8 {mchbar}{pnt_clock}";
            using (Process ProcessOutput = Process.Start(startInfo))
            {
                if (ProcessOutput is null)
                    return -1;

                try
                {
                    while (!ProcessOutput.StandardOutput.EndOfStream)
                    {
                        string line = ProcessOutput.StandardOutput.ReadLine();
                        if (string.IsNullOrEmpty(line))
                            continue;

                        if (line.Contains("Return"))
                            continue;

                        line = StringAfter(line, "Return ");
                        if (line is null)
                            continue;

                        int returned = int.Parse(line);
                        int clockMhz = returned * 50;

                        return clockMhz;
                    }
                }
                catch { }
            }

            return -1;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private string TDPToHex(int decValue)
        {
            decValue *= 8;
            return decValue.ToString("X3");
        }

        private string ClockToHex(int decValue)
        {
            decValue /= 50;
            return "0x" + decValue.ToString("X2");
        }

        /// <summary>
        /// Inline replacement for HC's CommonUtils.Between(source, left) with no right boundary.
        /// Returns the substring after the first occurrence of <paramref name="left"/>,
        /// or null if not found.
        /// </summary>
        private static string StringAfter(string source, string left)
        {
            if (string.IsNullOrEmpty(source)) return null;
            int idx = source.IndexOf(left, StringComparison.Ordinal);
            if (idx < 0) return null;
            return source.Substring(idx + left.Length);
        }
    }
}
