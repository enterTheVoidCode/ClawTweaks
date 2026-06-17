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
    /// valid cores is omitted. Values come straight from LHM (P-Core #N / E-Core #N Clock + Load
    /// sensors); the cap is read from the active power scheme via <see cref="PowerManager"/>.
    /// </summary>
    internal class OSDItemCPUCores : OSDItem
    {
        private readonly CPUCoreClockSensor[] pClocks;
        private readonly CPUCoreClockSensor[] eClocks;
        private readonly CPUCoreLoadSensor[] pLoads;
        private readonly CPUCoreLoadSensor[] eLoads;

        public OSDItemCPUCores(CPUCoreClockSensor[] pClocks, CPUCoreClockSensor[] eClocks,
                               CPUCoreLoadSensor[] pLoads, CPUCoreLoadSensor[] eLoads)
            : base("Cores", "CPUCores", Color.Turquoise)
        {
            this.pClocks = pClocks;
            this.eClocks = eClocks;
            this.pLoads = pLoads;
            this.eLoads = eLoads;
        }

        public override string GetOSDString(int osdLevel)
        {
            var lines = new List<string>();
            // P-core cap = PROCFREQMAX1 (secondary), E-core/all cap = PROCFREQMAX.
            AppendCluster(lines, "P-Cores", pClocks, pLoads, PowerManager.GetCpuFreqCapMHz(true));
            AppendCluster(lines, "E-Cores", eClocks, eLoads, PowerManager.GetCpuFreqCapMHz(false));

            return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
        }

        private void AppendCluster(List<string> lines, string label, CPUCoreClockSensor[] clocks,
                                   CPUCoreLoadSensor[] loads, int capMHz)
        {
            if (clocks == null) return;

            var rows = new List<string>();
            var tc = GetTextColorWithOpacity();
            for (int i = 0; i < clocks.Length; i++)
            {
                float mhz = clocks[i].Value;     // LHM per-core clock in MHz
                if (mhz < 0) continue;           // core not present on this SKU → skip

                float load = (loads != null && i < loads.Length) ? loads[i].Value : -1f;
                string loadStr = load < 0
                    ? "  --%"
                    : $"{(int)Math.Round(load),3}%";
                string clkStr = (mhz / 1000f).ToString("F2", CultureInfo.InvariantCulture);
                rows.Add($"<C={tc}>{loadStr}  {clkStr} GHz");
            }
            if (rows.Count == 0) return;

            // Header: label in the item colour; append the active cap (when set / not unlimited).
            string header = $"<C={ApplyOpacity(colorCode)}>{label}";
            if (capMHz > 0)
                header += $"<C={tc}> · max {capMHz} MHz";
            lines.Add(header);
            lines.AddRange(rows);
        }
    }
}
