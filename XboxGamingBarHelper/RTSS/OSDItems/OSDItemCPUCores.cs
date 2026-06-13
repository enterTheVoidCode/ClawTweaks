using System.Drawing;
using System.Text;
using XboxGamingBarHelper.Performance.Sensors;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// Per-core CPU clocks for the Full preset: one line for the P-Cores, one for the E-Cores,
    /// each core's clock in MHz. Cores reporting N/A (sensor value &lt; 0) are skipped; a cluster
    /// with no valid cores is omitted entirely. Values come straight from LHM (P-Core #N / E-Core #N
    /// Clock sensors) — the same source as the other CPU stats. Renders two physical lines via an
    /// embedded "\n" (Full preset is a 1-column vertical list, so each line is its own row).
    /// </summary>
    internal class OSDItemCPUCores : OSDItem
    {
        private readonly CPUCoreClockSensor[] pCores;
        private readonly CPUCoreClockSensor[] eCores;

        public OSDItemCPUCores(CPUCoreClockSensor[] pCores, CPUCoreClockSensor[] eCores)
            : base("Cores", "CPUCores", Color.Turquoise)
        {
            this.pCores = pCores;
            this.eCores = eCores;
        }

        public override string GetOSDString(int osdLevel)
        {
            string pLine = BuildClusterLine("P-Cores", pCores);
            string eLine = BuildClusterLine("E-Cores", eCores);

            if (pLine == null && eLine == null)
                return string.Empty;
            if (pLine != null && eLine != null)
                return pLine + "\n" + eLine;
            return pLine ?? eLine;
        }

        private string BuildClusterLine(string label, CPUCoreClockSensor[] cores)
        {
            if (cores == null)
                return null;

            var tc = GetTextColorWithOpacity();
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var core in cores)
            {
                float v = core.Value;
                if (v < 0) continue;
                if (shown > 0) sb.Append(' ');
                sb.Append((int)System.Math.Round(v));
                shown++;
            }
            if (shown == 0)
                return null;

            // Label in the item colour, values in the text colour — matches the other OSD items.
            return $"<C={ApplyOpacity(colorCode)}>{label}<C={tc}> {sb}";
        }
    }
}
