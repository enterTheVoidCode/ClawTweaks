using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using XboxGamingBarHelper.Performance.Sensors;
using XboxGamingBarHelper.Power;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// Per-core CPU stats for the Full preset. The P-cores and E-cores are listed in two clusters,
    /// stacked vertically, one core per row: load % on the left, the core's clock (GHz) on the right.
    /// If a max-frequency cap is set (P/E not unlimited) the cluster header shows it, e.g.
    /// "P-Cores · max 1200 MHz". Cores reporting N/A (clock &lt; 0) are skipped; a cluster with no
    /// valid cores is omitted. Clocks come from LHM's hybrid per-core Clock sensors (P-Core #N /
    /// E-Core #N); LOAD comes from LHM's FLAT per-core Load sensors ("CPU Core #1".."CPU Core #N",
    /// P-cores first then E-cores) — they are matched to each cluster by position. The cap is read
    /// from the active power scheme via <see cref="PowerManager"/>.
    /// </summary>
    internal class OSDItemCPUCores : OSDItem
    {
        private readonly CPUCoreClockSensor[] pClocks;
        private readonly CPUCoreClockSensor[] eClocks;
        private readonly CPUCoreLoadSensor[] coreLoads;   // flat "CPU Core #1".. across all cores

        public OSDItemCPUCores(CPUCoreClockSensor[] pClocks, CPUCoreClockSensor[] eClocks,
                               CPUCoreLoadSensor[] coreLoads)
            : base("Cores", "CPUCores", Color.Turquoise)
        {
            this.pClocks = pClocks;
            this.eClocks = eClocks;
            this.coreLoads = coreLoads;
        }

        public override string GetOSDString(int osdLevel)
        {
            var lines = new List<string>();
            // P-core cap = PROCFREQMAX1 (secondary), E-core/all cap = PROCFREQMAX.
            // Flat load slots are physical-core ordered (P-cores first), so the E-cluster's loads start
            // right after the present P-cores: pass the P-core count as the E-cluster's load offset.
            int pCount = AppendCluster(lines, "P-Cores", pClocks, 0, PowerManager.GetCpuFreqCapMHz(true));
            AppendCluster(lines, "E-Cores", eClocks, pCount, PowerManager.GetCpuFreqCapMHz(false));

            return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
        }

        /// <summary>
        /// Emits one cluster's rows and returns the number of present cores in it. <paramref name="loadOffset"/>
        /// is the flat-load index of this cluster's first physical core (0 for P-cores, P-core count for E-cores).
        /// </summary>
        private int AppendCluster(List<string> lines, string label, CPUCoreClockSensor[] clocks,
                                  int loadOffset, int capMHz)
        {
            if (clocks == null) return 0;

            var rows = new List<string>();
            var tc = GetTextColorWithOpacity();
            int present = 0;
            for (int i = 0; i < clocks.Length; i++)
            {
                float mhz = clocks[i].Value;     // LHM per-core clock in MHz
                if (mhz < 0) continue;           // core not present on this SKU → skip

                int flat = loadOffset + present; // physical core index → flat "CPU Core #(flat+1)"
                present++;
                float load = (coreLoads != null && flat < coreLoads.Length) ? coreLoads[flat].Value : -1f;
                string loadStr = load < 0
                    ? "  --%"
                    : $"{(int)Math.Round(load),3}%";
                string clkStr = (mhz / 1000f).ToString("F2", CultureInfo.InvariantCulture);
                rows.Add($"<C={tc}>{loadStr}  {clkStr} GHz");
            }
            if (rows.Count == 0) return 0;

            // Header: label in the item colour; append the active cap (when set / not unlimited).
            string header = $"<C={ApplyOpacity(colorCode)}>{label}";
            if (capMHz > 0)
                header += $"<C={tc}> · max {capMHz} MHz";
            lines.Add(header);
            lines.AddRange(rows);
            return present;
        }
    }
}
